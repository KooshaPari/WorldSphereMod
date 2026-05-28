# ADR-0017: Terrain Architecture — CompoundSpheres Upgrade Path and Complement DLLs

**Status:** Proposed

**Date:** 2026-05-26

**Author:** Claude / KooshaPari

**Stakeholders:** WorldSphereMod3D, CompoundSpheres library, Phenotype org polyrepo ecosystem

---

## Context

WorldSphereMod3D renders terrain through CompoundSpheres, a GPU-instanced
tile engine that maps a 2D grid of WorldBox tiles onto a cylindrical or flat
3D surface. Each tile is a single quad (the mesh set via
`SphereManagerSettings.SphereTileMesh`, default `Quad`) positioned,
rotated, and scaled per-instance via four `StructuredBuffer`s (Matrixes
64B, Scales 12B, Colors 4B, Textures 4B) uploaded to the GPU and drawn
with `Graphics.RenderMeshIndirect`. On a standard 461x720 map this is
331,920 independent flat quads.

The result is inherently blocky terrain — Minecraft-style discrete height
steps where each tile sits at a flat elevation determined by
`WorldTile.TileHeight()`. Height transitions between adjacent tiles are
vertical cliff faces with no geometric continuity.

`TerrainSmoothing.cs` (`MountainSlopeSurface`) attempts to fix this by
detecting cliff edges between tiles with differing heights, expanding to
neighboring tiles, and generating a bilinear-interpolated overlay mesh
with a 4x4 sub-grid per tile (`SubDiv = 4`). This overlay sits 0.05
units above the flat terrain to avoid z-fighting. While it softens cliff
edges visually, it has fundamental limitations:

1. **Double-draw waste.** The flat CompoundSpheres quads still render
   underneath the smooth overlay. Both contribute fragments.
2. **Seam artifacts.** The overlay mesh and the flat quads use different
   materials and shading paths (OpaqueVertexColor vs CompoundSpheres
   instanced shader). Color/lighting discontinuities are visible at
   overlay boundaries.
3. **No texture splatting.** The overlay interpolates vertex colors only.
   Biome texture transitions (grass-to-sand, forest-to-snow) remain
   hard-edged on the underlying CompoundSpheres quads.
4. **CPU-bound rebuild.** `RebuildMesh()` iterates every tile on the map,
   detects cliff quads, expands to neighbors, and builds the mesh list
   on the main thread. On large maps this is a multi-frame hitch.
5. **Static geometry.** The overlay only rebuilds on `WorldTilemap.redrawTiles`,
   not on per-tile height changes. Terraforming tools cause stale slopes.

These limitations cannot be fixed within the current architecture because
the root cause is CompoundSpheres' per-tile flat-quad model. Smooth
terrain requires a fundamentally different mesh topology.

## Decision

Adopt a two-track upgrade path: (A) fork CompoundSpheres into a
height-field-aware terrain engine, and (B) extract reusable subsystems
into Phenotype-org complement DLLs that WSM3D and sibling projects
(Civis, phenotype-voxel) consume as shared dependencies.

### Track A: Compound-Spheres-3D Fork

Replace the per-tile flat quad instancing model with a continuous
height-field mesh, delivered as a Git submodule at
`External/Compound-Spheres-3D/`.

#### A1. Height-Field Mesh Generation

Replace `SphereTile` + `SphereRow` + per-tile `Matrix4x4` with a
chunk-based height-field mesh. Each chunk covers an NxN tile region
(target: 32x32 = 1024 tiles per chunk). The mesh is a regular grid where
each vertex height is sampled from `WorldTile.TileHeight()` with optional
bilinear interpolation at corners (the same averaging
`MountainSlopeSurface.CornerHeight` already computes, but baked into the
primary mesh instead of an overlay).

**Key changes to CompoundSpheres API:**

