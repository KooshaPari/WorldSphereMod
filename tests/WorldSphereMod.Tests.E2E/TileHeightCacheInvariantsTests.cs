using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Performance invariant for the GetTileHeightSmooth cache. The former per-frame
/// ClearTileHeightSmoothCache call cold-missed EVERY entity on the clear frame — a
/// ~1160ms precalc spike with ~531 buildings. It was replaced by a bounded LRU that
/// evicts only the single oldest entry on overflow, keeping the cache permanently warm.
/// These tests assert the LRU pattern lives in Tools.cs and that VoxelRender's
/// EmitVoxels postfixes do NOT call ClearTileHeightSmoothCache per frame.
/// </summary>
public sealed class TileHeightCacheInvariantsTests
{
    const string ToolsRelative = "WorldSphereMod/Code/Tools.cs";
    const string VoxelRenderRelative = "WorldSphereMod/Code/Voxel/VoxelRender.cs";

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
    public void Tools_uses_bounded_LRU_for_tile_height_smooth()
    {
        var tools = ReadSource(ToolsRelative);

        tools.Should().Contain("class TileHeightSmoothLru",
            "GetTileHeightSmooth must be backed by a bounded LRU type");
        tools.Should().Contain("TileHeightSmoothCacheCap",
            "the LRU must declare a fixed capacity cap");
        // The LRU must evict only the single least-recently-used entry, not a full clear.
        Regex.IsMatch(tools, @"i\s*=\s*_tail").Should().BeTrue(
            "LRU overflow must reuse the least-recently-used (_tail) slot, evicting only one entry");
        tools.Should().Contain("[ThreadStatic]",
            "the cache must be [ThreadStatic] so the parallel precalc passes need no locking");
    }

    [Fact]
    public void GetTileHeightSmooth_reads_cache_before_recomputing()
    {
        var tools = ReadSource(ToolsRelative);

        tools.Should().Contain("cache.TryGet(pos, out float cachedHeight)",
            "GetTileHeightSmooth must consult the LRU before recomputing a tile height");
        tools.Should().Contain("cache.Set(pos, height)",
            "GetTileHeightSmooth must populate the LRU on a miss to keep it warm");
    }

    [Fact]
    public void EmitVoxels_does_not_clear_tile_height_cache_per_frame()
    {
        var voxelRender = ReadSource(VoxelRenderRelative);

        // The only permitted mention is the explanatory comment documenting that the
        // per-frame clear was removed. An actual ClearTileHeightSmoothCache() invocation
        // here would reintroduce the cold-miss storm.
        Regex.IsMatch(voxelRender, @"Tools\.ClearTileHeightSmoothCache\s*\(\s*\)").Should().BeFalse(
            "EmitVoxels must NOT call Tools.ClearTileHeightSmoothCache() per frame (reintroduces ~1160ms cold-miss storm)");
    }
}
