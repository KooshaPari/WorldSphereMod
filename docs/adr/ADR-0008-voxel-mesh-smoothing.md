# ADR-0008 — Voxel mesh smoothing for SpriteVoxelizer output

**Status:** Proposed

**Date:** 2026-05-19

**Author:** KooshaPari

**Stakeholders:** WorldSphereMod Voxel pipeline (Phase 1), rendering/mesh cache path

---

## Context

`SpriteVoxelizer.Build` currently performs:
1. alpha-thresholded fill of a 3D bool/color voxel field from sprite pixels (`solid[x,y,z]`, `color[x,y,z]`),
2. `GreedyMesh` axis pass with per-slice `present`/`mask` arrays,
3. `EmitQuad` to generate large planar quads per exposed face.

The current mesh output is therefore axis-aligned cuboids with:

- merged rectangle faces per color run,
- no UV attribute generation,
- per-vertex baked `Color32`,
- `RecalculateNormals`/`RecalculateBounds` as the only curvature indicator,
- mesh upload with `UploadMeshData(true)` (CPU copy discarded for render caching path).

This produces blocky silhouettes; users are asking for No Cubes / VanillaPuddingTart-style rounded transitions while avoiding a full pipeline rework.

## Decision

Adopt a **post-Greedy Mesher Laplacian smoothing pass** on generated vertex positions, gated behind settings and off by default.

### Chosen option

- Perform 1–2 iterations by default (`SmoothingIterations = 1` proposed), with a max of 3 (`range 0–3`).
- Keep the current greedy-cuboid topology and topology topology graph so that UV-less, color-baked rendering continues to work unchanged.
- Add vertex-anchoring by face normal category (planar/edge/isolated corner) so concave/critical silhouette details are not collapsed toward uniform noise.

### Why not marching cubes / surface nets / subdivision now

- Marching cubes needs a dense scalar field at boundaries; current mesher path discards that density representation in favor of merged face quads.
- Surface nets / dual contouring gives cleaner gradients but needs a broader rewrite and additional ambiguity/edge-case handling per voxel column; estimated effort and regression surface is materially higher.
- Catmull-Clark subdivision significantly increases topology complexity (`~4x` verts per level) and will likely require new UV/normal flow and stronger LOD guardrails.
- Bevel/chamfer on edges is visually sharp and geometry-specific, but creates repeated edge-case artifacts on irregular sprite silhouettes and is less adaptable to varying biome/terrain densities.

### Non-functional decision

- Add a `SavedSettings` flag `VoxelMeshSmoothing` (default `false`) and `SmoothingIterations` (default `1`, range `0..3`) in `SavedSettings` proposal only.  
- Do not ship implementation in this ADR scope; decision only.

## Consequences

### Compute cost

- CPU mesh-build cost increases by the smoothing pass:
  - `O(V * iterations)` where `V` is vertex count after greedy merge.
  - For small sprites with strong face merging, expected +10% to +35% mesh build time at `1` iteration.
  - At `3` iterations, estimated +25% to +70% worst-case small-build latency.
- No explicit per-frame GPU cost increase is expected in the base case because smoothed positions are pre-baked in mesh build.
- Rendering remains unchanged in the shader side (still vertex colors only), so no additional shader binding cost.

### Visual change

- Curved transitions appear around high-curvature boundaries (corners, stairs, small diagonal-like silhouettes).
- Large flat faces remain mostly intact because normal-based anchoring preserves planar runs.
- Pixel-art edge readability should degrade only at high iteration counts, so clamp defaults to `1` with opt-in to `2`.

### Fallback path if disabled

- `SavedSettings.VoxelMeshSmoothing == false` keeps all current behavior: greedy meshing output and no additional smoothing.
- `SmoothingIterations == 0` is equivalent to disabled behavior and can be used for quick runtime A/B validation.

## Implementation steps

1. In `SpriteVoxelizer` analysis stage, keep `solid`/`color` storage but add a minimal temporary vertex graph structure after `EmitQuad` to track adjacency and anchor class.
2. Extend mesh build to collect `List<Vector3> verts`, `List<Color32> colors`, and `List<int> tris`, then map each vertex to its adjacent edge/face set and to source source color cell.
3. Add Laplacian smoothing helper:
   - `SmoothingIteration(vertices, adjacency, anchor, factor)`
   - anchor weights: 1.0 for outer silhouette/edge-anchored vertices, 0.2 for interior face vertices.
4. Apply 1–`SmoothingIterations` passes after greedy topology creation and before normal generation.
5. Re-run `mesh.RecalculateNormals()` after smoothing to keep lighting stable with VertexColor + directional pass.
6. Preserve color array order; keep colors unchanged by default to avoid palette drift.
7. Expose settings wiring in ADR proposal only: add new `SavedSettings.VoxelMeshSmoothing`, `SavedSettings.SmoothingIterations`, and defaults.
8. Add debug/profiling counters in proposal:
   - vertex count before/after smoothing,
   - iteration time per sprite,
   - fallback hit ratio when disabled.

## Open questions

1. Should smoothing apply in model space only, or should the pivot/local origin be normalized again after pass to preserve downstream cache hashes?
2. Which anchor weights best match the requested aesthetic: fixed normal-threshold-based anchors (`dot(normal, axis)`), feature-angle anchors, or an explicit curvature threshold?
3. Do we need a second fallback for legacy memory-sensitive devices (e.g., disable smoothing at low terrain density threshold regardless of flag)?

## References

### Internal

- `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs` — current greedy meshing output shape and vertex color flow.
- `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs` — cache contract for generated mesh lifetime.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs` and `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs` — render consumers.

### External

- https://graphics.stanford.edu/courses/cs468-06-fall/Papers/lorensen.pdf — Lorensen and Cline, "Marching Cubes."
- https://www.david-colson.com/2017/03/20/surface-nets.html — surface nets introduction and trade-offs.
- https://graphics.stanford.edu/courses/cs368-00-spring/lectures/tsl/tsl.pdf — classic Laplacian mesh smoothing discussion.
- https://0fps.net/2012/07/07/meshing-in-a-minecraft-game/ — cube field meshing techniques and quality alternatives.
- https://github.com/budko3f/no-cubes — public No Cubes reference (mesh generation direction and UX expectations).
