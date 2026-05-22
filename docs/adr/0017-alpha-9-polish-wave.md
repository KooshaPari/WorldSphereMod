# ADR 0017 — Wave-19 outcomes (alpha.9 polish wave)

## Status

Accepted (2026-05-21)

## Context

Wave-19 is the post-release polish set for the alpha.9 branch. It focuses on rendering/perf recovery and long-tail behavior hardening after the alpha.9 milestone.

## Decision

Ship the following Wave-19 changes together as a single polish wave, with settings and schema migration as part of the same release sequence:

1. Tune LOD proxy boundary (`LodSelector.ProxyThreshold`) from `0.025f` to `0.020f`.
2. Restore instancing default path by changing `SavedSettings.ForceFallbackDrawPath` from `true` to `false` and updating draw-path selection logic to prefer `Graphics.DrawMeshInstanced` when valid.
3. Expand `VoxelMeshCache.Get` signature to `Get(Sprite sprite, int depth = -1, bool forceSyncBuild = false)` and use `forceSyncBuild` for building-facing paths (build-sync-first for placeholders).
4. Wire decal emission via `DecalPool.Emit` for effects:
   - explosion scorch decals,
   - blood decals.
5. Fix impostor billboard behavior for far-field actors (rotation/material configuration path) to prevent tri-dot artifacting.
6. Suppress the phase-toggle modal automatically at world load to avoid forced UI pop-in during startup transitions.
7. Migrate saved settings schema from `2.2` to `2.3` with phase-flag resets on schema drift.

## Wave-19 commit SHAs

- `31db096` — `LodSelector.ProxyThreshold` 0.025 → 0.020.
- `3f1048d` — sync-build option in `VoxelMeshCache.Get(...)`, `ForceFallbackDrawPath=false` default, and settings schema migration `2.2 → 2.3`.
- `f45bf44` — `DecalPool.Emit` wiring for decals, modal suppress on world load, and `ImpostorBillboard` far-field fix.

## References

- `WorldSphereMod/Code/LOD/LodSelector.cs`
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs`
- `WorldSphereMod/Code/SavedSettings.cs`
- `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs`
- `WorldSphereMod/Code/Voxel/VoxelRender.cs`
- `WorldSphereMod/Code/Effects.cs`
- `WorldSphereMod/Code/Fx/Environmental.cs`
- `WorldSphereMod/Code/LOD/ImpostorBillboard.cs`
- `WorldSphereMod/Code/WorldSphereTab.cs`
- `WorldSphereMod/Code/Core.cs`
