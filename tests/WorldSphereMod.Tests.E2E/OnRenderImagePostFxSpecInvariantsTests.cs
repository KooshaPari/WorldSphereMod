using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for docs/specs/onrenderimage-postfx-spec.md — documents the
/// split OnRenderImage passes shipped today vs the unified WSM3DPostStack target.
/// </summary>
public sealed class OnRenderImagePostFxSpecInvariantsTests
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

    static string ReadSource(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue($"source file must exist at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Spec_documents_implementation_status_and_split_stack_gap()
    {
        var spec = ReadSource(@"docs/specs/onrenderimage-postfx-spec.md");

        spec.Should().Contain("## Implementation status",
            "spec must record shipped vs target so Tier-2 work is traceable");
        spec.Should().Contain("WSM3DPostStack",
            "unified stack type remains the spec target");
        spec.Should().Contain("**Not shipped**",
            "status table must call out gaps explicitly");
        spec.Should().Contain("ScreenSpaceAO",
            "SSAO OnRenderImage pass is part of current architecture");
        spec.Should().Contain("PostFxController",
            "URP volume path must be documented as partial/degraded in WB");
        spec.Should().Contain("OnRenderImagePostFxSpecInvariantsTests",
            "spec must point maintainers at e2e guardrails");
    }

    [Fact]
    public void Unified_WSM3DPostStack_is_not_shipped_yet()
    {
        var root = FindRepoRoot();
        var codeDir = Path.Combine(root, "WorldSphereMod", "Code");

        foreach (var file in Directory.EnumerateFiles(codeDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain("class WSM3DPostStack",
                $"unified stack belongs in a future change, not {file}");
        }
    }

    [Fact]
    public void Shipped_OnRenderImage_passes_exist_with_spec_shaders()
    {
        ReadSource(@"WorldSphereMod/Code/PostFx/ScreenSpaceAO.cs")
            .Should().Contain("void OnRenderImage(RenderTexture source, RenderTexture destination)");
        ReadSource(@"WorldSphereMod/Code/PostFx/ScreenSpaceGI.cs")
            .Should().Contain("void OnRenderImage(RenderTexture source, RenderTexture destination)");
        ReadSource(@"WorldSphereMod/Code/Lighting/ColorGradingLUT.cs")
            .Should().Contain("void OnRenderImage(RenderTexture source, RenderTexture destination)");

        File.Exists(Path.Combine(FindRepoRoot(), @"WorldSphereMod/Resources/Shaders/ScreenSpaceAO.shader"))
            .Should().BeTrue("SSAO shader must ship under Resources for BRP fallback");
        File.Exists(Path.Combine(FindRepoRoot(), @"WorldSphereMod/AssetBundles/Shaders/ScreenSpaceAO.shader"))
            .Should().BeTrue("SSAO shader must ship in AssetBundles for bake pipeline");
    }

    [Fact]
    public void PostFxController_remains_URP_volume_path_separate_from_OnRenderImage_passes()
    {
        var postFx = ReadSource(@"WorldSphereMod/Code/Fx/PostFxController.cs");

        postFx.Should().Contain("UnityEngine.Rendering.Volume",
            "PostFX flag still targets reflective URP Volume setup");
        postFx.Should().Contain("renderPostProcessing",
            "camera hook must remain documented for URP path");
        postFx.Should().NotContain("OnRenderImage",
            "URP controller must not duplicate built-in BRP passes");
        postFx.Should().NotContain("ScreenSpaceAO",
            "SSAO stays on ScreenSpaceAO MonoBehaviour per status table");
    }

    [Fact]
    public void Core_routes_ColorGradingLut_to_LUT_OnRenderImage_pass()
    {
        var core = ReadSource(@"WorldSphereMod/Code/Core.cs");

        core.Should().Contain("nameof(SavedSettings.ColorGradingLut)",
            "LUT toggle must route through ApplyPhaseToggle");
        core.Should().Contain("ColorGradingLUT.ApplySetting(newValue)",
            "ColorGradingLut must attach/detach ColorGradingLUT at runtime");
    }
}