| Current | Proposed |
|---|---|
| `SphereTile[]` flat array, one struct per tile | `TerrainChunk[]`, one mesh per 32x32 region |
| `SphereRow.DrawTiles()` via `RenderMeshIndirect` per row | `TerrainChunk.Draw()` via `Graphics.DrawMesh` per chunk |
| 4 global `StructuredBuffer`s (331K entries) | Per-chunk vertex buffers + shared texture splatmap |
| `SphereTilePosition` delegate (flat or cylindrical) | `ITerrainProjection.Project(float x, float y, float h)` interface |
| `SphereTileScale` encodes height in Y component | Height is vertex position; scale is uniform |

The `SphereManager` facade remains for backward compatibility. Existing
callers that index by `[x, y]` get a `SphereTile`-compatible view that
delegates to the underlying chunk. The `DrawTiles(CameraX)` path
iterates visible chunks instead of visible rows.

#### A2. GPU Tessellation for Smooth Slopes

Add an optional hull/domain shader stage to the terrain material that
tessellates chunk mesh edges based on camera distance. Close-up tiles get
4-16x edge subdivision; distant tiles render at base resolution. This
replaces `MountainSlopeSurface`'s CPU-side subdivision entirely.

Tessellation factors are driven by:
- Edge length in screen space (adaptive, camera-distance-aware)
- Height delta across the edge (more tessellation at cliff transitions)
- A global quality slider exposed via `SavedSettings.TerrainTessellation`
  (0 = off, 1-4 = quality levels)

**Shader requirements:** Unity 2022.3 supports hull/domain stages on
DX11/DX12/Vulkan/Metal. The existing CompoundSpheres shader is a
vertex/fragment-only instanced shader; the tessellation stages are
additive. A `#pragma hull` / `#pragma domain` block is added with a
`[UNITY_domain("tri")]` attribute on the domain shader.

#### A3. Texture Splatting for Cross-Biome Blending

Replace the current `Texture2DArray` + per-tile `TextureIndex` (integer,
no blending) with a splatmap-based texture blend:

- Each chunk stores a 4-channel splatmap (`RGBAFloat`, one texel per
  tile vertex) where each channel is the blend weight for one of up to
  4 biome textures active in that chunk.
- The fragment shader samples all 4 biome textures and blends by
  splatmap weights, producing smooth grass-to-sand-to-snow transitions.
- Chunks that need more than 4 biomes use a second splatmap pass (rare
  in practice — WorldBox maps typically have 3-4 biomes per 32x32
  region).
- Splatmap updates are per-chunk and only dirty when a tile's biome
  changes, vs the current global `RefreshTextures()` that touches
  331K entries.

#### A4. Chunk-Based LOD

Each `TerrainChunk` maintains 3 LOD meshes:

| LOD | Vertex density | Distance threshold | Use case |
|---|---|---|---|
| 0 (full) | 33x33 (1 vert per tile + 1) | 0 - 100 units | Close-up |
| 1 (half) | 17x17 (every other tile) | 100 - 400 units | Mid-range |
| 2 (quarter) | 9x9 (every 4th tile) | 400+ units | Strategic zoom |

LOD selection happens per-chunk per-frame based on camera-to-chunk-center
distance. LOD transitions use geomorphing (vertex shader blends between
LOD levels over a configurable distance band) to avoid popping.

This replaces the current row-granularity rendering where every tile
draws at full resolution regardless of distance, and addresses the
"No LOD for sphere tiles" gap identified in ADR-0015.

#### Estimated Effort: L (2-4 weeks)

- Week 1: `TerrainChunk` mesh generation, `ITerrainProjection`, chunk
  manager replacing `SphereRow`-based drawing. `MountainSlopeSurface`
  deletion.
- Week 2: Splatmap texture blending, tessellation shader, LOD mesh
  generation and selection.
- Week 3: Cylindrical projection support (the hard part — chunk meshes
  must wrap correctly at the X seam), frustum culling per chunk.
- Week 4: Performance tuning, chunk streaming (lazy init from ADR-0015
  future work), SavedSettings integration, testing.

### Track B: Phenotype Complement DLLs

