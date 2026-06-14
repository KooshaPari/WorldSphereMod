using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Invariant tests for the WeatherDriver system.
///
/// WeatherDriver is a MonoBehaviour singleton that spawns rain, snow, and
/// lightning particle effects. It gates creation on Core.IsWorld3D and the
/// SavedSettings toggle triad (WeatherRain, WeatherSnow, WeatherLightning).
/// These source-level checks ensure the lifecycle, constants, and settings
/// mapping remain stable.
/// </summary>
[Trait("Category", "E2E")]
public class WeatherDriverInvariantsTests
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
    // 1. SavedSettings keys exist and are wired in WeatherDriver
    // ------------------------------------------------------------------
    [Fact]
    public void SavedSettings_contains_WeatherRain_WeatherSnow_WeatherLightning()
    {
        var settings = ReadSourceFile("WorldSphereMod/Code/SavedSettings.cs");

        settings.Should().Contain("public bool WeatherRain", "SavedSettings must declare WeatherRain toggle");
        settings.Should().Contain("public bool WeatherSnow", "SavedSettings must declare WeatherSnow toggle");
        settings.Should().Contain("public bool WeatherLightning", "SavedSettings must declare WeatherLightning toggle");
    }

    [Fact]
    public void WeatherDriver_EnsureCreated_gates_on_all_three_settings()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Weather/WeatherDriver.cs");

        source.Should().Contain("Core.savedSettings.WeatherRain && !Core.savedSettings.WeatherSnow && !Core.savedSettings.WeatherLightning",
            "EnsureCreated must check all three weather toggles before spawning the driver");
    }

    [Fact]
    public void WeatherDriver_Update_gates_rain_snow_lightning_on_individual_settings()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Weather/WeatherDriver.cs");

        var updateBody = ExtractMethodBody(source, "void Update()");

        updateBody.Should().Contain("Core.savedSettings.WeatherRain", "Update must gate rain on WeatherRain");
        updateBody.Should().Contain("Core.savedSettings.WeatherSnow", "Update must gate snow on WeatherSnow");
        updateBody.Should().Contain("Core.savedSettings.WeatherLightning", "Update must gate lightning on WeatherLightning");
    }

    // ------------------------------------------------------------------
    // 2. Driver lifecycle is singleton-safe and teardown-clean
    // ------------------------------------------------------------------
    [Fact]
    public void WeatherDriver_EnsureCreated_and_Teardown_are_public_static()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Weather/WeatherDriver.cs");

        source.Should().Contain("public static void EnsureCreated()",
            "EnsureCreated must be public static for bootstrap");
        source.Should().Contain("public static void Teardown()",
            "Teardown must be public static for world-unload cleanup");
    }

    [Fact]
    public void WeatherDriver_Teardown_sets_Instance_null()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Weather/WeatherDriver.cs");

        var teardownBody = ExtractMethodBody(source, "public static void Teardown()");

        teardownBody.Should().Contain("Instance == null", "Teardown must guard against null Instance");
        teardownBody.Should().Contain("Destroy(Instance);", "Teardown must destroy the MonoBehaviour instance");
        teardownBody.Should().Contain("Instance = null;", "Teardown must null the static reference after destruction");
    }

    [Fact]
    public void WeatherDriver_OnDestroy_cleans_up_systems_and_materials()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Weather/WeatherDriver.cs");

        var onDestroyBody = ExtractMethodBody(source, "void OnDestroy()");

        onDestroyBody.Should().Contain("Destroy(_rainSystem.gameObject)", "OnDestroy must destroy rain particle system");
        onDestroyBody.Should().Contain("Destroy(_snowSystem.gameObject)", "OnDestroy must destroy snow particle system");
        onDestroyBody.Should().Contain("Destroy(_particleMaterial)", "OnDestroy must destroy particle material");
        onDestroyBody.Should().Contain("Destroy(_boltMaterial)", "OnDestroy must destroy bolt material");
    }

    [Fact]
    public void WeatherDriver_singleton_pattern_uses_static_Instance_field()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Weather/WeatherDriver.cs");

        source.Should().Contain("static WeatherDriver Instance;",
            "static Instance field must exist for singleton pattern");
    }

    // ------------------------------------------------------------------
    // 3. Weather constants are positive and within reasonable bounds
    // ------------------------------------------------------------------
    [Fact]
    public void WeatherDriver_rain_constants_are_positive()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Weather/WeatherDriver.cs");

        Regex.Match(source, @"const\s+float\s+kRainHeight\s*=\s*14f\s*;")
            .Success.Should().BeTrue("kRainHeight must be 14f");
        Regex.Match(source, @"const\s+float\s+kRainSpeed\s*=\s*24f\s*;")
            .Success.Should().BeTrue("kRainSpeed must be 24f");
        Regex.Match(source, @"const\s+float\s+kRainFallRate\s*=\s*160f\s*;")
            .Success.Should().BeTrue("kRainFallRate must be 160f");
        Regex.Match(source, @"const\s+float\s+kRainStartSize\s*=\s*0\.05f\s*;")
            .Success.Should().BeTrue("kRainStartSize must be 0.05f");
        Regex.Match(source, @"const\s+float\s+kRainLife\s*=\s*2\.1f\s*;")
            .Success.Should().BeTrue("kRainLife must be 2.1f");
    }

    [Fact]
    public void WeatherDriver_snow_constants_are_positive_and_slower_than_rain()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Weather/WeatherDriver.cs");

        Regex.Match(source, @"const\s+float\s+kSnowSpeed\s*=\s*6f\s*;")
            .Success.Should().BeTrue("kSnowSpeed must be 6f (slower than rain 24f)");
        Regex.Match(source, @"const\s+float\s+kSnowFallRate\s*=\s*30f\s*;")
            .Success.Should().BeTrue("kSnowFallRate must be 30f (lower than rain 160f)");
        Regex.Match(source, @"const\s+float\s+kSnowStartSize\s*=\s*0\.09f\s*;")
            .Success.Should().BeTrue("kSnowStartSize must be 0.09f");
        Regex.Match(source, @"const\s+float\s+kSnowLife\s*=\s*6f\s*;")
            .Success.Should().BeTrue("kSnowLife must be 6f (longer than rain 2.1f)");
    }

    [Fact]
    public void WeatherDriver_lightning_constants_are_positive()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Weather/WeatherDriver.cs");

        Regex.Match(source, @"const\s+float\s+kLightningRangeMin\s*=\s*5f\s*;")
            .Success.Should().BeTrue("kLightningRangeMin must be 5f");
        Regex.Match(source, @"const\s+float\s+kLightningRangeMax\s*=\s*15f\s*;")
            .Success.Should().BeTrue("kLightningRangeMax must be 15f");
        Regex.Match(source, @"const\s+float\s+kLightningFlashIntensity\s*=\s*4\.5f\s*;")
            .Success.Should().BeTrue("kLightningFlashIntensity must be 4.5f");
        Regex.Match(source, @"const\s+float\s+kLightningFlashDuration\s*=\s*0\.12f\s*;")
            .Success.Should().BeTrue("kLightningFlashDuration must be 0.12f");
        Regex.Match(source, @"const\s+float\s+kLightningBoltHeight\s*=\s*9f\s*;")
            .Success.Should().BeTrue("kLightningBoltHeight must be 9f");
    }

    [Fact]
    public void WeatherDriver_SpawnLightningFlash_creates_directional_light_with_hard_shadows()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Weather/WeatherDriver.cs");

        var spawnBody = ExtractMethodBody(source, "void SpawnLightningFlash(Vector3 tileCenter)");

        spawnBody.Should().Contain("LightType.Directional", "lightning flash must be a directional light");
        spawnBody.Should().Contain("flash.intensity = kLightningFlashIntensity", "flash intensity must use the constant");
        spawnBody.Should().Contain("flash.shadows = LightShadows.Hard", "lightning flash must cast hard shadows");
        spawnBody.Should().Contain("Destroy(lightning, kLightningFlashDuration)", "flash must self-destruct after duration");
    }

    [Fact]
    public void WeatherDriver_SpawnLightningBolt_uses_voxel_mesh_and_bolt_material()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Weather/WeatherDriver.cs");

        var spawnBody = ExtractMethodBody(source, "void SpawnLightningBolt(Vector3 tileCenter)");

        spawnBody.Should().Contain("meshFilter.sharedMesh = _voxelMesh", "bolt must reuse the weather voxel mesh");
        spawnBody.Should().Contain("meshRenderer.sharedMaterial = _boltMaterial", "bolt must use the dedicated bolt material");
    }

    // ------------------------------------------------------------------
    // 4. Helper: extract method body
    // ------------------------------------------------------------------
    static string ExtractMethodBody(string source, string signature)
    {
        int headerIndex = source.IndexOf(signature, System.StringComparison.Ordinal);
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

        throw new System.InvalidOperationException("Unbalanced braces while extracting method body");
    }
}
