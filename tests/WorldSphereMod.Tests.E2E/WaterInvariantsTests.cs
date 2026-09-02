using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Invariant tests for the recovered Water system (FR-WSM-005).
///
/// The water surface pipeline was lost during the consolidation wave and recovered
/// as untracked source files, then committed. These source-level checks lock in the
/// public API surface of WaterSurface / WaterRender / WaterMaskBuffer so the water
/// feature cannot silently regress again.
/// </summary>
[Trait("Category", "E2E")]
public class WaterInvariantsTests
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

    static string ReadSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    // ------------------------------------------------------------------
    // 1. Water source files exist (recovered from consolidation loss)
    // ------------------------------------------------------------------
    [Fact]
    public void Water_source_files_exist_on_disk()
    {
        string root = FindRepoRoot();
        File.Exists(Path.Combine(root, "WorldSphereMod/Code/Water/WaterSurface.cs")).Should().BeTrue();
        File.Exists(Path.Combine(root, "WorldSphereMod/Code/Water/WaterRender.cs")).Should().BeTrue();
        File.Exists(Path.Combine(root, "WorldSphereMod/Code/Water/WaterMaskBuffer.cs")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // 2. WaterSurface is a MonoBehaviour singleton with Create/Destroy
    // ------------------------------------------------------------------
    [Fact]
    public void WaterSurface_is_MonoBehaviour_singleton()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Water/WaterSurface.cs");

        source.Should().Contain("public sealed class WaterSurface : MonoBehaviour",
            "WaterSurface must be a MonoBehaviour so it can own a MeshRenderer");
        source.Should().Contain("public static WaterSurface? Instance;",
            "static Instance field must exist for singleton pattern");
    }

    [Fact]
    public void WaterSurface_Create_and_Destroy_are_public_static()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Water/WaterSurface.cs");

        source.Should().Contain("public static WaterSurface? Create(Transform parent)",
            "Create must be public static for bootstrap");
        source.Should().Contain("public static void Destroy()",
            "Destroy must be public static for world-unload cleanup");
    }

    [Fact]
    public void WaterSurface_creates_named_GameObject_with_water_mesh()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Water/WaterSurface.cs");

        source.Should().Contain("new GameObject(\"WorldSphere Water\")",
            "water GameObject must use the stable WorldSphere Water name");
        source.Should().Contain("new Mesh { name = \"WorldSphere.Water\" }",
            "water mesh must be named WorldSphere.Water");
    }

    // ------------------------------------------------------------------
    // 3. WaterMaskBuffer exposes depth / isWater sampling API
    // ------------------------------------------------------------------
    [Fact]
    public void WaterMaskBuffer_exposes_depth_sampling_api()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Water/WaterMaskBuffer.cs");

        source.Should().Contain("public static float DepthAt(int tileIndex)",
            "DepthAt must be public static for depth sampling");
        source.Should().Contain("public static bool IsWater(int tileIndex)",
            "IsWater must be public static for water classification");
        source.Should().Contain("public static float MaxDepth()",
            "MaxDepth must be public static for shader normalization");
        source.Should().Contain("public static void RebuildMask()",
            "RebuildMask must be public static for tile-change invalidation");
        source.Should().Contain("public static void Clear()",
            "Clear must be public static for world-unload cleanup");
    }

    [Fact]
    public void WaterMaskBuffer_uses_liquid_and_ocean_tile_types()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Water/WaterMaskBuffer.cs");

        source.Should().Contain("(tt.liquid || tt.ocean)",
            "water classification must include liquid and ocean tile types");
    }

    // ------------------------------------------------------------------
    // 4. WaterRender wires Harmony patch lifecycle around Sphere Begin/Finish
    // ------------------------------------------------------------------
    [Fact]
    public void WaterRender_has_Begin_and_Finish_patches()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Water/WaterRender.cs");

        source.Should().Contain("public static class BeginPostfix",
            "Begin lifecycle patch must exist");
        source.Should().Contain("public static class FinishPrefix",
            "Finish lifecycle patch must exist");
    }

    [Fact]
    public void WaterRender_rebuilds_mask_and_mesh_on_lifecycle()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Water/WaterRender.cs");

        source.Should().Contain("WaterMaskBuffer.RebuildMask()",
            "water lifecycle must rebuild the water mask");
        source.Should().Contain("WaterSurface.Instance.RebuildMesh()",
            "water lifecycle must rebuild the surface mesh");
    }
}