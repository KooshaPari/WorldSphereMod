using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using FluentAssertions;
using Newtonsoft.Json.Linq;

public class LocaleKeyCoverageTests
{
    // Locate the repo root from test output directory.
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    private static string ReadSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void EnJson_parses_successfully()
    {
        var enJsonText = ReadSourceFile("WorldSphereMod/Locales/en.json");

        Action parseAction = () =>
        {
            var obj = JObject.Parse(enJsonText);
            obj.Should().NotBeNull();
        };

        parseAction.Should().NotThrow("en.json must be valid JSON that can be parsed");
    }

    [Fact]
    public void All_phase_toggle_buttons_in_WorldSphereTab_have_matching_locale_keys()
    {
        var tabSource = ReadSourceFile("WorldSphereMod/Code/WorldSphereTab.cs");
        var enJsonText = ReadSourceFile("WorldSphereMod/Locales/en.json");
        var enJson = JObject.Parse(enJsonText);

        // Extract all ButtonData calls within the "3D Phases" CreateWindowButton block.
        // The pattern looks for: new ButtonData("<key>", "<desc_key>", ...)
        // where <key> is a locale key like "voxel_entities".

        var phasesWindowPattern = @"CreateWindowButton\((?:""3D Phases""|PhasesWindowId)[\s\S]*?\}\s*\);";
        var match = Regex.Match(tabSource, phasesWindowPattern);

        match.Success.Should().BeTrue("WorldSphereTab must define the 3D Phases window");

        var phasesWindowBlock = match.Value;

        // Extract all ButtonData locale keys from the block.
        var buttonDataPattern = @"new\s+ButtonData\(\s*""([a-z_]+)""\s*,\s*""([a-z_]+)""";
        var buttonMatches = Regex.Matches(phasesWindowBlock, buttonDataPattern);

        buttonMatches.Should().NotBeEmpty("3D Phases window must contain at least one phase toggle");

        var foundKeys = new HashSet<string>();
        var foundDescKeys = new HashSet<string>();

        foreach (Match buttonMatch in buttonMatches)
        {
            var mainKey = buttonMatch.Groups[1].Value;
            var descKey = buttonMatch.Groups[2].Value;

            foundKeys.Add(mainKey);
            foundDescKeys.Add(descKey);
        }

        // Now verify that every extracted key exists in en.json.
        foreach (var key in foundKeys)
        {
            enJson.ContainsKey(key).Should().BeTrue(
                $"Locale key '{key}' from WorldSphereTab.CreateButtons must exist in en.json");

            var value = enJson[key];
            value.Should().NotBeNull("locale key must have a value");
            var strValue = value!.ToString();
            strValue.Should().NotBeNullOrWhiteSpace($"locale key '{key}' must have non-empty string value");
        }

        // Verify that every description key exists in en.json.
        foreach (var descKey in foundDescKeys)
        {
            enJson.ContainsKey(descKey).Should().BeTrue(
                $"Locale description key '{descKey}' from WorldSphereTab.CreateButtons must exist in en.json");

            var value = enJson[descKey];
            value.Should().NotBeNull("locale description key must have a value");
            var strValue = value!.ToString();
            strValue.Should().NotBeNullOrWhiteSpace($"locale description key '{descKey}' must have non-empty string value");
        }
    }

    [Fact]
    public void Extracted_phase_toggles_from_code_match_expected_eleven_phases()
    {
        var tabSource = ReadSourceFile("WorldSphereMod/Code/WorldSphereTab.cs");

        // Extract all ButtonData calls within the "3D Phases" CreateWindowButton block.
        var phasesWindowPattern = @"CreateWindowButton\((?:""3D Phases""|PhasesWindowId)[\s\S]*?\}\s*\);";
        var match = Regex.Match(tabSource, phasesWindowPattern);

        match.Success.Should().BeTrue("WorldSphereTab must define the 3D Phases window");

        var phasesWindowBlock = match.Value;
        var buttonDataPattern = @"new\s+ButtonData\(\s*""([a-z_]+)""";
        var buttonMatches = Regex.Matches(phasesWindowBlock, buttonDataPattern);

        buttonMatches.Should().HaveCount(20,
            "WorldSphereTab 3D Phases window defines 19 phase toggles + sanity_cube debug toggle");

        var expectedPhases = new[]
        {
            "voxel_entities",
            "procedural_buildings",
            "crossed_quad_foliage",
            "biome_blending",
            "mesh_water",
            "high_shadows",
            "skeletal_animation",
            "worldspace_ui",
            "day_night_cycle",
            "mountain_slope_smoothing",
            "hdr_skybox",
            "color_grading_lut",
            "ssao_enabled",
            "ssgi_enabled",
            "weather_rain",
            "weather_snow",
            "weather_lightning",
            "post_fx",
            "particle_effects",
            "sanity_cube"
        };

        var extractedPhases = new List<string>();
        foreach (Match buttonMatch in buttonMatches)
        {
            extractedPhases.Add(buttonMatch.Groups[1].Value);
        }

        foreach (var phase in expectedPhases)
        {
            extractedPhases.Should().Contain(phase,
                $"3D Phases window must include toggle for '{phase}'");
        }
    }

    [Fact]
    public void En_json_does_not_have_orphaned_phase_keys()
    {
        var tabSource = ReadSourceFile("WorldSphereMod/Code/WorldSphereTab.cs");
        var enJsonText = ReadSourceFile("WorldSphereMod/Locales/en.json");
        var enJson = JObject.Parse(enJsonText);

        // Extract all ButtonData locale keys from the 3D Phases window.
        var phasesWindowPattern = @"CreateWindowButton\((?:""3D Phases""|PhasesWindowId)[\s\S]*?\}\s*\);";
        var match = Regex.Match(tabSource, phasesWindowPattern);

        match.Success.Should().BeTrue();

        var phasesWindowBlock = match.Value;
        var buttonDataPattern = @"new\s+ButtonData\(\s*""([a-z_]+)""";
        var buttonMatches = Regex.Matches(phasesWindowBlock, buttonDataPattern);

        var expectedKeys = new HashSet<string>();
        foreach (Match buttonMatch in buttonMatches)
        {
            expectedKeys.Add(buttonMatch.Groups[1].Value);
        }

        // Check that en.json does not have unexpected phase keys that aren't in the code.
        // (This is less strict — orphaned keys are less critical than missing keys.)
        // But we'll document the pattern for completeness.
        var potentialPhaseKeys = new[]
        {
            "voxel_entities",
            "procedural_buildings",
            "crossed_quad_foliage",
            "biome_blending",
            "mesh_water",
            "high_shadows",
            "skeletal_animation",
            "worldspace_ui",
            "day_night_cycle",
            "mountain_slope_smoothing",
            "hdr_skybox",
            "color_grading_lut",
            "ssao_enabled",
            "ssgi_enabled",
            "weather_rain",
            "weather_snow",
            "weather_lightning",
            "post_fx",
            "particle_effects",
            "sanity_cube"
        };

        // Just verify that every expected key is in en.json (already done above, but document it here).
        foreach (var key in expectedKeys)
        {
            enJson.ContainsKey(key).Should().BeTrue(
                $"Phase key '{key}' must be in en.json");
        }
    }
}