Following the Phenotype hexagonal polyrepo pattern, extract reusable
terrain/environment subsystems into standalone DLLs that ship as NuGet
packages consumed by WSM3D and sibling projects. Each DLL targets
`netstandard2.0` (Unity-compatible) and has zero Unity dependencies in
its core — Unity adapter types live in a separate `.Unity` assembly.

#### B1. phenotype-terrain

**Repo:** `Phenotype-org/phenotype-terrain`
**Consumes:** nothing (leaf dependency)
**Provides:**

- `HeightField<T>` — generic 2D height-field with bilinear/bicubic
  sampling, gradient computation, normal generation. Parameterized on
  float precision (float32/float64).
- `ChunkMeshBuilder` — generates indexed triangle mesh from a height-field
  region. Supports configurable vertex density (LOD), edge stitching
  between chunks at different LODs, and optional skirt generation for
  gap-hiding.
- `MarchingCubes` — isosurface extraction for cave/overhang geometry.
  Not needed for Phase 1 terrain but positions the library for Civis
  underground features.
- `SplatmapBuilder` — generates blend-weight textures from categorical
  tile data (biome IDs -> continuous weights via distance-field
  smoothing).
- `TerrainLod` — LOD level selection and geomorph factor computation
  given camera distance and configurable thresholds.

**WSM3D integration:** `Compound-Spheres-3D` depends on
`phenotype-terrain` for mesh generation. The `TerrainChunk` class calls
`ChunkMeshBuilder.Build(heightField, region, lod)` and uploads the
result to a Unity `Mesh`.

#### B2. phenotype-water

**Repo:** `Phenotype-org/phenotype-water`
**Consumes:** `phenotype-terrain` (for shoreline height queries)
**Provides:**

- `GerstnerWaveBank` — parameterized multi-wave Gerstner wave superposition.
  CPU-side vertex displacement for mesh water; also emits shader-compatible
  uniform arrays for GPU-side displacement.
- `FluidSimSolver` — shallow-water equation solver on a regular grid for
  dynamic water (rivers, flooding). Fixed-timestep, deterministic.
- `FoamGenerator` — foam mask generation from wave steepness and shoreline
  proximity. Outputs a per-vertex or per-texel foam factor.
- `CausticsProjector` — caustic pattern generation via dual-paraboloid
  refraction. Outputs a caustic intensity texture.

**WSM3D integration:** Replaces the current `WaterRender` path (which
uses CompoundSpheres instancing for water tiles) with a dedicated water
mesh. The water chunk meshes are generated by `phenotype-water` +
`phenotype-terrain`'s `ChunkMeshBuilder` at water-surface height, with
Gerstner displacement applied in the vertex shader.

#### B3. phenotype-voxel (existing, extend)

**Repo:** `Phenotype-org/phenotype-voxel` (already bootstrapped at
`C:/Users/koosh/Dev/phenotype-voxel`)
**Extension:** Add disk cache layer.

The existing `phenotype-voxel` provides SVO + dense leaf chunks with a
deterministic dirty queue and per-engine `Mesher` trait. For WSM3D's
actor/building voxel meshes (Phase 1-2), the hot path is
`VoxelMeshCache.Get(sprite)` which voxelizes sprites at runtime.

**New: SQLite-backed disk cache.** Voxelized meshes are deterministic
given the input sprite hash. A SQLite database at
`{ModConfigPath}/voxel_cache.db` stores serialized mesh data keyed by
sprite content hash (SHA-256). On cache hit, mesh deserialization is
~100x faster than re-voxelization. Schema:

```sql
CREATE TABLE mesh_cache (
    sprite_hash BLOB PRIMARY KEY,   -- 32-byte SHA-256
    mesh_data   BLOB NOT NULL,      -- MessagePack-serialized vertices/indices/normals
    voxel_dims  TEXT NOT NULL,       -- "WxHxD" for cache invalidation on resolution change
    created_at  INTEGER NOT NULL,    -- Unix timestamp
    access_at   INTEGER NOT NULL     -- LRU eviction
);
CREATE INDEX idx_access ON mesh_cache(access_at);
```

