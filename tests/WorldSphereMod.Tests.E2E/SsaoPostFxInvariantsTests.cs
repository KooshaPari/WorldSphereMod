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
    public void Core_ApplyPhaseToggle_wires_PostFX_to_WSM3DPostStack()
    {
        var core = ReadSourceFile("WorldSphereMod/Code/Core.cs");
        var applyBody = ExtractMethodBody(core, "public static void ApplyPhaseToggle(string flagName, bool newValue)");

        applyBody.Should().Contain("nameof(SavedSettings.PostFX)",
            "PostFX master toggle must be routed through ApplyPhaseToggle");
        applyBody.Should().Contain("WSM3DPostStack.ApplySetting(newValue)",
            "PostFX toggle must route to unified WSM3DPostStack");

        applyBody.Should().Contain("nameof(SavedSettings.SSAOEnabled)",
            "SSAO sub-pass must be routed through ApplyPhaseToggle");
        applyBody.Should().Contain("nameof(SavedSettings.SSGIEnabled)",
            "SSGI sub-pass must be routed through ApplyPhaseToggle");
        applyBody.Should().Contain("nameof(SavedSettings.BloomEnabled)",
            "Bloom sub-pass must be routed through ApplyPhaseToggle");
        applyBody.Should().Contain("nameof(SavedSettings.ACESTonemapping)",
            "ACES tonemap sub-pass must be routed through ApplyPhaseToggle");

        applyBody.Should().Contain("WSM3DPostStack.RefreshMaterials()",
            "sub-pass toggles must refresh unified stack materials");
    }

    [Fact]
    public void Mod_init_wires_WSM3DPostStack()
    {
        var mod = ReadSourceFile("WorldSphereMod/Code/Mod.cs");
        var settings = ReadSourceFile("WorldSphereMod/Code/SavedSettings.cs");

        settings.Should().Contain("public bool SSAOEnabled = false",
            "SSAO should default OFF for Phase 9");
        settings.Should().Contain("public SsaoQuality SSAOQuality = SsaoQuality.Medium",
            "SSAO quality enum must live on SavedSettings");
        settings.Should().Contain("public bool BloomEnabled",
            "Bloom flag must exist on SavedSettings");
        settings.Should().Contain("public bool ACESTonemapping",
            "ACES tonemap flag must exist on SavedSettings");

        mod.Should().Contain("WSM3DPostStack.EnsureCreated()",
            "Mod world-init must initialize unified PostFX stack");
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

    [Fact]
    public void WSM3DPostStack_exposes_kernel_arrays_from_legacy_passes()
    {
        var ssao = ReadSourceFile("WorldSphereMod/Code/PostFx/ScreenSpaceAO.cs");
        var ssgi = ReadSourceFile("WorldSphereMod/Code/PostFx/ScreenSpaceGI.cs");

        ssao.Should().Contain("internal static readonly Vector4[] Kernel",
            "SSAO kernel must be accessible to unified stack");
        ssao.Should().Contain("internal static void BuildKernelStatic()",
            "SSAO must expose static kernel builder for stack init");

        ssgi.Should().Contain("internal static readonly Vector4[] Kernel",
            "SSGI kernel must be accessible to unified stack");
        ssgi.Should().Contain("internal static void BuildKernelStatic()",
            "SSGI must expose static kernel builder for stack init");
    }

    [Fact]
    public void Phase9_post_fx_shader_fallback_chains_follow_cache_find_resources_order()
    {
        var ssao = ReadSourceFile("WorldSphereMod/Code/PostFx/ScreenSpaceAO.cs");
        ssao.Should().Contain("Core.Sphere.LoadedShaders.TryGetValue(\"ScreenSpaceAO\"");
        ssao.Should().Contain("Shader.Find(\"WSM3D/ScreenSpaceAO\")");
        ssao.Should().Contain("Resources.LoadAsync<Shader>(ShaderResourcePath)");

        var ssgi = ReadSourceFile("WorldSphereMod/Code/PostFx/ScreenSpaceGI.cs");
        ssgi.Should().Contain("Core.Sphere.LoadedShaders.TryGetValue(\"ScreenSpaceGI\"");
        ssgi.Should().Contain("Shader.Find(\"WSM3D/ScreenSpaceGI\")");
        ssgi.Should().Contain("Resources.LoadAsync<Shader>(ShaderResourcePath)");

        var lut = ReadSourceFile("WorldSphereMod/Code/Lighting/ColorGradingLUT.cs");
        lut.Should().Contain("Core.Sphere.LoadedShaders.TryGetValue(\"ColorGradingLUT\"");
        lut.Should().Contain("Shader.Find(\"WSM3D/ColorGradingLUT\")");
        lut.Should().Contain("Resources.LoadAsync<Shader>(LutShaderResourcePath)");

        var postStack = ReadSourceFile("WorldSphereMod/Code/PostFx/WSM3DPostStack.cs");
        postStack.Should().Contain("Core.Sphere.LoadedShaders.TryGetValue(cacheKey");
        postStack.Should().Contain("Shader.Find(\"WSM3D/\" + cacheKey)");
        postStack.Should().Contain("Resources.Load<Shader>(resourcePath)");
    }
}
