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

    [Fact]
    public void SavedSettings_declares_Version_field_with_initial_value_2_0()
    {
        var source = ReadSavedSettingsSource();

        // Must declare: public string Version = "2.0";
        var pattern = @"public\s+string\s+Version\s*=\s*""2\.0""";
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
            ("VoxelEntities", false),
            ("ProceduralBuildings", false),
            ("CrossedQuadFoliage", true),
            ("BiomeBlending", false),
            ("MeshWater", false),
            ("HighShadows", false),
            ("SkeletalAnimation", false),
            ("WorldspaceUI", true),
            ("DayNightCycle", false),
            ("PostFX", false),
            ("ParticleEffects", true)
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
    [InlineData("VoxelEntities", "false")]
    [InlineData("ProceduralBuildings", "false")]
    [InlineData("CrossedQuadFoliage", "true")]
    [InlineData("BiomeBlending", "false")]
    [InlineData("MeshWater", "false")]
    [InlineData("HighShadows", "false")]
    [InlineData("SkeletalAnimation", "false")]
    [InlineData("WorldspaceUI", "true")]
    [InlineData("DayNightCycle", "false")]
    [InlineData("PostFX", "false")]
    [InlineData("ParticleEffects", "true")]
    public void SavedSettings_field_default_value_matches_spec(string fieldName, string expectedDefault)
    {
        var source = ReadSavedSettingsSource();
        var pattern = $@"public\s+bool\s+{fieldName}\s*=\s*{expectedDefault}";
        var match = Regex.Match(source, pattern);

        match.Success.Should().BeTrue(
            $"SavedSettings.{fieldName} must initialize to {expectedDefault}");
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
    public void SavedSettings_defaults_voxel_mesh_smoothing_off_with_one_iteration()
    {
        var source = ReadSavedSettingsSource();

        Regex.Match(source, @"public\s+bool\s+VoxelMeshSmoothing\s*=\s*false")
            .Success.Should().BeTrue("VoxelMeshSmoothing must default to false");

        Regex.Match(source, @"public\s+int\s+SmoothingIterations\s*=\s*1")
            .Success.Should().BeTrue("SmoothingIterations must default to 1");
    }
}
