# ADR-0001: Hybrid sprite→3D strategy (voxel actors + procgen buildings + crossed-quad foliage)

**Status:** Accepted

**Date:** 2026-01-15

**Author:** WorldSphereMod3D fork lead

**Stakeholders:** Render team, mod authors using `WorldSphereAPI`, downstream players

---

## Context

Upstream `MelvinShwuaner/WorldSphereMod` puts the **terrain** in 3D (via the
vendored `CompoundSpheres.dll`) but leaves every other visible entity as a
2D `SpriteRenderer`/`QuantumSprite` quad positioned in 3D space and rotated
to face the camera. The fork's mandate is to finish the 3D conversion.

Three classes of entities need 3D representations: **actors / items / drops /
projectiles** (1000s on screen, mostly sprite-based), **buildings** (100s on
screen, distinct silhouettes, sprite-based), and **foliage / clouds**
(thousands on screen, simple sprite cards).

A single uniform approach — e.g. "voxelize everything" — is wrong: voxelized
buildings look chunky at scale and at the camera distances WorldBox uses;
voxelizing every leaf would shred the GPU; a hand-authored 3D model pipeline
would require artist work for hundreds of vanilla assets.

### Problem Statement

What rendering technique do we use for each class of entity to finish the 3D
conversion, given the constraints of (a) no per-asset artist work, (b)
preserving WorldBox's pixel-art identity, (c) holding 60 fps with thousands
of entities, (d) sharing one lighting / shadow pipeline?

### Forces

- **No artist budget**: vanilla WorldBox has hundreds of sprite assets; we
  cannot commission 3D models for each.
- **Style preservation**: WorldBox is pixel-art; the fork must not look
  like a generic stylized indie game.
- **Performance ceiling**: target 60 fps with 5000 actors on a mid-range
  GPU (RTX 3060 / 5600X reference rig).
- **Shared lighting stack**: voxel, procgen, and foliage meshes must all
  consume the same directional sun + cascaded shadow maps from Phase 5.
- **Mod compatibility**: external assets registered via `WorldSphereAPI`
  must work in the chosen pipeline without per-asset rework.

### Alternatives Considered

| Alternative | Pros | Cons | Why not chosen |
|---|---|---|---|
| Voxelize everything (actors + buildings + foliage) | Uniform pipeline | Buildings look chunky; foliage cost is N² in voxels | Building silhouettes need straight edges; foliage cost is prohibitive |
| Hand-author 3D models for vanilla | Highest visual quality | Requires artist; non-deterministic for mod assets | Not in scope; doesn't solve mod-author problem |
| Pure crossed-quad billboards for everything | Cheapest | No real volume from any angle on actors; bosses look paper-thin | Fails the "real 3D" goal |
| Per-class hybrid: voxel actors + procgen buildings + crossed-quad foliage | Matches each class's needs | Three pipelines to maintain | **Chosen** — three small pipelines are cheaper than one bad fit |

## Decision

We use a **per-class hybrid** rendering strategy:

1. **Voxel actors / items / drops / projectiles** (Phase 1). Every opaque
   sprite texel becomes a unit cube, greedy-meshed on the X/Y plane with
   per-asset depth. Per-cube color baked into vertex colors. Mesh cache
   keyed by `Sprite.GetInstanceID()` with LRU eviction. Batched via
   `Graphics.DrawMeshInstanced` / `DrawMeshInstancedIndirect`.
2. **Procedural building meshes** (Phase 2). Footprint extruded from the
   sprite silhouette (top-down alpha projection); heuristic roof inference
   from warm-palette cluster detection; doors / windows from dark-rect
   detection. Per-asset cache in `ProcGenCache`. JSON overrides via
   `BuildingRules` for power-user / mod-author control.
3. **Crossed-quad foliage / clouds / decorations** (Phase 3). Two
   perpendicular textured quads per asset, vertex-displacement wind
   shader, soft-particle blending for clouds.

All three pipelines emit `{mesh, matrix, color}` tuples into the same
`MeshInstanceBatcher`, so they share the URP lit shader, shadow cascades,
and SSAO from Phase 5.

External mods can bypass per-class auto-generation via the v2 API
(`RegisterCustomMesh`, `RegisterBuildingRules`, `RegisterRig`).

### Implementation Notes

- Per-class entry points: `Voxel/SpriteVoxelizer.cs`,
  `ProcGen/BuildingMeshGen.cs`, `Foliage/CrossedQuadMesher.cs`.
- Shared queue: `Voxel/MeshInstanceBatcher.cs`.
- Patches into upstream: `QuantumSprites.calculateactordata3D` (Phase 1),
  `BuildingManager.precalculateRenderDataParallel` (Phase 2),
  `WorldTilemap.renderTile` / `drawWallType` (Phase 3).
- Override surface: `WorldSphereAPI.RegisterCustomMesh` /
  `RegisterBuildingRules` (v2, opt-in).

## Consequences

### Positive

- No artist work needed for vanilla assets — all three pipelines auto-
  generate from existing sprites.
- Pixel-art identity preserved via voxel-from-sprite and crossed-quad
  texturing.
- Single lighting / shadow stack consumes all three pipelines.
- Mod authors get a clean override path per class.

### Negative

- Three pipelines to maintain (per-class caches, generators, override
  surfaces).
- Voxel-from-sprite gets the proportions wrong on assets that rely on
  perspective tricks; per-asset depth overrides are needed.
- Building heuristics misclassify roofs on stylized assets; manual JSON
  overrides are sometimes required.
- Foliage crossed-quads do not cast volumetric shadows (acceptable in
  the WorldBox style).

### Neutral

- Each class still has a fallback to the upstream sprite-billboard path
  when its `SavedSettings` flag is OFF — so the fork degrades gracefully
  even before Phase 1 ships.

## References

- [PLAN: Phase 1 — Voxel sprite pipeline](/PLAN#phase-1--voxel-sprite-pipeline-12-weeks)
- [PLAN: Phase 2 — Procedural building meshes](/PLAN#phase-2--procedural-building-meshes-23-weeks)
- [PLAN: Phase 3 — Foliage, clouds, decorations as crossed-quads](/PLAN#phase-3--foliage-clouds-decorations-as-crossed-quads-12-weeks)
- [Phase 2 architecture](/phase2-architecture)
- [Phase 3 architecture](/phase3-architecture)
- Related: [ADR-0004](/adr/0004-rigid-skinning-over-blended) (skinning model for voxel actors)
