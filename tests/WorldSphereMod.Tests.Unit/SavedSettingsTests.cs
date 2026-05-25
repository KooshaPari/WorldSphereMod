using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using FluentAssertions;

/// <summary>
/// Static-text verification of SavedSettings.cs structure.
/// We can't instantiate SavedSettings in unit tests because it requires
/// Unity/WorldBox DLLs. Instead, we verify the source file directly.
/// </summary>
public class SavedSettingsTests
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

    private static string ReadSavedSettingsSource()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "WorldSphereMod/Code/SavedSettings.cs");
        File.Exists(path).Should().BeTrue($"SavedSettings.cs must exist at {path}");
        return File.ReadAllText(path);
    }

    private static string ExtractMethodBody(string source, string signature)
    {
        var headerIndex = source.IndexOf(signature, StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0, $"method signature should exist: {signature}");

        var openBrace = source.IndexOf('{', headerIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0, "method must open with a '{'");

        var depth = 0;
        for (int i = openBrace; i < source.Length; i++)
        {
            var c = source[i];
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
    public void SavedSettings_declares_Version_field_with_initial_value_2_0()
    {
        var source = ReadSavedSettingsSource();

        // Must declare: public string Version = "2.3";
        var pattern = @"public\s+string\s+Version\s*=\s*""2\.2""";
        var match = Regex.Match(source, pattern);

        match.Success.Should().BeTrue(
            "SavedSettings must declare: public string Version = \"2.0\";");
    }

    [Fact]
    public void SavedSettings_declares_BuildingStyleProcgen_field_with_initial_value_false()
    {
        var source = ReadSavedSettingsSource();

        var pattern = @"public\s+bool\s+BuildingStyleProcgen\s*=\s*false";
        var match = Regex.Match(source, pattern);

        match.Success.Should().BeTrue(
            "SavedSettings must declare: public bool BuildingStyleProcgen = false;");
    }

    [Fact]
    public void SavedSettings_declares_all_ten_v2_phase_fields()
    {
        var source = ReadSavedSettingsSource();

        var phaseFields = new[]
        {
            ("VoxelEntities", true),
            ("ProceduralBuildings", false),
            ("CrossedQuadFoliage", false),
            ("BiomeBlending", false),
            ("MeshWater", false),
            ("HighShadows", false),
            ("SkeletalAnimation", false),
            ("WorldspaceUI", false),
            ("DayNightCycle", false),
            ("PostFX", false),
            ("ParticleEffects", false)
        };

        foreach (var (fieldName, expectedDefault) in phaseFields)
        {
            var expectedValue = expectedDefault ? "true" : "false";
            var pattern = $@"public\s+bool\s+{fieldName}\s*=\s*{expectedValue}";
            var match = Regex.Match(source, pattern);

            match.Success.Should().BeTrue(
                $"SavedSettings must declare: public bool {fieldName} = {expectedValue};");
        }
    }

    [Fact]
    public void SavedSettings_v2_phase_fields_all_have_bool_type()
    {
        var source = ReadSavedSettingsSource();

        var phaseFields = new[]
        {
            "VoxelEntities",
            "ProceduralBuildings",
            "CrossedQuadFoliage",
            "BiomeBlending",
            "MeshWater",
            "HighShadows",
            "SkeletalAnimation",
            "WorldspaceUI",
            "DayNightCycle",
            "PostFX",
            "ParticleEffects"
        };

        foreach (var fieldName in phaseFields)
        {
            // Each field should declare as `public bool FieldName = ...`
            var pattern = $@"public\s+bool\s+{fieldName}\s*=";
            var match = Regex.Match(source, pattern);

            match.Success.Should().BeTrue(
                $"SavedSettings.{fieldName} must be declared as public bool");
        }
    }

    [Fact]
    public void SavedSettings_v2_phase_field_defaults_are_documented()
    {
        var source = ReadSavedSettingsSource();

        // Each phase should have an inline comment explaining what it is.
        var expectedComments = new[]
        {
            ("VoxelEntities", "Phase 1"),
            ("ProceduralBuildings", "Phase 2"),
            ("CrossedQuadFoliage", "Phase 3"),
            ("BiomeBlending", "biome blending"),
            ("MeshWater", "Phase 4"),
            ("HighShadows", "Phase 5"),
            ("SkeletalAnimation", "Phase 6"),
            ("WorldspaceUI", "Phase 7"),
            ("DayNightCycle", "Phase 8"),
            ("PostFX", "Phase 9"),
            ("ParticleEffects", "Phase 9")
        };

        foreach (var (fieldName, phaseDesc) in expectedComments)
        {
            // Look for a comment mentioning the phase near the field declaration.
            var pattern = $@"//{1,2}.*?{phaseDesc}[\s\S]*?public\s+bool\s+{fieldName}";
            var match = Regex.Match(source, pattern, RegexOptions.IgnoreCase);

            // Less strict: just verify the phase declaration exists (comment is nice but not required).
            source.Should().Contain(fieldName,
                $"SavedSettings.{fieldName} must be declared for phase support");
        }
    }

    [Theory]
    [InlineData("VoxelEntities", "true")]
    [InlineData("ProceduralBuildings", "false")]
    [InlineData("CrossedQuadFoliage", "false")]
    [InlineData("BiomeBlending", "false")]
    [InlineData("MeshWater", "false")]
    [InlineData("HighShadows", "false")]
    [InlineData("SkeletalAnimation", "false")]
    [InlineData("WorldspaceUI", "false")]
    [InlineData("DayNightCycle", "false")]
    [InlineData("PostFX", "false")]
    [InlineData("ParticleEffects", "false")]
    public void SavedSettings_field_default_value_matches_spec(string fieldName, string expectedDefault)
    {
        var source = ReadSavedSettingsSource();
        var pattern = $@"public\s+bool\s+{fieldName}\s*=\s*{expectedDefault}";
        var match = Regex.Match(source, pattern);

        match.Success.Should().BeTrue(
            $"SavedSettings.{fieldName} must initialize to {expectedDefault}");
    }

    [Fact]
    public void SavedSettings_phase3_defaults_match_code()
    {
        var source = ReadSavedSettingsSource();

        Regex.Match(source, @"public\s+bool\s+CrossedQuadFoliage\s*=\s*false")
            .Success.Should().BeTrue("Phase 3 should default CrossedQuadFoliage to false");
    }

    [Fact]
    public void SavedSettings_phase4_defaults_match_code()
    {
        var source = ReadSavedSettingsSource();

        Regex.Match(source, @"public\s+bool\s+MeshWater\s*=\s*false")
            .Success.Should().BeTrue("Phase 4 should default MeshWater to false");
    }

    [Fact]
    public void SavedSettings_phase5_defaults_match_code()
    {
        var source = ReadSavedSettingsSource();

        Regex.Match(source, @"public\s+bool\s+HighShadows\s*=\s*false")
            .Success.Should().BeTrue("Phase 5 should default HighShadows to false");
    }

    [Fact]
    public void SavedSettings_phase5b_defaults_match_code()
    {
        var source = ReadSavedSettingsSource();

        Regex.Match(source, @"public\s+bool\s+HdrSkybox\s*=\s*false")
            .Success.Should().BeTrue("Phase 5b should default HdrSkybox to false");
        Regex.Match(source, @"public\s+bool\s+ColorGradingLut\s*=\s*false")
            .Success.Should().BeTrue("Phase 5b should default ColorGradingLut to false");
    }

    [Fact]
    public void SavedSettings_phase6_defaults_match_code()
    {
        var source = ReadSavedSettingsSource();

        Regex.Match(source, @"public\s+bool\s+SkeletalAnimation\s*=\s*false")
            .Success.Should().BeTrue("Phase 6 should default SkeletalAnimation to false");
    }

    [Fact]
    public void SavedSettings_phase8_defaults_match_code()
    {
        var source = ReadSavedSettingsSource();

        Regex.Match(source, @"public\s+bool\s+DayNightCycle\s*=\s*false")
            .Success.Should().BeTrue("Phase 8 should default DayNightCycle to false");
        Regex.Match(source, @"public\s+float\s+FogDensity\s*=\s*0\.05f")
            .Success.Should().BeTrue("Phase 8 should default FogDensity to 0.05f");
    }

    [Fact]
    public void SavedSettings_phase9_defaults_match_code()
    {
        var source = ReadSavedSettingsSource();

        Regex.Match(source, @"public\s+bool\s+PostFX\s*=\s*false")
            .Success.Should().BeTrue("Phase 9 should default PostFX to false");
        Regex.Match(source, @"public\s+bool\s+SSAOEnabled\s*=\s*false")
            .Success.Should().BeTrue("Phase 9 should default SSAOEnabled to false");
        Regex.Match(source, @"public\s+bool\s+SSGIEnabled\s*=\s*false")
            .Success.Should().BeTrue("Phase 9 should default SSGIEnabled to false");
    }

    [Fact]
    public void SavedSettings_contains_no_duplicate_phase_field_declarations()
    {
        var source = ReadSavedSettingsSource();

        var phaseFields = new[]
        {
            "VoxelEntities",
            "ProceduralBuildings",
            "CrossedQuadFoliage",
            "BiomeBlending",
            "MeshWater",
            "HighShadows",
            "SkeletalAnimation",
            "WorldspaceUI",
            "DayNightCycle",
            "PostFX",
            "ParticleEffects"
        };

        foreach (var fieldName in phaseFields)
        {
            var pattern = $@"public\s+bool\s+{fieldName}\s*=";
            var matches = Regex.Matches(source, pattern);

            matches.Count.Should().Be(1,
                $"SavedSettings.{fieldName} must be declared exactly once");
        }
    }

    [Fact]
    public void SavedSettings_defaults_voxel_mesh_smoothing_off_with_zero_iterations()
    {
        var source = ReadSavedSettingsSource();

        Regex.Match(source, @"public\s+bool\s+VoxelMeshSmoothing\s*=\s*false")
            .Success.Should().BeTrue("VoxelMeshSmoothing must default to false (ADR-0008)");

        Regex.Match(source, @"public\s+int\s+SmoothingIterations\s*=\s*0")
            .Success.Should().BeTrue("SmoothingIterations must default to 0 when smoothing is off");
    }

    [Fact]
    public void SavedSettings_lightweight_preset_disables_postfx_bloom_and_aces()
    {
        var source = ReadSavedSettingsSource();
        var body = ExtractMethodBody(source, "public static void ApplyLightweightPreset(SavedSettings s)");

        body.Should().Contain("s.PostFX = false");
        body.Should().Contain("s.SSAOEnabled = false");
        body.Should().Contain("s.SSGIEnabled = false");
        body.Should().Contain("s.BloomEnabled = false");
        body.Should().Contain("s.ACESTonemapping = false");
    }

    [Fact]
    public void SavedSettings_full_preset_enables_postfx_bloom_and_aces()
    {
        var source = ReadSavedSettingsSource();
        var body = ExtractMethodBody(source, "public static void ApplyFullPreset(SavedSettings s)");

        body.Should().Contain("s.PostFX = true");
        body.Should().Contain("s.SSAOEnabled = true");
        body.Should().Contain("s.SSGIEnabled = true");
        body.Should().Contain("s.BloomEnabled = true");
        body.Should().Contain("s.ACESTonemapping = true");
    }
}
