using System;
using System.IO;
using FluentAssertions;
using Xunit;

public class SpriteVoxelizerInvariantsTests
{
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
    public void SpriteVoxelizer_keeps_a_cached_pixel_path_and_empty_mesh_fallback_for_unreadable_textures()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs");

        source.Should().Contain("internal static Color32[] GetPixelsCached(Texture2D tex)");
        source.Should().Contain("public static void ClearPixelCache()");
        source.Should().Contain("public static Mesh Build(Sprite sprite, int depth = -1)");
        source.Should().Contain("public static Mesh BuildProxy(Sprite sprite)");
        source.Should().Contain("if (!sprite.texture.isReadable)");
        source.Should().Contain("return ReturnProfiled(null);",
            "unreadable sprite imports must fail closed instead of crashing the render path");
        source.Should().Contain("return CreateEmpty();",
            "the lower-level builders must return an empty mesh when the source texture cannot be used");
        source.Should().Contain("ColorTonemap.Tonemap(tex[row + x])",
            "the optional tonemap must remain wired into the voxel sampling loop");
    }

    [Fact]
    public void SpriteVoxelizer_build_pipeline_greedy_meshing_stays_pivoted_to_the_sprite()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs");

        source.Should().Contain("Rect r = sprite.textureRect;");
        source.Should().Contain("Vector3 origin = new Vector3(-pivot.x / ppu, -pivot.y / ppu, -(depth * 0.5f) / ppu);");
        source.Should().Contain("GreedyMesh(solid, color, w, h, depth, origin, cell, verts, cols, tris);");
        source.Should().Contain("mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;");
        source.Should().Contain("mesh.UploadMeshData(true);");
        source.Should().Contain("if (_buildGreedyDiagCount < 5)");
        source.Should().Contain("Debug.Log($\"[WSM3D][DIAG] BuildGreedy");
    }
}
