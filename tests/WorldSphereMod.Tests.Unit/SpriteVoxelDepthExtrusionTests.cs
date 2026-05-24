using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for symmetric sprite voxel depth extrusion
/// (docs/journeys/scratch/voxel-depth-extrusion-spec.md).
/// </summary>
public sealed class SpriteVoxelDepthExtrusionTests
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
    public void SavedSettings_defaults_VoxelSpriteDepth_to_three()
    {
        var source = ReadSource(@"WorldSphereMod/Code/SavedSettings.cs");
        Regex.IsMatch(source, @"public\s+int\s+VoxelSpriteDepth\s*=\s*3")
            .Should().BeTrue("extrusion spec default depth is 3");
    }

    [Fact]
    public void SpriteVoxelizer_ResolveDepth_honors_explicit_positive_and_settings_fallback()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs");

        source.Should().Contain("internal static int ResolveDepth(int depth)");
        source.Should().Contain("if (depth > 0) return depth");
        source.Should().Contain("Core.savedSettings.VoxelSpriteDepth");
        source.Should().Contain("return configuredDepth > 0 ? configuredDepth : 3");
        Regex.IsMatch(source, @"public\s+static\s+Mesh\s+Build\(Sprite\s+sprite,\s*int\s+depth\s*=\s*-1\)")
            .Should().BeTrue("Build must accept unset depth via -1 default");
        Regex.IsMatch(source, @"depth\s*=\s*ResolveDepth\(depth\)")
            .Should().BeTrue("Build must resolve depth before voxel fill");
    }

    [Fact]
    public void SpriteVoxelizer_Build_symmetrically_centers_extrusion_on_Z()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs");

        source.Should().Contain("for (int z = 0; z < depth; z++)");
        source.Should().Contain("-(depth * 0.5f) / ppu",
            "pivot must stay centered when extrusion depth changes");
    }

    [Fact]
    public void VoxelMeshCache_Get_passes_unset_depth_to_build_queue()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/VoxelMeshCache.cs");

        Regex.IsMatch(source, @"public\s+static\s+Mesh\s+Get\(Sprite\s+sprite,\s*int\s+depth\s*=\s*-1")
            .Should().BeTrue("cache Get must default depth to -1");
        source.Should().Contain("EnqueueBuild(sprite, depth, key)");
        source.Should().Contain("EnqueueBuild(sprite, -1, key, shapeHint)");
        source.Should().Contain("SpriteVoxelizer.Build(sprite, out MeshSnapshot _, depth)");
    }

    [Theory]
    [InlineData("pertexel")]
    [InlineData("greedy")]
    [InlineData("extruded")]
    [InlineData("balloon")]
    [InlineData("lathe")]
    [InlineData("organicblob")]
    [InlineData("legacy-pertexel")]
    public void VoxelMeshCache_dispatch_recognizes_inflation_style(string style)
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/VoxelMeshCache.cs");
        source.Should().Contain($"\"{style}\"", because: $"BuildVoxelMesh should branch on {style}");
    }

    [Fact]
    public void Rig_paths_resolve_depth_via_unset_not_hardcoded_eight()
    {
        var cache = ReadSource(@"WorldSphereMod/Code/Voxel/VoxelMeshCache.cs");
        var rig = ReadSource(@"WorldSphereMod/Code/Rig/RigCache.cs");

        cache.Should().Contain("SpriteVoxelizer.BuildPerTexel(sprite, -1,");
        rig.Should().Contain("SpriteVoxelizer.BuildPerTexel(sprite, -1,");
        cache.Should().NotContain("SpriteVoxelizer.DefaultDepth");
        rig.Should().NotContain("SpriteVoxelizer.DefaultDepth");
    }

    [Fact]
    public void Bridge_invalidates_voxel_cache_when_VoxelSpriteDepth_changes()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Bridge/BridgeServer.cs");
        source.Should().Contain("\"VoxelSpriteDepth\"");
        source.Should().Contain("invalidateVoxel");
    }

    [Fact]
    public void SavedSettings_defaults_VoxelInflationStyle_to_pertexel()
    {
        var source = ReadSource(@"WorldSphereMod/Code/SavedSettings.cs");
        Regex.IsMatch(source, @"public\s+string\s+VoxelInflationStyle\s*=\s*""pertexel""")
            .Should().BeTrue("default inflation path must match symmetric greedy extrusion spec");
        source.Should().Contain("voxel-depth-extrusion-spec.md");
    }

    [Fact]
    public void VoxelMeshCache_lathe_forces_unset_depth_so_VoxelSpriteDepth_is_ignored()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/VoxelMeshCache.cs");
        source.Should().Contain("if (string.Equals(inflationStyle, \"lathe\"");
        source.Should().Contain("depth = -1");
        source.Should().Contain("SpriteVoxelizer.BuildLathe(sprite, depth");
    }

    [Theory]
    [InlineData("pertexel")]
    [InlineData("greedy")]
    [InlineData("extruded")]
    public void VoxelMeshCache_greedy_styles_route_to_Build_with_depth(string style)
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/VoxelMeshCache.cs");
        source.Should().Contain($"\"{style}\"");
        source.Should().Contain("return SpriteVoxelizer.Build(sprite, out MeshSnapshot _, depth)");
    }

    [Theory]
    [InlineData("BuildBalloon")]
    [InlineData("BuildOrganicBlob")]
    [InlineData("BuildPerTexel")]
    public void SpriteVoxelizer_non_lathe_builders_resolve_unset_depth(string method)
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs");
        var pattern = $@"public\s+static\s+Mesh\s+{method}\([^)]*int\s+depth";
        Regex.IsMatch(source, pattern).Should().BeTrue($"{method} must accept depth parameter");
        source.Should().Contain($"{method}(Sprite sprite, int depth");
        var methodStart = source.IndexOf($"public static Mesh {method}", System.StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0);
        var nextMethod = source.IndexOf("public static Mesh ", methodStart + 1, System.StringComparison.Ordinal);
        var body = nextMethod > methodStart
            ? source.Substring(methodStart, nextMethod - methodStart)
            : source.Substring(methodStart);
        body.Should().Contain("depth = ResolveDepth(depth)",
            $"{method} must honor VoxelSpriteDepth when depth is unset");
    }
}
