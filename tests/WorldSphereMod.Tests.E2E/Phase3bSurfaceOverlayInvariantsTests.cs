using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Phase 3b surface-overlay + wall patch invariants: Harmony Prefix wiring on
/// WorldTilemap.renderTile and QuantumSpriteLibrary.drawWallType, shared mesh
/// submit path, overlay allow-list, and world-unload cache drain (HANDOFF Phase 3b).
/// </summary>
public sealed class Phase3bSurfaceOverlayInvariantsTests
{
    const string FoliageTileRenderRelative = "WorldSphereMod/Code/Foliage/FoliageTileRender.cs";
    const string WallTileRenderRelative = "WorldSphereMod/Code/Foliage/WallTileRender.cs";
    const string WorldUnloadPatchRelative = "WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs";
    const string HandoffRelative = "docs/HANDOFF.md";

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
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    static string ExtractMethodBody(string source, string signature)
    {
        int headerIndex = source.IndexOf(signature, StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0, $"method signature should exist: {signature}");

        int openBrace = source.IndexOf('{', headerIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0, "method must open with a '{'");

        int depth = 0;
        for (int i = openBrace; i < source.Length; i++)
        {
            char c = source[i];
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

    static string ExtractClassSection(string source, string className)
    {
        int classIndex = source.IndexOf($"class {className}", StringComparison.Ordinal);
        classIndex.Should().BeGreaterThanOrEqualTo(0, $"{className} must exist in source");

        int sectionStart = Math.Max(0, classIndex - 512);
        var prior = source.Substring(sectionStart, classIndex - sectionStart);
        int attrIndex = prior.LastIndexOf("[Phase(", StringComparison.Ordinal);
        if (attrIndex >= 0)
        {
            sectionStart = sectionStart + attrIndex;
        }

        int openBrace = source.IndexOf('{', classIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0, $"{className} must open with a '{{'");

        int depth = 0;
        for (int i = openBrace; i < source.Length; i++)
        {
            char c = source[i];
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
                return source.Substring(sectionStart, i - sectionStart + 1);
            }
        }

        throw new InvalidOperationException($"Unbalanced braces while extracting {className}");
    }

    [Fact]
    public void Handoff_documents_phase3b_renderTile_and_drawWallType_prefix_wiring()
    {
        var handoff = ReadSourceFile(HandoffRelative);

        handoff.Should().Contain("3b Surface overlays + walls");
        handoff.Should().Contain("`WorldTilemap.renderTile` Prefix + `drawWallType` Prefix wired");
    }

    [Fact]
    public void FoliageTileRender_harmony_prefix_wires_WorldTilemap_renderTile_with_CrossedQuadFoliage_gate()
    {
        var source = ReadSourceFile(FoliageTileRenderRelative);
        var section = ExtractClassSection(source, "FoliageTileRender");

        Regex.IsMatch(section, @"\[Phase\(nameof\(SavedSettings\.CrossedQuadFoliage\)\)\]")
            .Should().BeTrue("surface overlay emit must respect the CrossedQuadFoliage saved setting");
        Regex.IsMatch(section, @"\[HarmonyPatch\(typeof\(WorldTilemap\),\s*""renderTile""\)\]")
            .Should().BeTrue("Phase 3b must prefix WorldTilemap.renderTile at the per-tile dispatcher");
        section.Should().Contain("[HarmonyPrefix]");
        section.Should().Contain("public static bool Prefix(WorldTilemap __instance, WorldTile pTile)");

        var prefixBody = ExtractMethodBody(source, "public static bool Prefix(WorldTilemap __instance, WorldTile pTile)");
        prefixBody.Should().Contain("!Core.IsWorld3D");
        prefixBody.Should().Contain("!Core.savedSettings.CrossedQuadFoliage");
        prefixBody.Should().Contain("t.grass || t.life || t.road");
        prefixBody.Should().Contain("!t.wall && !t.animated_wall");
        prefixBody.Should().Contain("!t.liquid && !t.ocean && !t.lava");
        prefixBody.Should().Contain("__instance.getVariation(pTile)");
        prefixBody.Should().Contain("FoliageMaterial.EnsureMaterial()");
        prefixBody.Should().Contain("MeshInstanceBatcher.Submit(mesh, mat, trs, Color.white)");
        prefixBody.Should().Contain("return false",
            "successful overlay emit must skip the upstream Tilemap flush");
    }

    [Fact]
    public void FoliageTileRender_routes_road_and_life_overlays_through_shared_mesh_pipeline()
    {
        var source = ReadSourceFile(FoliageTileRenderRelative);
        var prefixBody = ExtractMethodBody(source, "public static bool Prefix(WorldTilemap __instance, WorldTile pTile)");

        prefixBody.Should().Contain("CrossedQuadMeshCache.GetOrBuild(sprite, BuildingShape.Single, 0f)",
            "road overlays must stay flat decals via the crossed-quad cache");
        prefixBody.Should().Contain("VoxelMeshCache.Get(sprite, ShapeHint.OrganicBlob)",
            "life/grass overlays must use the shared voxel mesh cache");
        prefixBody.Should().Contain("Tools.To3DTileHeight(pos2)",
            "overlay TRS must lift tile height like other 3D emit paths");
        prefixBody.Should().Contain("mesh.vertexCount == 0",
            "empty atlas meshes must not waste draw calls");
        prefixBody.Should().Contain("FoliageDensity.ShouldRender",
            "life overlays must honor the foliage density knob");
    }

    [Fact]
    public void FoliageTileRender_exposes_ClearCache_and_world_unload_drains_sprite_memo()
    {
        var source = ReadSourceFile(FoliageTileRenderRelative);

        source.Should().Contain("public static void ClearCache()");
        source.Should().Contain("_lastSprite.Clear()");

        var unload = ReadSourceFile(WorldUnloadPatchRelative);
        unload.Should().Contain("WorldSphereMod.Foliage.FoliageTileRender.ClearCache()",
            "world unload must drop per-tile overlay sprite memo");
    }

    [Fact]
    public void WallTileRender_harmony_prefix_wires_drawWallType_with_prism_mesh_emit()
    {
        var source = ReadSourceFile(WallTileRenderRelative);
        var section = ExtractClassSection(source, "WallTileRender");

        Regex.IsMatch(section, @"\[Phase\(nameof\(SavedSettings\.CrossedQuadFoliage\)\)\]")
            .Should().BeTrue("wall mesh emit must respect the CrossedQuadFoliage saved setting");
        Regex.IsMatch(
                section,
                @"\[HarmonyPatch\(typeof\(QuantumSpriteLibrary\),\s*nameof\(QuantumSpriteLibrary\.drawWallType\)\)\]")
            .Should().BeTrue("Phase 3b must prefix QuantumSpriteLibrary.drawWallType at the wall flush boundary");
        section.Should().Contain("[HarmonyPrefix]");
        section.Should().Contain(
            "public static bool Prefix(TopTileType pTileTypeAsset, QuantumSpriteAsset pAsset, bool pTransparentBuildings, Material pMaterial)");

        var prefixBody = ExtractMethodBody(
            source,
            "public static bool Prefix(TopTileType pTileTypeAsset, QuantumSpriteAsset pAsset, bool pTransparentBuildings, Material pMaterial)");
        prefixBody.Should().Contain("!Core.IsWorld3D");
        prefixBody.Should().Contain("!Core.savedSettings.CrossedQuadFoliage");
        prefixBody.Should().Contain("pTileTypeAsset.animated_wall");
        prefixBody.Should().Contain("pTileTypeAsset.getCurrentTiles()");
        prefixBody.Should().Contain("tiles == null || tiles.Count == 0");
        prefixBody.Should().Contain("t.zone == null || !t.zone.visible");
        prefixBody.Should().Contain("Tools.To3DTileHeight(pos2)");
        prefixBody.Should().Contain("MeshInstanceBatcher.Submit(mesh, mat, trs, Color.white)");
        prefixBody.Should().Contain("return false",
            "successful wall emit must suppress the vanilla QuantumSprite wall flush");
    }

    [Fact]
    public void WallTileRender_builds_shared_extruded_prism_and_exposes_Reset()
    {
        var source = ReadSourceFile(WallTileRenderRelative);

        source.Should().Contain("static Mesh GetOrBuildPrism()");
        source.Should().Contain("name = \"wsm3d.wall.prism\"");
        source.Should().Contain("public static void Reset()");
        source.Should().Contain("Object.Destroy(_sharedPrism)");
    }
}
