# Phase 1b: Drop / Projectile Voxelization Spec

Scope: add voxel rendering for dropped items and projectiles without changing the existing actor/building Phase 1 path. Current plan already says Phase 1 covers actors/items/drops/projectiles (`PLAN.md:56-72`), but the repo only wires actors/buildings today.

## What needs a new Harmony Postfix

1. `Drop.updatePosition` in `WorldSphereMod/Code/General.cs:225-238` needs a Postfix that submits a voxel mesh after the vanilla position/rotation update has run. The existing Prefix is still useful for the 3D transform gate, but it does not draw anything.

2. `QuantumSpriteLibrary.drawProjectiles` in `WorldSphereMod/Code/QuantumSprites.cs:367-379` needs a Postfix on the draw loop, not on `Manager.SetProjectile`. `SetProjectile` is only a helper extension (`QuantumSprites.cs:155-183`), so the voxel submit needs to happen at the game method boundary that actually iterates projectiles.

3. I do not see a separate `Drop.updateRotation` requirement yet. The current rotation logic already lives in the `Drop` patches (`General.cs:240-248`) and the projectile helper (`QuantumSprites.cs:161-172`). If the Postfix can read the final transform state, no extra rotation hook should be needed.

## Render-data surface

There is no actor-style `render_data` array for either path.

- Actor voxelization consumes `ActorManager.render_data` arrays such as `positions`, `scales`, `rotations`, `main_sprites`, and `has_normal_render` (`docs/render-data-fields.md:8-30`, `WorldSphereMod/Code/Voxel/VoxelRender.cs:286-387`).
- Projectile rendering is driven through `GroupSpriteObject` state in `SetProjectile` (`QuantumSprites.cs:155-183`) and the `drawProjectiles` transpiler (`QuantumSprites.cs:367-379`).
- Drop rendering is just `Drop.transform` plus `Drop.current_position` / `_currentHeightZ` in `Drop.updatePosition` (`General.cs:225-238`).

Conclusion: Phase 1b should source sprite, position, scale, and rotation from the object itself. There is no existing projectile/drop `render_data[]` mirror to extend.

## Material behavior

Yes, the voxel material should apply the same way it does for actors.

- `VoxelRender.EnsureMaterial()` owns one shared voxel material and configures the fallback shader path (`WorldSphereMod/Code/Voxel/VoxelRender.cs:63-103`, `120-159`).
- `VoxelMeshCache.Get(sprite)` is the sprite-to-mesh step used by Phase 1 actor voxels (`VoxelRender.cs:376`, `506`), so drop/projectile voxelization should call the same cache and then submit with `VoxelRender.Submit(...)`.
- The cache is keyed by sprite instance ID and returns `null` for empty meshes (`WorldSphereMod/Code/Voxel/VoxelMeshCache.cs:52-97`).

## Thread-safety concerns

No new threading model is needed if the Postfixes stay on the same Unity update/render paths.

- `VoxelMeshCache.Get()` uses a lock around the cache, but its comment says it “always runs on the main thread” and mesh construction touches Unity APIs (`VoxelMeshCache.cs:56-75`).
- `MeshInstanceBatcher.Submit()` can queue cross-thread work, but its fast path assumes the main thread and its buckets are mutated during flush (`WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:88-105`, `122-132`, `344-358`).
- Therefore the safe rule for Phase 1b is: do not move drop/projectile voxel submission into background work or parallel loops; keep it on the same thread as the existing sprite update/draw methods.

## Phase 1b intent

Use the same actor pattern: `sprite -> VoxelMeshCache.Get(sprite) -> lift to 3D -> tier selection -> impostor fallback -> shared voxel material`. The only difference is the data source:

- actors read `render_data[]`
- drops read `Drop` instance state
- projectiles read `GroupSpriteObject` / `Projectile` instance state

