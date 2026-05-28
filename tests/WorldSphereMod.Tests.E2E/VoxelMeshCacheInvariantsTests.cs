using System;
using System.IO;
using FluentAssertions;
using Xunit;

public class VoxelMeshCacheInvariantsTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln"))) dir = dir.Parent;
        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    private static string ReadSourceFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void VoxelMeshCache_exposes_cache_lifecycle_and_build_entrypoints()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelMeshCache.cs");
        source.Should().Contain("public static Mesh Get(Sprite sprite, int depth = -1, bool forceSyncBuild = false)");
        source.Should().Contain("public static void PumpQueuedBuilds(int maxBuildsPerFrame = 1)");
        source.Should().Contain("public static void DrainCompletedBuilds(int maxCompletionsPerFrame = 8)");
        source.Should().Contain("public static void BeginFrame()");
        source.Should().Contain("public static void Clear()");
        source.Should().Contain("public static void DrainPendingDestroy()");
    }
}
