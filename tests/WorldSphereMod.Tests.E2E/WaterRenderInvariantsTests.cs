using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Behavioral source invariants for the WaterSurface Standard-transparent fallback
/// (ADR-0013): GerstnerWater is not in SafeShaders, so EnsureMaterial must fall back
/// to the built-in Standard shader, and the GerstnerWater path must keep transparent
/// queue 3000 with ZWrite off so opaque terrain renders first and the water doesn't
/// punch a hole in the depth buffer. Runtime Unity is unavailable in CI, so these
/// assert on the source text the way the rest of this suite does.
/// </summary>
public sealed class WaterRenderInvariantsTests
{
    const string WaterSurfaceRelative = "WorldSphereMod/Code/Water/WaterSurface.cs";

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
    public void EnsureMaterial_has_Standard_transparent_fallback_when_GerstnerWater_unavailable()
    {
        var ensureBody = ExtractMethodBody(ReadSource(WaterSurfaceRelative), "static bool EnsureMaterial()");

        ensureBody.Should().Contain("Shader.Find(\"Standard\")",
            "EnsureMaterial must fall back to the built-in Standard shader when GerstnerWater is unavailable (ADR-0013)");
        ensureBody.Should().Contain("isStandardFallback = true",
            "the Standard fallback path must flag itself so ConfigureWaterMaterial applies transparent setup");
        ensureBody.Should().Contain("No water shader available (GerstnerWater + Standard both null); water disabled.",
            "water is only disabled when BOTH GerstnerWater and the Standard fallback are null");
    }

    [Fact]
    public void GerstnerWater_path_uses_transparent_render_queue_3000()
    {
        var configureBody = ExtractMethodBody(ReadSource(WaterSurfaceRelative),
            "static void ConfigureWaterMaterial(Material material, Color waterTint,");

        configureBody.Should().Contain("material.renderQueue = 3000",
            "the GerstnerWater (non-Standard) branch must use the Transparent queue (3000) so opaque terrain renders first");
        Regex.IsMatch(configureBody, @"SetOverrideTag\(""Queue"",\s*""Transparent""\)").Should().BeTrue(
            "the GerstnerWater branch must tag the material Queue=Transparent");
        configureBody.Should().Contain("_ALPHABLEND_ON",
            "transparent water must enable alpha blending for depth-driven opacity");
    }

    [Fact]
    public void GerstnerWater_path_disables_ZWrite_for_transparent_blend()
    {
        var configureBody = ExtractMethodBody(ReadSource(WaterSurfaceRelative),
            "static void ConfigureWaterMaterial(Material material, Color waterTint,");

        Regex.IsMatch(configureBody, @"SetInt\(""_ZWrite"",\s*0\)").Should().BeTrue(
            "transparent water must set ZWrite off (0) so it doesn't punch a depth-buffer hole occluding geometry behind it");
        configureBody.Should().Contain("OneMinusSrcAlpha",
            "transparent water must use the SrcAlpha/OneMinusSrcAlpha translucent blend");
    }

    [Fact]
    public void Standard_fallback_activates_transparent_alpha_pass_with_queue_3000()
    {
        var setStandardBody = ExtractMethodBody(ReadSource(WaterSurfaceRelative),
            "static void SetStandardTransparentMode(Material material)");

        // The Standard fallback renders alpha-blended transparent water. Standard's
        // transparent mode requires _Mode=3 PLUS the matching blend/keywords/queue,
        // or the alpha pass never activates and the surface goes invisible/flat.
        setStandardBody.Should().Contain("material.SetFloat(\"_Mode\", 3f)",
            "Standard fallback must use _Mode=3 (Transparent) to activate the alpha pass");
        setStandardBody.Should().Contain("material.EnableKeyword(\"_ALPHABLEND_ON\")",
            "Standard transparent fallback must enable the alpha-blend keyword");
        Regex.IsMatch(setStandardBody, @"SetInt\(""_ZWrite"",\s*0\)").Should().BeTrue(
            "Standard transparent fallback must disable depth writes (_ZWrite=0)");
        setStandardBody.Should().Contain("material.renderQueue = 3000",
            "Standard transparent fallback must use the Transparent render queue (3000)");
    }
}