LRU eviction at configurable max cache size (default 256MB). Cache is
per-`VoxelScaleMultiplier` setting — changing the multiplier invalidates
via `voxel_dims` mismatch.

**WSM3D integration:** `VoxelMeshCache.Get(sprite)` checks
`phenotype-voxel`'s disk cache before CPU voxelization. Cache writes
happen asynchronously on a background thread.

#### DLL Delivery

All three complement DLLs ship as:
1. **NuGet packages** on the Phenotype org feed for `dotnet restore`
   consumption by CI and local builds.
2. **Pre-built DLLs** in `WorldSphereMod/Assemblies/` alongside the
   existing `CompoundSpheres.dll` for NML's Roslyn compilation (NML
   cannot restore NuGet packages; it needs raw DLLs on the reference
   path).
3. **Git submodule** references in `External/` for source-level debugging
   during development.

The `Directory.Build.props` pattern already used for `$(WorldBoxPath)`
extends to `$(PhenotypePackagesPath)` for local-override builds.

## Migration Path

The migration from CompoundSpheres flat-quad rendering to height-field
terrain proceeds incrementally without breaking the existing 3D pipeline.

### Phase M0: Dual-Path Gate (1-2 days)

Add `SavedSettings.UseHeightFieldTerrain` (default `false`). When false,
the existing `SphereManager` + flat quads + `MountainSlopeSurface`
overlay renders as today. When true, the new `TerrainChunkManager` path
runs instead.

Both paths consume the same `WorldTile` height/biome data via the same
`Core.Sphere` accessors. The gate is in `Sphere.DrawTiles()`:

```
if (UseHeightFieldTerrain && ChunkManager != null && ChunkManager.IsReady)
    ChunkManager.DrawVisibleChunks(camera);
else if (Manager != null && Manager.IsReady)
    Manager.DrawTiles(CameraX);
```

### Phase M1: Flat-Projection Height-Field (1 week)

Implement `TerrainChunkManager` for flat map mode only (no cylindrical
wrapping). Each 32x32 chunk generates a height-field mesh via
`phenotype-terrain`'s `ChunkMeshBuilder`. Colors are vertex colors
sampled from `Core.Sphere.GetColor()`. No texture splatting yet.

Acceptance criteria:
- Flat map renders continuous terrain mesh with smooth height transitions
- No `MountainSlopeSurface` overlay needed (it is suppressed when
  `UseHeightFieldTerrain` is true)
- Frame rate is equal or better than current flat-quad path at equivalent
  zoom levels
- Terraforming tools trigger per-chunk dirty rebuild (not full-map)

### Phase M2: Cylindrical Projection (1 week)

Extend `ITerrainProjection` with `CylindricalProjection` that maps chunk
vertex positions onto the cylinder surface using the same math as
`CompoundSphereScripts.CartesianToCylindrical`. The X-seam wrapping
requires stitching the last chunk column to the first — handled by
`ChunkMeshBuilder`'s edge-stitching support from `phenotype-terrain`.

Acceptance criteria:
- Cylindrical map renders continuous terrain mesh wrapping around the
  cylinder
- No seam artifacts at the X-wrap boundary
- Camera range culling works per-chunk (replacing per-row)

### Phase M3: Texture Splatting + Tessellation (1 week)

Add splatmap generation and the tessellation shader. This is the visual
quality leap — terrain transitions between biomes become smooth gradients
instead of hard tile edges.

Acceptance criteria:
- Biome transitions blend smoothly across 2-3 tile widths
- Tessellation increases detail at cliff edges without CPU cost
- `SavedSettings.TerrainTessellation` quality slider works (0 disables)
- GPU memory usage for splatmaps is within 50MB for a 461x720 map

### Phase M4: LOD + Streaming + Cleanup (1 week)

Add the 3-level LOD system with geomorphing. Implement chunk streaming
(lazy init for chunks outside the initial viewport). Delete
`MountainSlopeSurface` and the `TerrainSmoothing.cs` file entirely.
Update `CompoundSphereScripts.cs` to delegate to the new chunk system
where applicable.

