using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for luminance-based depth complement settings
/// (docs/journeys/scratch/luminance-depth-spec.md).
/// </summary>
public sealed class LuminanceDepthInvariantsTests
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
    public void SavedSettings_documents_luminance_depth_spec_and_phase1_defaults()
    {
        var source = ReadSource(@"WorldSphereMod/Code/SavedSettings.cs");
        source.Should().Contain("luminance-depth-spec.md");

        Regex.IsMatch(source, @"public\s+bool\s+VoxelLuminanceDepth\s*=\s*false")
            .Should().BeTrue("feature stays off until SpriteVoxelizer hybrid path ships");
        Regex.IsMatch(source, @"public\s+float\s+VoxelNeutralLuminance\s*=\s*0\.5f")
            .Should().BeTrue("spec neutral_luminance default is 0.5");
        Regex.IsMatch(source, @"public\s+float\s+VoxelShadowRecession\s*=\s*1\.0f")
            .Should().BeTrue("spec shadow_recession default is 1.0");
    }

    [Fact]
    public void Luminance_depth_spec_maps_knobs_to_SavedSettings_fields()
    {
        var spec = ReadSource(@"docs/journeys/scratch/luminance-depth-spec.md");
        spec.Should().Contain("VoxelLuminanceDepth");
        spec.Should().Contain("VoxelNeutralLuminance");
        spec.Should().Contain("VoxelShadowRecession");
        spec.Should().Contain("VoxelSpriteDepth");
        spec.Should().Contain("SpriteVoxelizer");
    }

    [Fact]
    public void Bridge_invalidates_voxel_cache_when_luminance_depth_settings_change()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Bridge/BridgeServer.cs");
        source.Should().Contain("\"VoxelLuminanceDepth\"");
        source.Should().Contain("\"VoxelNeutralLuminance\"");
        source.Should().Contain("\"VoxelShadowRecession\"");
        source.Should().Contain("invalidateVoxel");
    }
}
