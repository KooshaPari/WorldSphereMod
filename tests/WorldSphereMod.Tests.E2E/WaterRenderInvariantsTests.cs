using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Behavioral source invariants for the Compound-Spheres water rendering path
/// (Phase 6 architecture decision ADR-0013): water rendering was migrated from
/// WS3D's deleted WorldSphereMod/Code/Water/WaterSurface.cs into the
/// Compound-Spheres fork's HeightFieldRenderer.ConfigureWater. These tests now
/// assert on the new location.
/// </summary>
public sealed class WaterRenderInvariantsTests
{
    const string HeightFieldRendererRelative = "External/Compound-Spheres/CompoundSpheres/HeightFieldRenderer.cs";

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
    public void ConfigureWater_enables_transparent_render_queue_3000()
    {
        // The water rendering path lives in HeightFieldRenderer.ConfigureWater inside
        // the Compound-Spheres submodule (post-Phase 6 migration). It must use the
        // Transparent render queue (3000) so opaque terrain renders first.
        var source = ReadSource(HeightFieldRendererRelative);
        source.Should().Contain("void ConfigureWater(",
            "HeightFieldRenderer must expose ConfigureWater (post Phase 6 migration)");
        source.Should().Contain("renderQueue = 3000",
            "water must use the Transparent queue (3000) so opaque terrain renders first");
        source.Should().Contain("_ALPHABLEND_ON",
            "transparent water must enable alpha blending for depth-driven opacity");
    }

    [Fact]
    public void ConfigureWater_disables_ZWrite_for_transparent_blend()
    {
        var source = ReadSource(HeightFieldRendererRelative);
        // Transparent water must set ZWrite off so it doesn't punch a depth-buffer hole.
        Regex.IsMatch(source, @"SetInt\(""_ZWrite"",\s*0\)").Should().BeTrue(
            "transparent water must set ZWrite off (0) so it doesn't punch a depth-buffer hole");
        source.Should().Contain("OneMinusSrcAlpha",
            "transparent water must use the SrcAlpha/OneMinusSrcAlpha translucent blend");
    }

    [Fact]
    public void ConfigureWater_tags_Queue_Transparent()
    {
        var source = ReadSource(HeightFieldRendererRelative);
        Regex.IsMatch(source, @"SetOverrideTag\(""Queue"",\s*""Transparent""\)").Should().BeTrue(
            "water must tag the material Queue=Transparent");
    }

    [Fact]
    public void HeightFieldRenderer_has_SetWaterMaterial_for_Standard_fallback()
    {
        // When GerstnerWater is unavailable, the fallback to the built-in Standard
        // shader must still apply the transparent setup (queue 3000, alpha blend on).
        var source = ReadSource(HeightFieldRendererRelative);
        source.Should().Contain("SetWaterMaterial(",
            "HeightFieldRenderer must expose SetWaterMaterial for the Standard-fallback path");
        source.Should().Contain("Shader.Find(\"Standard\")",
            "SetWaterMaterial must fall back to the built-in Standard shader when GerstnerWater is unavailable (ADR-0013)");
    }
}