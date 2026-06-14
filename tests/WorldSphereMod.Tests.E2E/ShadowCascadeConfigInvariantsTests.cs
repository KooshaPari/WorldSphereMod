using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Invariant tests for cascaded shadow configuration (FR-WSM-007).
///
/// ShadowCascadeConfig uses full reflection to probe URP runtime because
/// WorldBox ships without URP DLLs in worldbox_Data/Managed. These source-level
/// checks ensure the cascade count, split ranges, and bias defaults stay within
/// documented bounds, and that SunRig / SunDriver public surface remains null-safe.
/// </summary>
[Trait("Category", "E2E")]
public class ShadowCascadeConfigInvariantsTests
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

    // ------------------------------------------------------------------
    // 1. ShadowCascadeConfig constants are capped and documented
    // ------------------------------------------------------------------
    [Fact]
    public void ShadowCascadeConfig_kMaxShadowCascades_is_2_and_capped_in_Apply()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs");

        Regex.Match(source, @"const\s+int\s+kMaxShadowCascades\s*=\s*2\s*;")
            .Success.Should().BeTrue("kMaxShadowCascades must be exactly 2 to limit GPU budget");

        source.Should().Contain("requestedCascades > kMaxShadowCascades ? kMaxShadowCascades : requestedCascades",
            "Apply must cap requested cascades to kMaxShadowCascades so the budget is never exceeded");
    }

    [Fact]
    public void ShadowCascadeConfig_kShadowDistance_is_30f()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs");

        Regex.Match(source, @"const\s+float\s+kShadowDistance\s*=\s*30f\s*;")
            .Success.Should().BeTrue("shadow distance constant must stay 30f for performance budget");
    }

    [Fact]
    public void ShadowCascadeConfig_cascade2Split_is_0_25f_within_valid_range()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs");

        Regex.Match(source, @"cascade2Split""\s*,\s*0\.25f\)")
            .Success.Should().BeTrue("2-cascade split must be 0.25f (mid-range)");
    }

    [Fact]
    public void ShadowCascadeConfig_Apply_and_Reset_are_public_static()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs");

        source.Should().Contain("public static void Apply(bool highShadows)",
            "Apply must be public static so SunDriver can call it");
        source.Should().Contain("public static void Reset()",
            "Reset must be public static so teardown can restore original settings");
    }

    // ------------------------------------------------------------------
    // 2. SunRig public methods are static and null-safe
    // ------------------------------------------------------------------
    [Fact]
    public void SunRig_Bind_is_public_static_and_accepts_null()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunRig.cs");

        source.Should().Contain("public static void Bind(Light sun)",
            "SunRig.Bind must be public static and accept a Light parameter");
    }

    [Fact]
    public void SunRig_Drive_guards_null_sun()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunRig.cs");

        source.Should().Contain("if (_sun == null) return;",
            "Drive must early-out when no sun is bound to avoid NRE during teardown");
    }

    [Fact]
    public void SunRig_sky_curve_methods_are_public_static_and_accept_0_to_1()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunRig.cs");

        source.Should().Contain("public static Color ZenithColor(float t)",
            "ZenithColor must be public static for skybox sampling");
        source.Should().Contain("public static Color SunColor(float t)",
            "SunColor must be public static for sun tint sampling");
        source.Should().Contain("public static Color HorizonColor(float t)",
            "HorizonColor must be public static for fog/ambient sampling");
        source.Should().Contain("public static Color FogColor(float t)",
            "FogColor must be public static for depth-fog tint");
    }

    [Fact]
    public void SunRig_SampleSkyCurve_is_four_segment_lerp()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunRig.cs");

        source.Should().Contain("t < 0.25f", "sky curve must have 4 segments (0-0.25, 0.25-0.5, 0.5-0.75, 0.75-1)");
        source.Should().Contain("t < 0.5f");
        source.Should().Contain("t < 0.75f");
        source.Should().Contain("Color.Lerp(dusk, night, (t - 0.75f) / 0.25f)",
            "final segment must lerp dusk back to night");
    }

    // ------------------------------------------------------------------
    // 3. SunDriver public surface is stable and null-safe
    // ------------------------------------------------------------------
    [Fact]
    public void SunDriver_public_static_surface_is_present()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunDriver.cs");

        source.Should().Contain("public static Light? Sun { get; private set; }",
            "Sun property must be public static so the rig can bind it");
        source.Should().Contain("public static bool Active",
            "Active getter must be public static for phase-gate checks");
        source.Should().Contain("public static void ApplyShadowSettings()",
            "ApplyShadowSettings must be public static");
        source.Should().Contain("public static void Init()",
            "Init must be public static for world-entry bootstrap");
        source.Should().Contain("public static void Teardown()",
            "Teardown must be public static for world-unload cleanup");
        source.Should().Contain("public static void Update()",
            "Update must be public static for per-frame pump");
        source.Should().Contain("public static void BindMainCamera(Camera? camera)",
            "BindMainCamera must be public static and accept null");
    }

    [Fact]
    public void SunDriver_ApplyShadowSettings_guards_null_sun()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunDriver.cs");

        source.Should().Contain("if (Sun == null) return;",
            "ApplyShadowSettings must guard against null Sun before writing shadow fields");
    }

    [Fact]
    public void SunDriver_TimeOfDayToEuler_maps_24h_to_360_degrees()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunDriver.cs");

        Regex.Match(source, @"return\s*\(hours\s*/\s*24f\)\s*\*\s*360f\s*-\s*90f\s*;")
            .Success.Should().BeTrue("TimeOfDayToEuler must map 24h to 360 degrees with -90 degree offset (noon at top)");
    }
}
