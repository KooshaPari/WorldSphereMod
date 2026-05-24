using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class SsaoPostFxInvariantsTests
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
    public void Core_ApplyPhaseToggle_wires_PostFX_SSAO_and_SSGI_to_runtime_ApplySetting()
    {
        var core = ReadSourceFile("WorldSphereMod/Code/Core.cs");
        var applyBody = ExtractMethodBody(core, "public static void ApplyPhaseToggle(string flagName, bool newValue)");

        applyBody.Should().Contain("nameof(SavedSettings.PostFX)",
            "URP volume post-FX must be routed through ApplyPhaseToggle");
        applyBody.Should().Contain("PostFxController.ApplySetting(newValue)",
            "PostFX toggle must create/destroy the URP Volume immediately");

        applyBody.Should().Contain("nameof(SavedSettings.SSAOEnabled)",
            "built-in SSAO pass must be routed through ApplyPhaseToggle");
        applyBody.Should().Contain("ScreenSpaceAO.ApplySetting(newValue)",
            "SSAOEnabled toggle must attach/detach the OnRenderImage pass immediately");

        applyBody.Should().Contain("nameof(SavedSettings.SSAOQuality)",
            "SSAO quality enum must be routed through ApplyPhaseToggle");
        applyBody.Should().Contain("ScreenSpaceAO.ApplyQualitySetting()",
            "SSAOQuality changes must refresh live material parameters");

        applyBody.Should().Contain("nameof(SavedSettings.SSGIEnabled)",
            "built-in SSGI pass must be routed through ApplyPhaseToggle");
        applyBody.Should().Contain("ScreenSpaceGI.ApplySetting(newValue)",
            "SSGIEnabled toggle must attach/detach the OnRenderImage pass immediately");
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
    public void Mod_init_and_WorldSphereTab_wire_SSAO_toggle_end_to_end()
    {
        var mod = ReadSourceFile("WorldSphereMod/Code/Mod.cs");
        var tab = ReadSourceFile("WorldSphereMod/Code/WorldSphereTab.cs");
        var settings = ReadSourceFile("WorldSphereMod/Code/SavedSettings.cs");

        settings.Should().Contain("public bool SSAOEnabled = true",
            "SSAO should default ON for Phase 9");
        settings.Should().Contain("public SsaoQuality SSAOQuality = SsaoQuality.Medium",
            "SSAO quality enum must live on SavedSettings");

        mod.Should().Contain("ScreenSpaceAO.ApplySetting(Core.savedSettings != null && Core.savedSettings.SSAOEnabled)",
            "Mod world-init coroutine must apply persisted SSAOEnabled after scene transitions");

        tab.Should().Contain("\"ssao_enabled\"",
            "3D Phases window must expose the SSAO toggle button");
        tab.Should().Contain("Core.savedSettings.SSAOEnabled",
            "SSAO button must bind to SavedSettings.SSAOEnabled");
        tab.Should().Contain("nameof(SavedSettings.SSAOEnabled)",
            "Reset-to-defaults must re-apply SSAO runtime state");
    }

    [Fact]
    public void ScreenSpaceAO_maps_SSAOQuality_and_respects_enabled_gate_in_OnRenderImage()
    {
        var ssao = ReadSourceFile("WorldSphereMod/Code/PostFx/ScreenSpaceAO.cs");

        ssao.Should().Contain("Core.savedSettings.SSAOQuality switch",
            "quality profile must read SavedSettings.SSAOQuality");
        ssao.Should().Contain("SsaoQuality.Low => Quality.Low",
            "Low enum value must map to kernel sample tier");
        ssao.Should().Contain("SsaoQuality.High => Quality.High",
            "High enum value must map to kernel sample tier");

        var onRenderBody = ExtractMethodBody(ssao, "void OnRenderImage(RenderTexture source, RenderTexture destination)");
        onRenderBody.Should().Contain("!Core.savedSettings.SSAOEnabled",
            "OnRenderImage must passthrough when SSAO is disabled without waiting for component teardown");
    }

    [Fact]
    public void PostFxController_manages_URP_volume_only_not_SSAO_pass()
    {
        var postFx = ReadSourceFile("WorldSphereMod/Code/Fx/PostFxController.cs");

        postFx.Should().Contain("[Phase(nameof(SavedSettings.PostFX))]",
            "PostFxController must remain gated on PostFX SavedSettings flag");
        postFx.Should().Contain("BloomTypeName",
            "PostFxController should configure URP bloom override");
        postFx.Should().NotContain("SSAO",
            "SSAO is a separate built-in OnRenderImage pass, not a URP Volume override");
        postFx.Should().NotContain("ScreenSpaceAO",
            "PostFxController must not reference the ScreenSpaceAO MonoBehaviour");
    }
}
