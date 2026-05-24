using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for anatomical template scaffolding
/// (docs/journeys/scratch/anatomical-template-spec.md).
/// </summary>
public sealed class AnatomicalTemplateScaffoldTests
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

    static string ReadSource(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue($"source file must exist at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void AnatomicalTemplateTypes_exposes_region_and_occupancy_labels()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/AnatomicalTemplateTypes.cs");

        foreach (var name in new[] { "Head", "Core", "Limb", "Wing", "Tail" })
        {
            source.Should().Contain(name, $"AnatomicalRegion must include {name}");
        }

        source.Should().Contain("enum AnatomicalOccupancy");
        source.Should().Contain("struct AnatomicalTemplate");
        source.Should().Contain("RigType RigType");
    }

    [Fact]
    public void AnatomicalTemplateRegistry_maps_projection_modes_per_rig_family()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/AnatomicalTemplateRegistry.cs");

        source.Should().Contain("GetProjectionMode");
        source.Should().Contain("RigType.Humanoid");
        source.Should().Contain("AnatomicalProjectionMode.FrontFacing");
        source.Should().Contain("RigType.Insect");
        source.Should().Contain("AnatomicalProjectionMode.TopSideHybrid");
        source.Should().Contain("bool TryGetTemplate");
    }

    [Fact]
    public void AnatomicalTemplateValidation_enforces_minimum_voxel_count_and_connectivity()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/AnatomicalTemplateValidation.cs");

        source.Should().Contain("MinOccupiedVoxels = 8");
        source.Should().Contain("TryValidate");
        source.Should().Contain("disconnected body parts");
        source.Should().Contain("HasConnectedMass");
    }

    [Fact]
    public void AnatomicalTemplatePipeline_defers_to_extrusion_until_templates_exist()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/AnatomicalTemplatePipeline.cs");

        source.Should().Contain("ShouldUseTemplate");
        source.Should().Contain("RigType.None");
        source.Should().Contain("TryBuildColorizedTemplate");
        Regex.IsMatch(source, @"return\s+false\s*;", RegexOptions.Multiline)
            .Should().BeTrue("colorized build must stay off until projection is implemented");
    }

    [Fact]
    public void SpriteVoxelizer_remains_the_extrusion_fallback_floor()
    {
        var voxelizer = ReadSource(@"WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs");
        voxelizer.Should().Contain("public static Mesh Build(Sprite sprite");
    }
}
