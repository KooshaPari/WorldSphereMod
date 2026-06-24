using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Invariant tests for the directional-sun registration fix.
///
/// Root cause (commit 332c7d8f / fix/live-lighting): nothing in the WSM3D boot
/// sequence assigned RenderSettings.sun, so the scene had no key light — terrain
/// appeared solid black even though geometry was correctly generated. SunDriver.Init
/// now explicitly assigns RenderSettings.sun = Sun after creating the directional
/// light, and clears it on teardown.
///
/// These source-level checks guard against the null-sun regression being
/// re-introduced by a future refactor.
/// </summary>
[Trait("Category", "E2E")]
public class SunRegistrationInvariantsTests
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

    // ---------------------------------------------------------------
    // 1. SunDriver.Init assigns RenderSettings.sun
    // ---------------------------------------------------------------
    // Regression: RenderSettings.sun was null at runtime (never assigned).
    // Unity uses RenderSettings.sun as the key directional light for ambient
    // and skybox lighting. Without it terrain is rendered black regardless
    // of whether a directional Light component exists in the scene.
    [Fact]
    public void SunDriver_Init_assigns_RenderSettings_sun()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunDriver.cs");

        source.Should().Contain("RenderSettings.sun = Sun",
            "SunDriver.Init must assign RenderSettings.sun = Sun so the directional " +
            "light is registered as the scene key light — without this terrain is black " +
            "(fix/live-lighting commit 332c7d8f)");
    }

    // ---------------------------------------------------------------
    // 2. SunDriver teardown clears RenderSettings.sun
    // ---------------------------------------------------------------
    // Invariant: the cleanup path must null out RenderSettings.sun when the
    // sun is torn down to avoid dangling references across world reloads.
    [Fact]
    public void SunDriver_teardown_clears_RenderSettings_sun()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunDriver.cs");

        source.Should().Contain("RenderSettings.sun = null",
            "SunDriver teardown must clear RenderSettings.sun = null so the scene " +
            "does not hold a stale reference to a destroyed light across world reloads");
    }

    // ---------------------------------------------------------------
    // 3. SunDriver carries the null-sun root-cause comment
    // ---------------------------------------------------------------
    // Guard: the comment explaining WHY we do this (to prevent future
    // removal by an uninformed edit) must be present.
    [Fact]
    public void SunDriver_has_null_sun_root_cause_comment()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunDriver.cs");

        source.Should().Contain("SUN=NULL ROOT-CAUSE FIX",
            "SunDriver must carry the SUN=NULL ROOT-CAUSE FIX comment so future " +
            "editors understand why the explicit RenderSettings.sun assignment exists " +
            "and do not remove it as 'redundant'");
    }
}
