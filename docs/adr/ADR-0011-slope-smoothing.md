# ADR-0011 --- Mountain slope smoothing: bilinear height-interpolated mesh overlay

**Status:** Accepted

**Date:** 2026-05-25

**Author:** KooshaPari

**Stakeholders:** WorldSphereMod terrain pipeline (Phase 2 terrain polish), rendering path, performance budget

---

## Context

### Problem Statement

WorldBox terrain is a flat tile grid where each tile has a discrete integer height. When viewed in 3D (WorldSphereMod's sphere projection), height transitions between adjacent tiles produce hard Minecraft-style staircase steps. Mountain ridges and coastlines look blocky and artificial, breaking the otherwise organic feel of the sphere world.

The original WorldSphereMod upstream addressed this with **billboard trapezoid quads** --- flat cardboard-cutout polygons stretched between height levels, textured with the adjacent biome colors. These quads face the camera and create the illusion of slopes, but under camera rotation they reveal themselves as paper-thin facades with no actual geometric depth. Players reported a persistent "cardboard cutout" feel, especially at oblique viewing angles and along ridgelines where multiple height steps stack.

### History

| Generation | Technique | File | Weakness |
|---|---|---|---|
| v0 (upstream) | Billboard trapezoid quads | Removed; was inline in tile redraw | Paper-thin, camera-facing, no true 3D depth |
| v1 (current, this ADR) | Bilinear height-interpolated mesh overlay | `WorldSphereMod/Code/Terrain/TerrainSmoothing.cs` | Low subdivision (SubDiv=2), faceted normals, still slightly angular on steep slopes |

### Forces

- The underlying tile data is immutable --- WorldBox's simulation runs on discrete integer heights. We can only change the visual overlay, never the logical grid.
- The mod already carries a significant per-frame GPU budget for voxelized actors, instanced batching, and sphere projection. Terrain smoothing must be cheap enough to not blow the frame budget.
- Cylindrical X-wrapping (`Core.Sphere.IsWrapped`) must be handled correctly; naive neighbor sampling breaks at the world seam.
- The overlay must integrate with the existing `OpaqueVertexColor` shader pipeline and vertex-color-only material path (no UVs, no texture atlas).
- The feature must be gated behind `SavedSettings.MountainSlopeSmoothing` (default OFF) and togglable at runtime without world reload.

### Alternatives Considered

| Alternative | Pros | Cons | Why not chosen |
|---|---|---|---|
| Billboard trapezoid quads (v0) | Simple, low vertex count | Paper-thin, breaks under rotation, no true 3D | Replaced by current implementation |
| Per-tile flat-shaded height offset | Trivial to implement | Still blocky, just raised blocks | Does not solve the core problem |
| Marching cubes on height field | Smooth isosurfaces, well-understood algorithm | Needs dense scalar field at boundaries; current tile system has no density representation; high vertex count for whole-map pass | Too expensive for overlay-only use case; see Future Work |
| Dual contouring / surface nets | Cleaner sharp-feature preservation than marching cubes | Requires hermite data (position + normal per edge crossing); significant rewrite of tile sampling; ambiguity cases at T-junctions | Estimated effort too high for Phase 2 scope |
| Catmull-Clark subdivision on existing mesh | Smooth limit surface | ~4x vertex multiplier per level; needs UV/normal flow that does not exist; LOD explosion | Overkill for terrain overlay |
| GPU tessellation (hull/domain shader) | Zero CPU vertex cost, adaptive detail | Requires shader model 5.0+; not compatible with current `OpaqueVertexColor` shader; Unity 2022.3 tessellation support is fragile on some drivers | Strong future candidate; see Future Work |

## Decision

Adopt a **bilinear height-interpolated mesh overlay** that generates smooth geometry at cliff/ridge transitions by averaging tile-corner heights from the four surrounding tile centers, subdividing each affected tile into a configurable grid, and projecting the result onto the sphere surface. The overlay is a single `Mesh` parented under the sphere rig, rebuilt on demand when tiles redraw.

### Architecture

The implementation lives in `WorldSphereMod/Code/Terrain/TerrainSmoothing.cs` as the `MountainSlopeSurface` MonoBehaviour and the `MountainSlopeRedrawPatch` Harmony postfix.

**Cliff detection.** `DetectCliffQuads` scans every tile pair (horizontal and vertical neighbors). When `|tileHeight - neighborHeight| > 1.0` (more than one height unit), a `CliffQuad` record is emitted carrying both heights and both biome colors. This threshold filters out flat plains and shallow water edges, focusing geometry budget on visible transitions.

**Neighbor expansion.** Each cliff quad expands to a 3x3 neighborhood of tiles (`dx,dy in [-1,+1]`). This ensures the smooth falloff extends one tile beyond the hard edge, preventing a visible seam between the smooth overlay and the flat underlying terrain.

**Corner height averaging.** `CornerHeight(cx, cy)` computes the interpolated height at the junction where four tiles meet: tiles `(cx-1,cy-1)`, `(cx,cy-1)`, `(cx-1,cy)`, and `(cx,cy)`. Their heights are averaged with equal 0.25 weight. This is the core smoothing primitive --- it converts discrete per-tile heights into a continuous per-corner height field, identical in principle to how OptiFine smooth lighting works for Minecraft.

**Subdivision grid.** Each tile in the smooth set is tessellated into a `(SubDiv+1) x (SubDiv+1)` vertex grid (currently `SubDiv=2`, yielding 9 vertices and 8 triangles per tile). Within the grid, vertex heights are **bilinearly interpolated** from the four corner heights:

```
h(fx, fy) = lerp(lerp(hBL, hBR, fx), lerp(hTL, hTR, fx), fy)
```

where `fx` and `fy` are the normalized position within the tile `[0..1]`. The same bilinear interpolation applies to vertex colors, blending the four corner biome colors smoothly across the tile face.

**Sphere projection.** Each interpolated vertex position `(worldX, worldY, h)` is projected onto the sphere surface via `Core.Sphere.SpherePos(worldX, worldY, h)`, which handles cylindrical wrapping, latitude/longitude mapping, and radial height offset. A small `HeightBias = 0.02` lifts the overlay above the flat terrain to prevent z-fighting.

**Material.** The overlay uses the same `OpaqueVertexColor` shader as voxelized actors, resolved from `Core.Sphere.LoadedShaders` cache with fallback to `Shader.Find`. Tint is forced to `Color.white` so vertex colors are the sole albedo source. A slight emission (`0.15, 0.15, 0.15`) ensures visibility under minimal scene lighting.

**Lifecycle.** The overlay is created/destroyed via `EnsureActive()`, called from `MountainSlopeRedrawPatch.OnRedraw()` (a Harmony postfix on `WorldTilemap.redrawTiles`). Runtime toggle changes route through `Core.ApplyPhaseToggle` for immediate create/destroy without world reload. `WorldUnloadPatch.OnFinish` tears down the overlay on world exit.

### Implementation Notes

- File: `WorldSphereMod/Code/Terrain/TerrainSmoothing.cs`
- Settings flag: `SavedSettings.MountainSlopeSmoothing` (default `false`)
- Phase gate: `[Phase(nameof(SavedSettings.MountainSlopeSmoothing))]`
- Mesh index format: `UInt32` (supports >65k vertices for large maps)
- Test coverage: `tests/WorldSphereMod.Tests.E2E/TerrainSmoothingInvariantsTests.cs` --- 11 source-invariant tests covering flag defaults, cliff detection thresholds, sphere projection, material resolution, lifecycle wiring, and toggle propagation

## Consequences

### Positive

- Mountain ridges and coastlines display smooth height gradients instead of hard staircase steps
- Biome color transitions blend smoothly across tile boundaries via corner color averaging
- The overlay integrates cleanly with the existing vertex-color shader pipeline --- no new shader, no UVs, no texture atlas
- Feature-flagged and default-OFF, zero risk to existing users
- Single-mesh architecture avoids per-tile draw call overhead
- Cylindrical wrapping is handled correctly at the world seam

### Negative

- **CPU rebuild cost.** Full cliff scan is `O(W * H)` over the tile grid on every `redrawTiles` call. For a 300x300 map with ~500 cliff tiles expanding to ~2000 smooth tiles, this produces ~18,000 vertices and ~16,000 triangles per rebuild. Measured at <5ms on a mid-range desktop but could spike on very large or mountainous maps.
- **No incremental update.** The entire mesh is rebuilt from scratch on any tile change. A dirty-region approach would reduce rebuild cost but adds complexity.
- **Low subdivision.** `SubDiv=2` produces only 4 sub-quads per tile. On steep multi-step cliffs, individual facets are still visible. The mesh looks smoother than billboard quads but not as smooth as players expect from modern terrain rendering.
- **Flat-shaded normals.** `RecalculateNormals()` computes per-vertex normals by averaging adjacent face normals, but with only 9 vertices per tile the normal field is coarse. Steep slopes can show visible lighting facets.
- **No shared vertices at tile boundaries.** Adjacent tiles in the smooth set generate independent vertex grids. Boundary vertices at the same position are duplicated, not shared, which wastes memory and can produce hairline lighting seams if normals differ slightly.

### Neutral

- The overlay mesh is parented under the sphere rig and transforms with it; no additional transform management needed
- The `CliffQuad` struct captures data for both horizontal and vertical edges but the current implementation does not distinguish between them during mesh generation

## Remaining Issues

These are known shortcomings in the current implementation that warrant future attention:

1. **"Billboard slope" feel persists.** Even with bilinear interpolation, the overlay can feel flat on steep single-step cliffs because the height gradient spans only one tile width. The geometry is correct but the visual impression is that of a ramp decal rather than true terrain.

2. **Subdivision is too low for steep gradients.** `SubDiv=2` means 4 sub-quads per tile edge. A cliff face spanning 3+ height units produces visible linear facets. Increasing to `SubDiv=4` (16 sub-quads, 25 vertices per tile) would significantly improve curvature at the cost of 2.78x more vertices.

3. **Normal smoothing is absent.** The current `RecalculateNormals()` call produces faceted normals. A post-pass that averages normals across shared-position vertices (even across tile boundaries) would eliminate lighting seams. Alternatively, computing analytic normals from the bilinear height derivatives would produce perfectly smooth shading at zero extra geometry cost.

4. **No shared boundary vertices.** Merging vertices at tile edges would halve boundary vertex count and guarantee normal continuity. Requires a position-keyed vertex dedup pass during mesh build.

5. **Height bias is a magic number.** `HeightBias = 0.02` works for typical sphere radii but may z-fight or visibly hover at extreme zoom levels. Should be proportional to sphere radius or camera distance.

## Future Work

### Short-term (next 1--2 waves)

- **Increase SubDiv to 4** behind a quality setting (`SlopeMeshQuality: Low=2, Medium=4, High=8`). Profile the vertex budget impact.
- **Analytic normal computation.** The bilinear surface `h(u,v) = lerp(lerp(...))` has closed-form partial derivatives. Computing `dh/du` and `dh/dv` and crossing them gives exact smooth normals without any post-pass.
- **Vertex deduplication at tile boundaries.** Build a `Dictionary<(float,float,float), int>` during mesh generation to merge coincident vertices.
- **Incremental rebuild.** Track dirty tile regions and only regenerate the affected smooth tiles instead of the full mesh.

### Medium-term (Phase 6+)

- **GPU tessellation.** Replace the CPU-side subdivision grid with a hull/domain shader that tessellates at runtime based on camera distance. This eliminates the SubDiv tradeoff entirely --- close tiles get high tessellation, distant tiles stay coarse. Requires moving to a tessellation-capable shader, which is a non-trivial departure from the current `OpaqueVertexColor` pipeline but would be a significant quality and performance win.
- **Marching cubes on height field.** For worlds with many vertical cliff faces (caves, overhangs if WorldBox ever adds them), marching cubes on the 3D height field would produce true volumetric slopes. Current WorldBox terrain is strictly a 2D heightmap, so this is speculative unless the simulation model changes.
- **Dual contouring.** Better sharp-feature preservation than marching cubes, producing cleaner ridge lines. Requires hermite data (edge crossing positions + normals) which would need to be synthesized from the tile height field. High implementation cost but best visual quality for terrain with both smooth slopes and sharp cliff edges.

### Aspirational

- **Erosion-aware smoothing.** Weight the corner height average by biome type --- sand/soil tiles get more aggressive smoothing (wider blend radius), rock/mountain tiles preserve sharper edges. This would produce naturally varied slope profiles without increasing geometry.
- **LOD integration.** At far zoom levels, skip the overlay entirely (the height steps are sub-pixel). At medium zoom, use SubDiv=2. At close zoom, use SubDiv=8 or GPU tessellation. Tie into the existing `LodSelector` infrastructure.

## Performance Budget

### Current cost (SubDiv=2)

| Map size | Typical cliff tiles | Smooth tiles (3x3 expand) | Vertices | Triangles | Rebuild time (est.) |
|---|---|---|---|---|---|
| 100x100 | ~80 | ~400 | ~3,600 | ~3,200 | <1ms |
| 200x200 | ~300 | ~1,500 | ~13,500 | ~12,000 | ~3ms |
| 300x300 | ~500 | ~2,500 | ~22,500 | ~20,000 | ~5ms |
| 500x500 (max) | ~1,200 | ~5,000 | ~45,000 | ~40,000 | ~12ms |

At 60 FPS the per-frame budget is 16.6ms. The overlay rebuilds only on `redrawTiles` (not every frame), so the rebuild cost is amortized. The GPU draw cost for a single 45k-vertex mesh with no texture sampling is negligible (<0.5ms on integrated GPUs).

**Scaling concern:** If SubDiv increases to 4, vertex counts multiply by ~2.78x. A 500x500 map would produce ~125k vertices --- still within the UInt32 index buffer and well under GPU limits, but rebuild time would approach 30ms. This motivates the move to GPU tessellation for high-quality settings.

**Target:** The overlay must not cause a sustained frame drop below 60 FPS on the minimum spec (integrated GPU, 300x300 map). Current SubDiv=2 meets this target with headroom. SubDiv=4 is borderline and should be gated behind a quality setting. SubDiv=8+ requires GPU tessellation to be viable.

## References

### Internal

- `WorldSphereMod/Code/Terrain/TerrainSmoothing.cs` --- current implementation (this ADR documents)
- `WorldSphereMod/Code/Voxel/MeshSmoother.cs` --- Laplacian smoothing for voxel meshes (ADR-0008); complementary but separate pipeline
- `WorldSphereMod/Code/SavedSettings.cs:88` --- `MountainSlopeSmoothing` flag
- `WorldSphereMod/Code/Core.cs` --- `ApplyPhaseToggle` lifecycle, `Sphere.SpherePos` projection, `Sphere.GetColor` biome sampling
- `WorldSphereMod/Code/TileMapToSphere.cs` --- biome blend dirty tracking
- `WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs` --- overlay teardown on world exit
- `tests/WorldSphereMod.Tests.E2E/TerrainSmoothingInvariantsTests.cs` --- source invariant tests
- ADR-0008 --- Voxel mesh smoothing (Laplacian post-pass on actor meshes; different pipeline, shared design philosophy)

### External

- https://0fps.net/2012/07/07/meshing-in-a-minecraft-game/ --- cube field meshing survey
- https://graphics.stanford.edu/courses/cs468-06-fall/Papers/lorensen.pdf --- Lorensen and Cline, "Marching Cubes"
- https://www.david-colson.com/2017/03/20/surface-nets.html --- surface nets introduction
- https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-7-adaptive-tessellation-subdivision-surfaces --- GPU tessellation for terrain
- OptiFine smooth lighting algorithm --- same bilinear corner-averaging principle applied to Minecraft light values rather than heights
