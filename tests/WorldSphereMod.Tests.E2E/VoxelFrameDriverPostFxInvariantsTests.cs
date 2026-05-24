using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// VoxelFrameDriver PostFX/SSAO reconciler wiring — change-only ApplySetting in TickPerFrame,
/// separate PostFxController (URP volume) vs ScreenSpaceAO (OnRenderImage). Complements
/// <see cref="SsaoPostFxInvariantsTests"/> which covers ApplyPhaseToggle and component internals.
/// </summary>
public sealed class VoxelFrameDriverPostFxInvariantsTests
{
    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    static string ReadSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    static string ExtractMethodBody(string source, string signature)
    {
        int headerIndex = source.IndexOf(signature, StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0, $"method signature should exist: {signature}");

        int openBrace = source.IndexOf('{', headerIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0, "method must open with a '{'");

        int depth = 0;
        for (int i = openBrace; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '{')
            {
                depth++;
                continue;
            }

            if (c != '}')
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return source.Substring(openBrace + 1, i - openBrace - 1);
            }
        }

        throw new InvalidOperationException("Unbalanced braces while extracting method body");
    }

    static int IndexOfOrFail(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"{needle} must appear in TickPerFrame");
        return index;
    }

    [Fact]
    public void VoxelFrameDriver_TickPerFrame_reconciles_PostFX_SSAO_and_SSGI_without_per_frame_spam()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var tickBody = ExtractMethodBody(voxelRender, "public static void TickPerFrame()");

        tickBody.Should().Contain("_lastAppliedPostFX",
            "PostFX must track last applied value for change-only reconciliation");
        tickBody.Should().Contain("PostFxController.ApplySetting(currentPostFX)",
            "PostFX reconciler must call ApplySetting when SavedSettings diverges");

        tickBody.Should().Contain("_lastAppliedSSAOEnabled",
            "SSAOEnabled must track last applied value for change-only reconciliation");
        tickBody.Should().Contain("ScreenSpaceAO.ApplySetting(currentSSAO)",
            "SSAO reconciler must call ApplySetting when SavedSettings diverges");

        tickBody.Should().Contain("_lastAppliedSSGIEnabled",
            "SSGIEnabled must track last applied value for change-only reconciliation");
        tickBody.Should().Contain("ScreenSpaceGI.ApplySetting(currentSSGI)",
            "SSGI reconciler must call ApplySetting when SavedSettings diverges");

        tickBody.Should().Contain("ScreenSpaceAO.EnsureCreated()",
            "late-bound main camera must retry SSAO component creation");
        tickBody.Should().Contain("ScreenSpaceGI.EnsureCreated()",
            "late-bound main camera must retry SSGI component creation");
    }

    [Fact]
    public void VoxelFrameDriver_TickPerFrame_separates_PostFxController_from_ScreenSpaceAO_reconcilers()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var tickBody = ExtractMethodBody(voxelRender, "public static void TickPerFrame()");

        int postFxReconcile = IndexOfOrFail(tickBody, "PostFxController.ApplySetting(currentPostFX)");
        int ssaoReconcile = IndexOfOrFail(tickBody, "ScreenSpaceAO.ApplySetting(currentSSAO)");
        int ssgiReconcile = IndexOfOrFail(tickBody, "ScreenSpaceGI.ApplySetting(currentSSGI)");

        postFxReconcile.Should().BeLessThan(ssaoReconcile,
            "URP volume PostFX reconciler should precede built-in SSAO pass wiring");
        ssaoReconcile.Should().BeLessThan(ssgiReconcile,
            "SSAO and SSGI reconcilers stay as separate OnRenderImage passes");

        var postFxGuard = tickBody.Substring(
            tickBody.IndexOf("if (currentPostFX != _lastAppliedPostFX)", StringComparison.Ordinal),
            ssaoReconcile - tickBody.IndexOf("if (currentPostFX != _lastAppliedPostFX)", StringComparison.Ordinal));
        postFxGuard.Should().NotContain("ScreenSpaceAO",
            "PostFxController reconciler must not reference ScreenSpaceAO");
        postFxGuard.Should().NotContain("ScreenSpaceGI",
            "PostFxController reconciler must not reference ScreenSpaceGI");

        var ssaoGuard = tickBody.Substring(
            tickBody.IndexOf("if (currentSSAO != _lastAppliedSSAOEnabled)", StringComparison.Ordinal),
            ssgiReconcile - tickBody.IndexOf("if (currentSSAO != _lastAppliedSSAOEnabled)", StringComparison.Ordinal));
        ssaoGuard.Should().NotContain("PostFxController",
            "ScreenSpaceAO reconciler must not reference PostFxController URP volume path");
    }

    [Fact]
    public void VoxelFrameDriver_LateUpdate_does_not_reconcile_PostFX_or_SSAO()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var lateBody = ExtractMethodBody(voxelRender, "void LateUpdate()");

        lateBody.Should().NotContain("PostFxController.ApplySetting",
            "PostFX reconciler belongs in TickPerFrame pre-emit hook, not LateUpdate flush");
        lateBody.Should().NotContain("ScreenSpaceAO.ApplySetting",
            "SSAO reconciler belongs in TickPerFrame pre-emit hook, not LateUpdate flush");
        lateBody.Should().NotContain("ScreenSpaceGI.ApplySetting",
            "SSGI reconciler belongs in TickPerFrame pre-emit hook, not LateUpdate flush");
        lateBody.Should().NotContain("EnsureCreated()",
            "camera-bound post-FX component creation runs from TickPerFrame camera lookup");
    }

    [Fact]
    public void VoxelFrameDriver_PostFx_reconciler_runs_from_BridgeSurvival_TickPerFrame_not_LateUpdate()
    {
        var bridgeTick = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgePerFrameTick.cs");
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        var survivalRunBody = ExtractMethodBody(bridgeTick, "public static void Run(bool runVoxelFrame)");
        survivalRunBody.Should().Contain("VoxelFrameDriver.TickPerFrame()",
            "MapBox.renderStuff survival hook must invoke pre-emit voxel frame driver");

        var tickBody = ExtractMethodBody(voxelRender, "public static void TickPerFrame()");
        tickBody.Should().Contain("PostFxController.ApplySetting(currentPostFX)",
            "PostFX reconciler must live in the TickPerFrame path wired from BridgeSurvival");
        tickBody.Should().Contain("ScreenSpaceAO.ApplySetting(currentSSAO)",
            "SSAO reconciler must live in the TickPerFrame path wired from BridgeSurvival");

        var lateBody = ExtractMethodBody(voxelRender, "void LateUpdate()");
        lateBody.Should().Contain("VoxelRender.Flush()",
            "LateUpdate remains flush-only after emit postfixes");
    }
}
