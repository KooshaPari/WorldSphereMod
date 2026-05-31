using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for the MeshWater toggle + WaterGerstner.shader resource.
///
/// NOTE (post crossed-quad removal + fork pivot, 2026-05-30): the main-mod water
/// renderer (Water/WaterSurface.cs, Water/WaterRender.cs, Water/WaterMaskBuffer.cs) is
/// DELETED. Mesh water now lives in the Compound-Spheres fork's HeightFieldRenderer.
/// The MeshWater saved setting + WaterGerstner.shader asset are retained on the main
/// mod, so their invariants stay; the deleted main-mod render-class source-text tests
/// are removed in favor of a guard that the Water/ code dir stays gone.
/// </summary>
public sealed class MeshWaterInvariantsTests
{
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
    public void MainMod_water_render_classes_are_deleted_after_fork_pivot()
    {
        // The main-mod water renderer moved to the Compound-Spheres fork HeightFieldRenderer.
        // Guard against the deleted overlay classes re-appearing in the main mod.
        var root = FindRepoRoot();
        foreach (var deleted in new[]
                 {
                     "WorldSphereMod/Code/Water/WaterSurface.cs",
                     "WorldSphereMod/Code/Water/WaterRender.cs",
                     "WorldSphereMod/Code/Water/WaterMaskBuffer.cs",
                 })
        {
            File.Exists(Path.Combine(root, deleted)).Should().BeFalse(
                $"{deleted} must stay deleted — mesh water lives in the fork HeightFieldRenderer");
        }

        var voxelRender = ReadSource("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        voxelRender.Should().NotContain("WaterRender",
            "TickPerFrame must not drive a main-mod WaterRender — water is fork-side now");
        voxelRender.Should().NotContain("WaterSurface",
            "VoxelRender must not reference the deleted main-mod WaterSurface overlay");
    }
}
