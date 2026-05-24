using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for docs/specs/onrenderimage-postfx-spec.md — verifies the
/// unified WSM3DPostStack ping-pong chain replaces the old split OnRenderImage passes.
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
        spec.Should().Contain("ScreenSpaceAO",
            "SSAO OnRenderImage pass is part of current architecture");
        spec.Should().Contain("PostFxController",
            "URP volume path must be documented as partial/degraded in WB");
        spec.Should().Contain("OnRenderImagePostFxSpecInvariantsTests",
            "spec must point maintainers at e2e guardrails");
    }

    [Fact]
    public void Unified_WSM3DPostStack_is_shipped()
    {
        var root = FindRepoRoot();
        var stackPath = Path.Combine(root, "WorldSphereMod", "Code", "PostFx", "WSM3DPostStack.cs");
        File.Exists(stackPath).Should().BeTrue("WSM3DPostStack must exist as the unified post-FX chain");

        var src = File.ReadAllText(stackPath);
        src.Should().Contain("class WSM3DPostStack");
        src.Should().Contain("void OnRenderImage(RenderTexture src, RenderTexture dst)");
        src.Should().Contain("EnsurePingPong");
        src.Should().Contain("RemoveLegacyPasses");
    }

    [Fact]
    public void WSM3DPostStack_chains_all_five_passes_in_deterministic_order()
    {
        var src = ReadSource(@"WorldSphereMod/Code/PostFx/WSM3DPostStack.cs");

        int ssaoIdx = src.IndexOf("Pass 1: SSAO", StringComparison.Ordinal);
        int ssgiIdx = src.IndexOf("Pass 2: SSGI", StringComparison.Ordinal);
        int bloomIdx = src.IndexOf("Pass 3: Bloom", StringComparison.Ordinal);
        int acesIdx = src.IndexOf("Pass 4: ACES", StringComparison.Ordinal);
        int lutIdx = src.IndexOf("Pass 5: LUT", StringComparison.Ordinal);

        ssaoIdx.Should().BeGreaterThan(0, "SSAO pass must exist");
        ssgiIdx.Should().BeGreaterThan(ssaoIdx, "SSGI follows SSAO");
        bloomIdx.Should().BeGreaterThan(ssgiIdx, "Bloom follows SSGI");
        acesIdx.Should().BeGreaterThan(bloomIdx, "ACES follows Bloom");
        lutIdx.Should().BeGreaterThan(acesIdx, "LUT follows ACES");
    }

    [Fact]
    public void Legacy_OnRenderImage_passes_still_exist_for_standalone_use()
    {
        ReadSource(@"WorldSphereMod/Code/PostFx/ScreenSpaceAO.cs")
            .Should().Contain("void OnRenderImage(RenderTexture source, RenderTexture destination)");
        ReadSource(@"WorldSphereMod/Code/PostFx/ScreenSpaceGI.cs")
            .Should().Contain("void OnRenderImage(RenderTexture source, RenderTexture destination)");
        ReadSource(@"WorldSphereMod/Code/Lighting/ColorGradingLUT.cs")
            .Should().Contain("void OnRenderImage(RenderTexture source, RenderTexture destination)");
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
    public void Core_routes_PostFX_toggle_to_WSM3DPostStack()
    {
        var core = ReadSource(@"WorldSphereMod/Code/Core.cs");

        core.Should().Contain("nameof(SavedSettings.PostFX)");
        core.Should().Contain("WSM3DPostStack.ApplySetting(newValue)",
            "PostFX master toggle must route to unified stack");
        core.Should().Contain("WSM3DPostStack.RefreshMaterials()",
            "sub-pass toggles must refresh the unified stack materials");
    }

    [Fact]
    public void BRP_shaders_exist_for_bloom_and_ACES()
    {
        var root = FindRepoRoot();
        File.Exists(Path.Combine(root, @"WorldSphereMod/Resources/Shaders/BrpACES.shader"))
            .Should().BeTrue("BRP-compatible ACES tonemap shader must exist");
        File.Exists(Path.Combine(root, @"WorldSphereMod/Resources/Shaders/BrpBloom.shader"))
            .Should().BeTrue("BRP-compatible Bloom shader must exist");
    }
}
