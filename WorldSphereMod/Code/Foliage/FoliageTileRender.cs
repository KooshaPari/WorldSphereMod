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
                // VOXEL-OR-INVISIBLE (task #195 follow-up): once in 3D the deprecated
                // vanilla 2D Tilemap billboard foliage must NEVER render. The voxel
                // foliage path is the SOLE renderer — we deliberately do NOT consult
                // CrossedQuadFoliage here. Re-enabling that runtime check (the old
                // `|| !CrossedQuadFoliage return true` escape) let a stale persisted
                // flag=false or an in-game toggle resurrect the deprecated crossed-quad/
                // billboard 2D path; that fallback is removed. The [Phase] attribute
                // still gates *patch installation* (default-on + version migration keep
                // it installed), but the runtime path no longer falls back to vanilla 2D.
                if (!Core.IsWorld3D) return true;
                if (pTile == null || pTile.Type == null) return true;

                TileTypeBase t = pTile.Type;
                // Foliage filter: surface overlays we claim. Walls/animated_wall
                // are deferred to Step 3's transpile; liquid/ocean/lava are handled
                // by the water mesh path.
                bool isFoliage = (t.grass || t.life || t.road) && !t.wall && !t.animated_wall
                                    && !t.liquid && !t.ocean && !t.lava;
                if (!isFoliage) return true;

                // VOXEL-OR-INVISIBLE for both branches (voxel mesh + cull/placeholder):
                // force the vanilla tilemap sprite off before any other processing.
                SuppressVanillaTileSprite(__instance, pTile, t);

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
                if (sprite == null) return false;

                if (t.life && !FoliageDensity.ShouldRender(pTile.pos.x, pTile.pos.y, sprite.name, Core.savedSettings.FoliageDensity))
                {
                    return false;
                }

                if (!FoliageMaterial.EnsureMaterial()) return false;
                Material? mat = FoliageMaterial.Get();
                if (mat == null) return false;

                // VOXEL-OR-INVISIBLE POLICY (user, 2026-05-30): foliage must be a REAL
                // voxel volume or NOTHING — NEVER a crossed-quad X / 2.5D slab. Grass
                // now routes through the rounded balloon voxelizer so it reads as a tuft,
                // while trees/bushes (.life) route through the OrganicBlob volume mesh.
                // Road remains a flat ground decal (it is genuinely a ground surface,
                // not an upright object).
                //
                // Important: a null return from GetOrBuild means the per-frame
                // build budget was exhausted. That is a transient "skip and retry
                // next frame" condition, not a signal to downgrade to voxel blobs.
                // Only a real build failure (empty mesh / vertexCount == 0) falls
                // back to OrganicBlob so we keep the crossed-quad look whenever the
                // cache can still produce it.
                bool crossedQuadPath = t.life && Core.savedSettings.CrossedQuadFoliage;
                Mesh? mesh;
                if (t.road)
                {
                    mesh = VoxelMeshCache.Get(sprite, ShapeHint.Flat, true, VoxelEntityType.Unknown);
                }
                else if (t.grass)
                {
                    mesh = VoxelMeshCache.Get(sprite, ShapeHint.Mirror, true, VoxelEntityType.Unknown);
                }
                else
                {
                    mesh = VoxelMeshCache.Get(sprite, ShapeHint.OrganicBlob, true, VoxelEntityType.Unknown);
                }
                if (mesh == null || mesh.vertexCount == 0)
                {
                    // Suppress the vanilla 2D overlay (return false) so no billboard shows;
                    // the voxel foliage pops in next frame once the async build completes.
                    return false;
                }

                // Ground the quads on the terrain surface: To3DTileHeight resolves
                // the smooth tile height so the base verts (y0) sit on the ground
                // instead of floating at the world plane.
                Vector2 pos2 = new Vector2(pTile.pos.x, pTile.pos.y);
                Vector3 pos3 = Tools.To3DTileHeight(pos2);
                Quaternion rot = Tools.GetRotation(pTile.pos);
                // WHY: foliage meshes are built at raw sprite-pixel size; without the
                // shared VoxelScaleMultiplier (8x) they render sub-pixel against the
                // 8x-scaled 3D world and are effectively invisible (same fix as the
                // actor/building/drop voxel paths in VoxelRender).
                float foliageScale = Mathf.Max(1f, Core.savedSettings.VoxelScaleMultiplier * Core.savedSettings.FoliageVoxelScaleFactor);
                Matrix4x4 trs = Matrix4x4.TRS(pos3, rot, Vector3.one * foliageScale);

                // Per-tree scale variety so a forest isn't a uniform stamp. The
                // mesher already differentiates oak/pine/palm silhouettes by
                // profile; here we add a deterministic per-tile size jitter (seeded
                // from the tile position so it's stable across frames/reloads) and a
                // variant-dependent base scale — palms/pines read taller, oaks
                // bushier. Crossed quads scale uniformly so the billboard stays
                // square; the voxel-blob fallback keeps Vector3.one to avoid
                // stretching the cube cluster.
                Vector3 scale = Vector3.one;
                if (crossedQuadPath)
                {
                    float jitter01 = DeterministicJitter01(pTile.pos.x, pTile.pos.y);
                    CrossedQuadVariant variant = ResolveVariant(sprite.name);
                    float baseScale = VariantBaseScale(variant);
                    // ±18% size spread around the variant base.
                    float s = baseScale * (0.82f + 0.36f * jitter01);
                    scale = new Vector3(s, s, s);
                }
                Matrix4x4 trs = Matrix4x4.TRS(pos3, rot, scale);

                if (!t.road && crossedQuadPath)
                {
                    WorldSphereMod.Fx.Environmental.EnqueueLeaf(pos3);
                    if (Core.savedSettings.DayNightCycle)
                    {
                        WorldSphereMod.Fx.Environmental.EnqueueFirefly(pos3);
                    }
                }

                // Per-instance tint sampled from the sprite's actual opaque pixels
                // (NOT a flat hard-coded green) — routed via Submit's color arg,
                // which the batcher feeds into _Color on the MaterialPropertyBlock.
                // OpaqueVertexColor / FoliageWind both multiply vertex.color × _Color,
                // so the sprite-derived hue comes through instead of emissive white.
                // A small per-tile brightness jitter (same seed family as the scale)
                // breaks up the flat look across a stand of trees while keeping the
                // color sprite-driven rather than a uniform tint.
                Color tint = SpriteAverageColorCache.Sample(sprite);
                if (crossedQuadPath)
                {
                    float bri = 0.88f + 0.24f * DeterministicJitter01(pTile.pos.x + 17, pTile.pos.y - 31);
                    tint = new Color(
                        Mathf.Clamp01(tint.r * bri),
                        Mathf.Clamp01(tint.g * bri),
                        Mathf.Clamp01(tint.b * bri),
                        tint.a);
                }
                MeshInstanceBatcher.Submit(mesh, mat, trs, tint);

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

        static void SuppressVanillaTileSprite(WorldTilemap tilemap, WorldTile tile, TileTypeBase tileType)
        {
            if (tilemap == null || tile == null || tileType == null) return;

            try
            {
                int renderZ = tileType.render_z;
                if (renderZ < 0 || tilemap._layers == null) return;

                Vector3Int renderPos = new Vector3Int(tile.pos.x, tile.pos.y, renderZ);
                Vector3Int lastRenderPos = tile.last_rendered_pos_tile;
                if (lastRenderPos.z != WorldTilemap.EMPTY_Z)
                {
                    if (tilemap._layers.TryGetValue(lastRenderPos.z, out TilemapExtended oldLayer))
                    {
                        oldLayer.addToQueueToRedraw(tile, lastRenderPos, null);
                    }
                    tile.last_rendered_pos_tile = WorldTilemap.EMPTY_TILE_POS;
                }

                if (tilemap._layers.TryGetValue(renderZ, out TilemapExtended layer))
                {
                    layer.addToQueueToRedraw(tile, renderPos, null);
                }

                tile.last_rendered_tile_type = tileType;
                tile.last_rendered_pos_tile = renderPos;
            }
            catch
            {
            }
        }

        /// <summary>Drop the per-tile memo on world reload.</summary>
        public static void ClearCache()
        {
            _lastSprite.Clear();
        }
    }
}