Acceptance criteria:
- Distant terrain renders at reduced vertex density without visible
  popping
- Initial terrain load time is under 10 seconds (vs current 25-45s)
  because only viewport-adjacent chunks build immediately
- `MountainSlopeSurface` code is deleted, not just disabled
- All existing 3D pipeline features (actors, buildings, water, effects)
  work unchanged on the new terrain

### Rollback

At any migration phase, setting `UseHeightFieldTerrain = false` reverts
to the proven CompoundSpheres flat-quad path. The old code is not deleted
until Phase M4 is validated in-game. This is the same pattern used for
Phase 1 voxel actors (`SavedSettings.UseVoxelActors`).

## Consequences

### Positive

- Terrain becomes a continuous surface instead of 331K independent quads.
  This eliminates the entire class of "gap between tiles" and "cliff face
  z-fighting" visual bugs.
- `MountainSlopeSurface` (the 670-line band-aid) is deleted. Its CPU-side
  mesh rebuild, double-draw overhead, and seam artifacts go with it.
- Chunk-based LOD reduces vertex count at strategic zoom by 4-16x,
  directly addressing the "no LOD for sphere tiles" gap from ADR-0015.
- Phenotype complement DLLs are reusable across Civis, phenotype-voxel,
  and future Phenotype-org projects. The terrain and water subsystems
  are not WorldBox-specific.
- Disk-cached voxel meshes eliminate the "first load voxelization stall"
  for returning players.

### Negative

- 2-4 weeks of effort before visual parity with the current system (the
  current system, despite being blocky, is functional and shipped).
- Three new external dependencies (phenotype-terrain, phenotype-water,
  phenotype-voxel DLLs) that must be built, versioned, and shipped in
  `Assemblies/`. Increases the NML compilation reference set.
- Tessellation requires DX11+ / Vulkan / Metal. Players on DX9 or
  OpenGL ES fall back to non-tessellated chunk meshes (still an
  improvement over flat quads, just without adaptive subdivision).
- The `SphereManager` API surface is large (custom buffers, per-tile
  color/scale/texture update, row-level draw control). Maintaining
  backward compatibility while migrating to chunks adds adapter
  complexity.

### Neutral

- The CompoundSpheres library (`External/Compound-Spheres/`) is not
  deleted — it remains available for the `UseHeightFieldTerrain = false`
  fallback path and for any non-terrain use cases (e.g., water tile
  rendering during the transition period).
- The existing frustum culling (`FrustumCuller.cs`) transfers to
  chunk-level AABB tests with minimal changes — the `TestAABB` and
  `BoundsFromTileRange` methods are already chunk-granularity-compatible.

## References

- `External/Compound-Spheres/CompoundSpheres/SphereManager.cs` — current
  tile instancing engine (523 lines)
- `External/Compound-Spheres/CompoundSpheres/SphereTile.cs` — per-tile
  data struct (92 lines)
- `External/Compound-Spheres/CompoundSpheres/SphereRow.cs` — per-row
  indirect draw (96 lines)
- `External/Compound-Spheres/CompoundSpheres/FrustumCuller.cs` — chunk
  AABB frustum culling (117 lines)
- `External/Compound-Spheres/CompoundSpheres/BufferUtils.cs` — buffer
  management and chunked upload (147 lines)
- `External/Compound-Spheres/CompoundSpheres/SphereManagerSettings.cs` —
  settings, delegates, custom buffer API (287 lines)
- `WorldSphereMod/Code/CompoundSphereScripts.cs` — WSM3D wrapper delegates
  (119 lines)
- `WorldSphereMod/Code/Terrain/TerrainSmoothing.cs` — bilinear overlay
  band-aid (685 lines)
- `docs/adr/ADR-0015-compound-spheres-performance.md` — async init and
  frustum culling (predecessor ADR)
- `C:/Users/koosh/Dev/phenotype-voxel` — existing phenotype-voxel repo
