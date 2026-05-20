# DecalPool / Phase 9 audit

Scope: `WorldSphereMod/Code/Fx/DecalPool.cs`, `WorldSphereMod/Code/Fx/ParticleEffectLibrary.cs`, `WorldSphereMod/Code/Fx/EffectPatches9.cs`, `WorldSphereMod/Code/Voxel/VoxelRender.cs`

Result: mostly sound. I found one concrete unload leak risk in the particle path; the decal pool itself cleans up correctly.

1. Pool capacity and drop-on-overflow behavior are explicit and bounded. `DecalPool` hard-caps Footprint/Scorch/Blood at `32/16/32`, returns immediately when the free queue is empty, and `ParticleEffectLibrary` uses a 16-system pool that returns `false` when no idle system is found. References: `WorldSphereMod/Code/Fx/DecalPool.cs:30-33`, `WorldSphereMod/Code/Fx/DecalPool.cs:55-71`, `WorldSphereMod/Code/Fx/ParticleEffectLibrary.cs:60-63`, `WorldSphereMod/Code/Fx/ParticleEffectLibrary.cs:105-121`.

2. Reclaim on expiry is correct for decals. `Tick()` walks the active lists, disables expired objects, re-enqueues them, and removes the entry; footprint entries intentionally use `float.PositiveInfinity`. Particle systems do not have a frame-driven reclaim path yet, but the pool reuses them lazily on the next `Fire()` via `IsAlive(true)`. References: `WorldSphereMod/Code/Fx/DecalPool.cs:69-93`, `WorldSphereMod/Code/Fx/ParticleEffectLibrary.cs:69-80`, `WorldSphereMod/Code/Fx/ParticleEffectLibrary.cs:177-182`, `WorldSphereMod/Code/Voxel/VoxelRender.cs:596`.

3. World-unload lifecycle for the decal pool is fine. `EffectPatches9.OnFinish()` destroys the Fx root and calls `DecalPool.Clear()`, and `Clear()` destroys each pooled root GameObject. I do not see a persistent decal-projector leak in this path. References: `WorldSphereMod/Code/Fx/EffectPatches9.cs:36-45`, `WorldSphereMod/Code/Fx/DecalPool.cs:95-108`.

4. One leak risk remains in `ParticleEffectLibrary`: `BuildVoxelCubeMesh()` allocates a runtime `Mesh`, but `Clear()` only nulls `_voxelCubeMesh` and never `Destroy()`s it. That makes the mesh survive world unload unless Unity reclaims it indirectly. References: `WorldSphereMod/Code/Fx/ParticleEffectLibrary.cs:225-252`, `WorldSphereMod/Code/Fx/ParticleEffectLibrary.cs:184-195`.

5. I did not find a latent cull-lift pattern in Phase 9. These files never read `render_data.positions[i]`, never call `To3DTileHeight`, and never route through `FrustumCuller`; they are pure emit/reclaim/lifecycle code. References: `WorldSphereMod/Code/Fx/DecalPool.cs:55-93`, `WorldSphereMod/Code/Fx/EffectPatches9.cs:20-45`, `WorldSphereMod/Code/Fx/ParticleEffectLibrary.cs:98-175`.
