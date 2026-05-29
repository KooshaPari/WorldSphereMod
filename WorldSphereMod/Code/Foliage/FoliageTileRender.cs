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

                // Road remains a flat decal. Trees/bushes (.life) route through
                // CrossedQuadMeshCache first when the CrossedQuadFoliage flag is on
                // — that's the Phase 3 swaying-foliage path. If the cache cannot
                // build a crossed-quad mesh this frame (per-frame budget exhausted,
                // unreadable atlas, blank sprite), fall back to the OrganicBlob
                // voxel pathway so the tile still renders something visible.
                bool crossedQuadPath = t.life && Core.savedSettings.CrossedQuadFoliage;
                Mesh? mesh;
                if (t.road)
                {
                    mesh = CrossedQuadMeshCache.GetOrBuild(sprite, BuildingShape.Single, 0f);
                }
                else if (crossedQuadPath)
                {
                    // Crossed-quad is the authoritative .life path when the flag is on.
                    // GetOrBuild returns null only when the per-frame build budget is
                    // exhausted (CrossedQuadMesher.CanBuildThisFrame) — a transient
                    // condition, NOT a reason to permanently downgrade the tile to a
                    // voxel blob. Distinguish that from a genuine build failure (empty
                    // mesh = blank/unreadable atlas frame): retry next frame on budget
                    // exhaustion so the OrganicBlob fallback can't silently dominate
                    // and replace the swaying-foliage look.
                    float swayAmp = 0.15f;
                    mesh = CrossedQuadMeshCache.GetOrBuild(sprite, BuildingShape.CrossedQuad, swayAmp, sprite.name);
                    if (mesh == null)
                    {
                        // Budget exhausted this frame — skip the vanilla flush and let
                        // the dirty queue replay the tile next frame through the cache.
                        return false;
                    }
                    if (mesh.vertexCount == 0)
                    {
                        // Real failure (unreadable/blank sprite): voxel blob keeps the
                        // tile visible rather than leaving a hole.
                        mesh = VoxelMeshCache.Get(sprite, ShapeHint.OrganicBlob);
                        crossedQuadPath = false;
                    }
                }
                else
                {
                    mesh = VoxelMeshCache.Get(sprite, ShapeHint.OrganicBlob);
                }
                if (mesh == null || mesh.vertexCount == 0) return true;

                // Ground the quads on the terrain surface: To3DTileHeight resolves
                // the smooth tile height so the base verts (y0) sit on the ground
                // instead of floating at the world plane.
                Vector2 pos2 = new Vector2(pTile.pos.x, pTile.pos.y);
                Vector3 pos3 = Tools.To3DTileHeight(pos2);
                Quaternion rot = Tools.GetRotation(pTile.pos);

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
                    int seed = unchecked((pTile.pos.x * 73856093) ^ (pTile.pos.y * 19349663));
                    float jitter01 = ((seed & 0x7fffffff) % 1000) / 1000f;
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
                    int cseed = unchecked((pTile.pos.x * 83492791) ^ (pTile.pos.y * 521288629));
                    float bri = 0.88f + 0.24f * (((cseed & 0x7fffffff) % 1000) / 1000f);
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

        // Classify the foliage sprite into a silhouette variant. Mirrors the
        // private classifier in CrossedQuadMeshCache so the per-tile transform can
        // pick a matching base scale without reaching into that file.
        static CrossedQuadVariant ResolveVariant(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return CrossedQuadVariant.Generic;
            if (spriteName.IndexOf("palm", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return CrossedQuadVariant.Palm;
            if (spriteName.IndexOf("pine", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return CrossedQuadVariant.Pine;
            if (spriteName.IndexOf("oak", System.StringComparison.OrdinalIgnoreCase) >= 0
                || spriteName.StartsWith("tree_", System.StringComparison.OrdinalIgnoreCase))
                return CrossedQuadVariant.Oak;
            return CrossedQuadVariant.Generic;
        }

        // Per-variant base size multiplier applied to the crossed-quad billboard.
        // The mesher shapes the silhouette (slim pine, broad oak); this scales the
        // whole instance so the canopy footprint also reads distinctly per species.
        static float VariantBaseScale(CrossedQuadVariant variant)
        {
            switch (variant)
            {
                case CrossedQuadVariant.Oak: return 1.12f;
                case CrossedQuadVariant.Pine: return 1.05f;
                case CrossedQuadVariant.Palm: return 1.18f;
                default: return 1.0f;
            }
        }

        /// <summary>Drop the per-tile memo on world reload.</summary>
        public static void ClearCache()
        {
            _lastSprite.Clear();
        }
    }
}
