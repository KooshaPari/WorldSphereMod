using HarmonyLib;

namespace WorldSphereMod.Foliage
{
    /// <summary>
    /// Phase 3b scaffolding. Hooks <see cref="WorldTilemap.renderTile(WorldTile)"/>
    /// — the per-tile dispatcher inside the surface-overlay pipeline (grass,
    /// savanna, biomass, snow_sand, road, walls, etc). The original 2D path
    /// queues a <c>TileBase</c> into a <see cref="TilemapExtended"/> per layer
    /// and flushes through <c>Tilemap.SetTiles</c>; diff tracking lives on
    /// <c>WorldTile.current_rendered_tile_graphics</c> (a <c>TileBase</c>
    /// reference checked inside <c>TilemapExtended.addToQueueToRedraw</c> so
    /// unchanged tiles are skipped).
    ///
    /// Step 1 (this file): scaffold only. The Prefix returns <c>true</c>
    /// unconditionally so the upstream method runs verbatim and no behaviour
    /// changes. Gated by <see cref="SavedSettings.CrossedQuadFoliage"/> +
    /// <see cref="Core.IsWorld3D"/> when the real path lands.
    /// </summary>
    [HarmonyPatch(typeof(WorldTilemap), "renderTile")]
    public static class FoliageTileRender
    {
        // TODO Phase 3b Step 2:
        //   When IsWorld3D && CrossedQuadFoliage:
        //     1. Resolve the effective TileTypeBase the same way the upstream
        //        method does: prefer pTile.Type (top overlay) else
        //        pTile.main_type. Skip if null or considered_empty_tile.
        //     2. Filter to overlay-eligible TopTileType: e.g. tileType.grass,
        //        tileType.life, tileType.road, tileType.wall, or id prefix
        //        (grass_/savanna_/biomass_/snow_/road_/wall_) — finalize the
        //        predicate after a runtime dump of AssetManager.top_tiles.
        //     3. For matched tiles, fetch the variation Sprite (parallel to
        //        WorldTilemap.getVariation: pTile.Type.sprites.getRandom() or
        //        forced edge variation) and emit a crossed-quad mesh via
        //        CrossedQuadMesher.Build + MeshInstanceBatcher.Submit at
        //        Tools.To3DTileHeight(pTile.pos.x, pTile.pos.y).
        //     4. Maintain the diff-cache contract: stash the resolved Sprite
        //        (or a sentinel) on pTile.current_rendered_tile_graphics or a
        //        parallel dict keyed by WorldTile so the per-frame walk can
        //        early-out on unchanged tiles the way the upstream does.
        //     5. Return false to skip the Unity Tilemap.SetTiles flush for
        //        overlays we handle; still let the upstream run for tiles we
        //        don't claim (ocean borders, water_runup, pit borders) so the
        //        rest of the pipeline keeps working.
        [HarmonyPrefix]
        public static bool Prefix(WorldTilemap __instance, WorldTile pTile)
        {
            return true;
        }
    }
}
