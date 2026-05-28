# Perf Profile: VoxelRender Hot Paths

Scope: read-only profiling notes for the Harmony postfixes in `WorldSphereMod/Code/Voxel/VoxelRender.cs` and the flush path in `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs`.

## Summary

The most expensive work in this file is the per-entity voxel emission loop, especially the actor and building hooks because they traverse all visible entities every time their upstream render-data pass runs. `MeshInstanceBatcher.Flush` is normally batched with `Graphics.DrawMeshInstanced`; it only falls back to per-instance `Graphics.DrawMesh` when instancing is disabled, rejected, or throws.

## Harmony postfix iteration counts

### Actors: `ActorManager.precalculateRenderDataParallel` postfix

- File: `WorldSphereMod/Code/Voxel/VoxelRender.cs`
- Method: `ActorVoxelEmit.EmitVoxels(ActorManager __instance)`
- Main loop: `for (int i = 0; i < n; i++)`
- Iteration source: `__instance.visible_units.count`
- Effective iteration count per call: `n` visible units
- Notes:
  - Each iteration does visibility culling, LOD selection, sprite lookup, possible rig submission, possible impostor submission, or voxel mesh submission.
  - This is the highest-cost postfix on a per-call basis because it has the largest entity fanout and the most per-item branching.

### Buildings: `BuildingManager.precalculateRenderDataParallel` postfix

- File: `WorldSphereMod/Code/Voxel/VoxelRender.cs`
- Method: `BuildingVoxelEmit.EmitVoxels(BuildingManager __instance)`
- Main loop: `for (int i = 0; i < n; i++)`
- Iteration source: `__instance._visible_buildings_count`
- Effective iteration count per call: `n` visible buildings
- Notes:
  - Each iteration does the same style of culling/LOD work as actors, then either emits an impostor or a voxel mesh.
  - This is usually second only to actors in cost because it also scales linearly with the number of visible buildings.

### Drops: `Drop.updatePosition` postfix

- File: `WorldSphereMod/Code/Voxel/VoxelRender.cs`
- Method: `DropVoxelEmit.EmitVoxel(Drop __instance)`
- Main loop: none
- Effective iteration count per call: `1` drop instance
- Notes:
  - This path is per-drop and does not iterate over a collection.
  - Cost is dominated by single-object checks: material readiness, frustum culling, LOD selection, mesh lookup, and one submit.

### Projectiles: `QuantumSpriteLibrary.drawProjectiles` postfix

- File: `WorldSphereMod/Code/Voxel/VoxelRender.cs`
- Method: `ProjectileVoxelEmit.EmitVoxels(QuantumSpriteAsset pAsset)`
- Main loop: `for (int i = 0; i < list.Count; i++)`
- Iteration source: `World.world.projectiles.list.Count`
- Effective iteration count per call: `list.Count` active projectiles
- Notes:
  - Each iteration performs per-projectile validation, sprite resolution, culling, LOD selection, and then an impostor or voxel submit.
  - This can be expensive when projectile counts spike, but the total fanout is usually lower than actors/buildings.

## Flush path analysis

- File: `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs`
- Method: `MeshInstanceBatcher.Flush(...)`
- Normal path: batched
  - The flush loop groups submissions by `(mesh, material)` and issues `Graphics.DrawMeshInstanced(...)` in chunks of up to `1023` instances.
  - See the `while (offset < total)` loop and the `Graphics.DrawMeshInstanced(...)` call.
- Fallback path: per-instance
  - `DrawFallbackPath(...)` uses `Graphics.DrawMesh(...)` once per instance.
  - This fallback is only used when:
    - `_useFallbackPath` is already forced on,
    - `CanUseInstancedDraw(...)` rejects the material,
    - `Graphics.DrawMeshInstanced(...)` throws, or
    - BRG flush handles the batch before this path runs.

## Practical ranking

Most expensive to least expensive, based on loop fanout and per-item work:

1. Actors
2. Buildings
3. Projectiles
4. Drops

That ranking assumes typical visible counts. If projectile volume spikes, projectiles can temporarily overtake drops by a wide margin, but drops remain the only single-instance postfix here.

