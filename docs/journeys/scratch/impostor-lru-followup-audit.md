# Impostor Billboard LRU Follow-up Audit

Scope: `WorldSphereMod/Code/LOD/ImpostorBillboard.cs` after `bd08f04`, plus the call sites in `WorldSphereMod/Code/Voxel/VoxelRender.cs`, `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs`, and `WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs`.

## 1) Tick order vs `GetOrCreate()`

`ImpostorBillboard.Tick()` runs in `VoxelFrameDriver.LateUpdate()` before the later `VoxelRender.Flush()` call ([VoxelRender.cs:548](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Voxel/VoxelRender.cs#L548), [VoxelRender.cs:552](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Voxel/VoxelRender.cs#L552), [VoxelRender.cs:562](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Voxel/VoxelRender.cs#L562)). But the actual `GetOrCreate()` calls are in Harmony postfixes on `ActorManager.precalculateRenderDataParallel` and `BuildingManager.precalculateRenderDataParallel` ([VoxelRender.cs:350](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Voxel/VoxelRender.cs#L350), [VoxelRender.cs:478](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Voxel/VoxelRender.cs#L478), [BuildingProcRender.cs:15](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/ProcGen/BuildingProcRender.cs#L15), [BuildingProcRender.cs:61](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/ProcGen/BuildingProcRender.cs#L61)). So on a given frame, `GetOrCreate()` typically happens **before** the new tick, not after it.

## 2) `_frame` monotonicity

Yes, for normal process lifetime. `_frame` is a `ulong` that only changes via `_frame++` in `Tick()` ([ImpostorBillboard.cs:121](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/ImpostorBillboard.cs#L121)). `Clear()` does not reset it, and world unload only clears the atlas ([ImpostorBillboard.cs:126](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/ImpostorBillboard.cs#L126), [WorldUnloadPatch.cs:26](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs#L26)). The only caveat is theoretical `ulong` wraparound.

## 3) Can eviction destroy in-use meshes?

There is no explicit pinning or refcount. `GetOrCreate()` inserts the new mesh, then `Evict()` destroys the coldest entries purely by `LastFrame` and removes them from the dictionary ([ImpostorBillboard.cs:100](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/ImpostorBillboard.cs#L100), [ImpostorBillboard.cs:166](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/ImpostorBillboard.cs#L166)). So yes, a mesh can be evicted while it is still logically "in use" elsewhere in the frame; Unity’s `Object.Destroy()` is deferred, so it is not an immediate crash path, but the cache does not guarantee safety for live consumers.

## 4) Recreate-after-evict churn

Yes, it churns. If the same sprite is evicted and later requested again, the cache misses, rebuilds a brand-new mesh via `BuildQuad()`, recalculates bounds, stores it, and potentially destroys the old mesh again on the next eviction cycle ([ImpostorBillboard.cs:111](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/ImpostorBillboard.cs#L111), [ImpostorBillboard.cs:142](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/ImpostorBillboard.cs#L142), [ImpostorBillboard.cs:166](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/ImpostorBillboard.cs#L166)). That means both managed allocation churn (`List<Vector3>`, UV fallback array) and Unity object churn (`Mesh` create/destroy), with no reuse after eviction.
