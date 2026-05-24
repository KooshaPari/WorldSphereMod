# Skeletal rig audit

Scope: `WorldSphereMod/Code/Rig/*.cs`, plus the Phase 1 warm-cache path in `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs`

## Findings

1. **RigCache lifecycle is incomplete.** `RigCache` has a frame-stamped LRU (`LastFrame`, `_frame`, `Evict()`), but nothing calls `RigCache.Tick()` or `RigCache.DrainPendingDestroy()` anywhere in the repo. That means `_frame` never advances, `Evict()` hits the `maxFrame == minFrame` early return, and the deferred-destroy queue is never drained. In practice, rig entries will not age out and evicted base meshes can leak until world unload. References: `WorldSphereMod/Code/Rig/RigCache.cs:20-29`, `WorldSphereMod/Code/Rig/RigCache.cs:65-70`, `WorldSphereMod/Code/Rig/RigCache.cs:135-189`, `WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs:20-25`.

2. **Bone updates are per-submit, not per-frame, and currently CPU-gated.** The skeletal pose is rebuilt inside `RigDriver.DispatchSkin()` on every submit via `HumanoidRig.Evaluate(fd, 1f)`, then flattened into `_matricesScratch` and uploaded to `_matricesBuf` before dispatch/readback. There is no separate per-frame rig update loop. Today this path is dormant because `SubmitSkinnedActor()` forces `_gpuOK = false`, so live bone matrix updates do not actually run yet. References: `WorldSphereMod/Code/Rig/RigDriver.cs:29-31`, `WorldSphereMod/Code/Rig/RigDriver.cs:86-93`, `WorldSphereMod/Code/Rig/RigDriver.cs:132-178`, `WorldSphereMod/Code/Rig/HumanoidRig.cs:166-205`.

3. **Skeletal pre-warm is a real future opportunity.** `VoxelMeshCache.WarmCacheAsync()` now budgets voxel mesh builds across frames and drains from `VoxelFrameDriver.LateUpdate()`. `RigCache` has no sibling warm path, so the first skeletal submit still pays the full `BuildHumanoid()` cost synchronously. A future `RigCache.WarmCacheAsync()` could amortize sprite voxelization plus bone assignment ahead of time; if rig types stay humanoid-only, a sprite queue is enough, but once `ResolveRigType()` grows up it should probably key on `(Sprite, RigType)`. References: `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs:92-179`, `WorldSphereMod/Code/Rig/RigCache.cs:77-132`.

4. **No confirmed skeletal-specific cull-lift regression.** The branch still lifts `skPos` before submit, then `RigDriver` uses the passed `pos` directly for `Matrix4x4.TRS()`. `HumanoidRig.Evaluate()` only changes local bone pose; it does not re-read world position. That means the earlier “cull lifted, render raw” bug pattern does not repeat here. The remaining seam risk is shared with other render paths: a single frustum test on the lifted anchor, not a dual X-wrap seam test. References: `WorldSphereMod/Code/Voxel/VoxelRender.cs:308-340`, `WorldSphereMod/Code/Rig/RigDriver.cs:67-129`, `WorldSphereMod/Code/Rig/HumanoidRig.cs:166-205`.

## Bottom line

The hard bug is RigCache lifecycle management. The skeletal pose path is structurally okay, but its matrix upload path is still single-threaded/shared-state code that should be treated as non-reentrant until the GPU path is re-enabled and protected.
