using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Invariant for CubemapLighting ambient mode: WorldBox's scene has no real lighting,
/// so a Skybox-mode SH ambient probe samples the pale-blue horizon and tints every
/// surface blue. The fix is AmbientMode.Trilight with neutral sky/equator/ground
/// colors, which keeps shading directional without the blue cast. This test guards
/// against a regression back to Skybox ambient.
/// </summary>
public sealed class CubemapAmbientInvariantsTests
{
    const string CubemapLightingRelative = "WorldSphereMod/Code/Lighting/CubemapLighting.cs";

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
    public void Applies_Trilight_ambient_mode_not_Skybox()
    {
        var source = ReadSource(CubemapLightingRelative);

        Regex.IsMatch(source, @"RenderSettings\.ambientMode\s*=\s*AmbientMode\.Trilight").Should().BeTrue(
            "CubemapLighting must set ambientMode to Trilight to avoid the Skybox-probe blue cast on unlit WorldBox surfaces");
        Regex.IsMatch(source, @"RenderSettings\.ambientMode\s*=\s*AmbientMode\.Skybox").Should().BeFalse(
            "CubemapLighting must NOT set ambientMode to Skybox (regression: tints everything blue)");
    }

    [Fact]
    public void Trilight_uses_neutral_sky_equator_ground_colors()
    {
        var source = ReadSource(CubemapLightingRelative);

        source.Should().Contain("RenderSettings.ambientSkyColor",
            "Trilight ambient requires an explicit neutral sky color");
        source.Should().Contain("RenderSettings.ambientEquatorColor",
            "Trilight ambient requires an explicit neutral equator color");
        source.Should().Contain("RenderSettings.ambientGroundColor",
            "Trilight ambient requires an explicit neutral ground color");
    }

    [Fact]
    public void Captures_and_restores_previous_ambient_mode()
    {
        var source = ReadSource(CubemapLightingRelative);

        source.Should().Contain("_previousAmbientMode = RenderSettings.ambientMode",
            "the previous ambient mode must be captured so it can be restored on teardown");
        source.Should().Contain("RenderSettings.ambientMode = _previousAmbientMode",
            "the previous ambient mode must be restored when the cubemap lighting is removed");
    }
}
