using System;
using System.IO;
using FluentAssertions;
using Xunit;

public class TileMapToSphereInvariantsTests
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
    public void TileMapToSphere_keeps_zone_patch_classes_and_redraw_entrypoints_declared()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/TileMapToSphere.cs");
        source.Should().Contain("class getzone3D");
        source.Should().Contain("class Generate3D");
        source.Should().Contain("class Queue3D");
        source.Should().Contain("public static class TileMapToSphere");
        source.Should().Contain("public static void Redraw3DTiles()");
        source.Should().Contain("public static void AddTileToTextureQueue(WorldTile pTile)");
        source.Should().Contain("public static void AddTileToScaleQueue(WorldTile pTile)");
        source.Should().Contain("public static void MarkBiomeBlendDirty()");
    }
}
