using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// VoxelFrameDriver PostFX reconciler wiring — change-only WSM3DPostStack.ApplySetting
/// in TickPerFrame. Complements <see cref="SsaoPostFxInvariantsTests"/> which covers
/// ApplyPhaseToggle and component internals.
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

    [Fact]
    public void VoxelFrameDriver_TickPerFrame_reconciles_PostFX_via_WSM3DPostStack()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var tickBody = ExtractMethodBody(voxelRender, "public static void TickPerFrame()");

        tickBody.Should().Contain("_lastAppliedPostFX",
            "PostFX must track last applied value for change-only reconciliation");
        tickBody.Should().Contain("WSM3DPostStack.ApplySetting(currentPostFX)",
            "PostFX reconciler must route to unified stack");

        tickBody.Should().Contain("_lastAppliedSSAOEnabled",
            "SSAOEnabled must track last applied value for change-only reconciliation");
        tickBody.Should().Contain("WSM3DPostStack.RefreshMaterials()",
            "sub-pass reconciler must refresh unified stack");

        tickBody.Should().Contain("_lastAppliedSSGIEnabled",
            "SSGIEnabled must track last applied value for change-only reconciliation");
    }

    [Fact]
    public void VoxelFrameDriver_TickPerFrame_uses_unified_stack_not_individual_passes()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var tickBody = ExtractMethodBody(voxelRender, "public static void TickPerFrame()");

        tickBody.Should().NotContain("PostFxController.ApplySetting",
            "PostFX reconciler should route to WSM3DPostStack, not old URP controller");
        tickBody.Should().NotContain("ScreenSpaceAO.ApplySetting",
            "SSAO reconciler should route to WSM3DPostStack.RefreshMaterials");
        tickBody.Should().NotContain("ScreenSpaceGI.ApplySetting",
            "SSGI reconciler should route to WSM3DPostStack.RefreshMaterials");
    }

    [Fact]
    public void VoxelFrameDriver_LateUpdate_does_not_reconcile_PostFX()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var lateBody = ExtractMethodBody(voxelRender, "void LateUpdate()");

        lateBody.Should().NotContain("WSM3DPostStack",
            "PostFX reconciler belongs in TickPerFrame, not LateUpdate flush");
        lateBody.Should().NotContain("PostFxController.ApplySetting",
            "PostFX reconciler belongs in TickPerFrame pre-emit hook, not LateUpdate flush");
        lateBody.Should().NotContain("ScreenSpaceAO.ApplySetting",
            "SSAO reconciler belongs in TickPerFrame pre-emit hook, not LateUpdate flush");
    }

    [Fact]
    public void VoxelFrameDriver_PostFx_reconciler_runs_from_BridgeSurvival_TickPerFrame()
    {
        var bridgeTick = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgePerFrameTick.cs");
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        var survivalRunBody = ExtractMethodBody(bridgeTick, "public static void Run(bool runVoxelFrame)");
        survivalRunBody.Should().Contain("VoxelFrameDriver.TickPerFrame()",
            "MapBox.renderStuff survival hook must invoke pre-emit voxel frame driver");

        var tickBody = ExtractMethodBody(voxelRender, "public static void TickPerFrame()");
        tickBody.Should().Contain("WSM3DPostStack.ApplySetting(currentPostFX)",
            "PostFX reconciler must live in the TickPerFrame path wired from BridgeSurvival");

        var lateBody = ExtractMethodBody(voxelRender, "void LateUpdate()");
        lateBody.Should().Contain("VoxelRender.Flush()",
            "LateUpdate remains flush-only after emit postfixes");
    }
}
