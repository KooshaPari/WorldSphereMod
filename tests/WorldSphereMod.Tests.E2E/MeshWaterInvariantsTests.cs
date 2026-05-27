using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for Phase 4 mesh water scaffold: SavedSettings.MeshWater default,
/// WaterSurface.EnsureMaterial fallback chain, and WaterGerstner.shader resource reference.
/// </summary>
public sealed class MeshWaterInvariantsTests
{
    const string WaterSurfaceRelative = "WorldSphereMod/Code/Water/WaterSurface.cs";
    const string WaterRenderRelative = "WorldSphereMod/Code/Water/WaterRender.cs";
    const string SavedSettingsRelative = "WorldSphereMod/Code/SavedSettings.cs";
    const string WaterGerstnerShaderRelative = "WorldSphereMod/Resources/Shaders/WaterGerstner.shader";

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
    public void SavedSettings_MeshWater_defaults_false()
    {
        var settings = ReadSource(SavedSettingsRelative);

        Regex.IsMatch(settings, @"public\s+bool\s+MeshWater\s*=\s*false")
            .Should().BeTrue("Phase 4 mesh water must default OFF for new installs");
        settings.Should().Contain("Phase 4: Mesh water surface",
            "MeshWater must remain documented as the Phase 4 water toggle");
    }

    [Fact]
    public void WaterGerstner_shader_ships_under_Resources()
    {
        var shaderPath = Path.Combine(FindRepoRoot(), WaterGerstnerShaderRelative);
        File.Exists(shaderPath).Should().BeTrue("WaterGerstner.shader must ship under Resources for runtime load");

        var shader = File.ReadAllText(shaderPath);
        shader.Should().Contain("Shader \"WorldSphereMod3D/WaterGerstner\"",
            "shader asset must declare the mod water shader name");
        shader.Should().Contain("_WaveTime",
            "Gerstner wave time uniform must exist for WaterSurface.ApplyWaveProfile");
    }

    [Fact]
    public void WaterSurface_EnsureMaterial_references_WaterGerstner_and_fallback_chain()
    {
        var source = ReadSource(WaterSurfaceRelative);
        var ensureBody = ExtractMethodBody(source, "static bool EnsureMaterial()");

        ensureBody.Should().Contain("LoadedShaders.TryGetValue(\"GerstnerWater\"",
            "bundled GerstnerWater must be preferred when AssetBundle cache is warm");
        ensureBody.Should().Contain("Shader.Find(\"WSM3D/GerstnerWater\")",
            "runtime must probe the baked shader name before Resources fallback");
        ensureBody.Should().Contain("Resources.Load<Shader>(\"Shaders/WaterGerstner\")",
            "EnsureMaterial must load the shipped WaterGerstner.shader resource");

        ensureBody.Should().Contain("string[] candidates =");
        ensureBody.Should().Contain("\"Universal Render Pipeline/Lit\"");
        ensureBody.Should().Contain("\"Standard\"");
        ensureBody.Should().Contain("\"Universal Render Pipeline/Unlit\"");

        ensureBody.Should().Contain("[WSM3D] No water shader found; water disabled.",
            "missing shader path must disable water instead of creating a broken surface");
    }

    [Fact]
    public void WaterSurface_EnsureMaterial_standard_fallback_avoids_blackworld_regression()
    {
        var source = ReadSource(WaterSurfaceRelative);
        var configureBody = ExtractMethodBody(source,
            "static void ConfigureWaterMaterial(Material material, Color waterTint,");

        configureBody.Should().Contain("SetStandardTransparentMode(material)",
            "built-in Standard fallback should use the shared transparent setup helper");
        configureBody.Should().Contain("EnableKeyword(\"_EMISSION\")",
            "Standard/URP Lit fallback must self-illuminate in zero-light scenes");
        configureBody.Should().Contain("material.renderQueue = 3000",
            "GerstnerWater path must use transparent queue for depth-based alpha blending");
    }

    [Fact]
    public void WaterSurface_EnsureMaterial_validates_enableInstancing_after_setting()
    {
        var source = ReadSource(WaterSurfaceRelative);
        var ensureBody = ExtractMethodBody(source, "static bool EnsureMaterial()");

        var setThenReadPattern = @"enableInstancing\s*=\s*true[\s\S]*?enableInstancing";
        Regex.IsMatch(ensureBody, setThenReadPattern).Should().BeTrue(
            "EnsureMaterial must verify enableInstancing was accepted before keeping a material");
    }

    [Fact]
    public void WaterRender_wires_MeshWater_lifecycle_and_tile_suppression()
    {
        var waterRender = ReadSource(WaterRenderRelative);
        var voxelRender = ReadSource("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        waterRender.Should().Contain("[Phase(nameof(SavedSettings.MeshWater))]",
            "mesh water patches must be gated by the MeshWater phase attribute");
        waterRender.Should().Contain("Core.savedSettings.MeshWater",
            "runtime toggle must read SavedSettings.MeshWater");
        waterRender.Should().Contain("WaterSurface.Create(",
            "world begin and runtime toggle must create the water surface");
        waterRender.Should().Contain("WaterSurface.Destroy()",
            "world finish and runtime toggle must tear down the water surface");
        waterRender.Should().Contain("WaterMaskBuffer.RebuildMask()",
            "mask rebuild must precede mesh creation and tile edits");

        var colorSuppression = ExtractMethodBody(waterRender,
            "public static void OnSphereTileColor(SphereTile SphereTile, ref Color32 __result)");
        colorSuppression.Should().Contain("WaterSurface.Instance == null",
            "tile color suppression must wait until the mesh exists");
        colorSuppression.Should().Contain("__result.a = 0",
            "water tiles must hide vanilla tile tint when mesh water is active");

        var tickPerFrame = ExtractMethodBody(voxelRender, "public static void TickPerFrame()");
        tickPerFrame.Should().Contain("WaterRender.UpdateLifecycle()",
            "TickPerFrame must reconcile runtime MeshWater toggles without world reload");
    }
}
