using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Tilemaps;
using WorldSphereMod.ProcGen;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Foliage
{
    /// <summary>
    /// Phase 3b Step 2. Hooks <see cref="WorldTilemap.renderTile(WorldTile)"/> —
    /// the per-tile dispatcher inside the surface-overlay pipeline (grass,
    /// savanna, biomass, snow_sand, road, walls, etc).
    ///
    /// For tiles tagged as grass / life / road we resolve the variation
    /// sprite WorldBox would have flushed into the Tilemap, build a voxel
    /// foliage mesh for trees/bushes (or a single ground quad for road), and
    /// submit it to <see cref="MeshInstanceBatcher"/>. Walls and water are
    /// left to the vanilla flush — walls get their own transpile in Step 3,
    /// and liquid/ocean already route through the water mesh.
    ///
    /// Diff cache: parallel <c>WorldTile -&gt; Sprite</c> map mirrors
    /// <c>WorldTile.last_rendered_tile_type</c>. When the resolved sprite
    /// matches the cached one we still re-submit (the batcher accumulates
    /// per-frame draws) but skip the mesh rebuild via
    /// <see cref="VoxelMeshCache"/>.
    /// </summary>
    [Phase(nameof(SavedSettings.CrossedQuadFoliage))]
    [HarmonyPatch(typeof(WorldTilemap), "renderTile")]
    public static class FoliageTileRender
    {
        // Per-tile sprite memo. Mirrors the diff key WorldBox uses on
        // current_rendered_tile_graphics so subsequent frames can early-out
        // before resolving the variation again. Not strictly needed for
        // correctness — the cache layer dedupes builds — but keeps the
        // per-tile path cheap when the dirty queue replays an unchanged tile.
        static readonly Dictionary<WorldTile, Sprite> _lastSprite = new Dictionary<WorldTile, Sprite>(4096);

        [HarmonyPrefix]
        public static bool Prefix(WorldTilemap __instance, WorldTile pTile)
        {
            try
            {
                if (!Core.IsWorld3D || !Core.savedSettings.CrossedQuadFoliage) return true;
                if (pTile == null || pTile.Type == null) return true;

                TileTypeBase t = pTile.Type;
                // Foliage filter: surface overlays we claim. Walls/animated_wall
                // are deferred to Step 3's transpile; liquid/ocean/lava are handled
                // by the water mesh path.
                bool isFoliage = (t.grass || t.life || t.road) && !t.wall && !t.animated_wall
                                    && !t.liquid && !t.ocean && !t.lava;
                if (!isFoliage) return true;

                // Resolve the variation sprite the vanilla path would have flushed.
                // WorldTilemap.getVariation returns a UnityEngine.Tilemaps.Tile whose
                // .sprite is the atlas-resolved frame. Assembly-CSharp-Publicized
                // exposes the private member directly.
                Sprite? sprite = null;
                try
                {
                    Tile variation = __instance.getVariation(pTile);
                    if (variation != null) sprite = variation.sprite;
                }
                catch
                {
                    sprite = null;
                }
                // Fallback: TileSprites.main if the variation lookup didn't yield
                // a usable sprite (e.g. force_edge_variation with a sparse atlas).
                if (sprite == null)
                {
                    var ts = t.sprites;
                    if (ts != null)
                    {
                        try { sprite = ts.main?.sprite; } catch { /* fall through */ }
                    }
                }
                if (sprite == null) return true;

                if (t.life && !FoliageDensity.ShouldRender(pTile.pos.x, pTile.pos.y, sprite.name, Core.savedSettings.FoliageDensity))
                {
                    return false;
                }

                if (!FoliageMaterial.EnsureMaterial()) return true;
                Material? mat = FoliageMaterial.Get();
                if (mat == null) return true;

                // Road remains a flat decal. Foliage now uses voxel meshes so
                // trees render with actual 3D depth on all axes.
                Mesh? mesh = t.road
                    ? CrossedQuadMeshCache.GetOrBuild(sprite, BuildingShape.Single, 0f)
                    : VoxelMeshCache.Get(sprite, ShapeHint.OrganicBlob);
                if (mesh == null || mesh.vertexCount == 0) return true;

                Vector2 pos2 = new Vector2(pTile.pos.x, pTile.pos.y);
                Vector3 pos3 = Tools.To3DTileHeight(pos2);
                Quaternion rot = Tools.GetRotation(pTile.pos);
                Matrix4x4 trs = Matrix4x4.TRS(pos3, rot, Vector3.one);

                if (!t.road && t.life && Core.savedSettings.CrossedQuadFoliage)
                {
                    WorldSphereMod.Fx.Environmental.EnqueueLeaf(pos3);
                    if (Core.savedSettings.DayNightCycle)
                    {
                        WorldSphereMod.Fx.Environmental.EnqueueFirefly(pos3);
                    }
                }

                MeshInstanceBatcher.Submit(mesh, mat, trs, Color.white);

                // Update the diff memo. The cached sprite reference lets a future
                // pass skip re-resolving the variation when the tile is still in
                // the same TileType; vanilla's own diff key
                // (last_rendered_tile_type) still drives whether renderTile gets
                // called in the first place.
                _lastSprite[pTile] = sprite;

                // Skip the upstream Tilemap.SetTiles flush — we drew the overlay.
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[WSM3D] FoliageTileRender.Prefix: " + ex);
                return true;
            }
        }

        /// <summary>Drop the per-tile memo on world reload.</summary>
        public static void ClearCache()
        {
            _lastSprite.Clear();
        }
    }
}
