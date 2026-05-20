# InstancingBroken Removal Verification

Verified against commit `864d7c3`.

- `WorldSphereMod/Code/Voxel/VoxelRender.cs:195-203` no longer contains a live `InstancingBroken` early-return in `Submit()`. The only mention is a removed-code comment at `:197-200`; `Submit()` now only guards on material resolution and always forwards to `MeshInstanceBatcher.Submit()`.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:350-399` no longer gates the impostor or voxel actor branches behind `!InstancingBroken`. The impostor branch submits the billboard mesh unconditionally once the LOD tier matches, and the voxel branch calls `Submit(m, trs, rd.colors[i])` directly.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:475-523` shows the same pattern for buildings: impostor and voxel paths submit without any `InstancingBroken` check.
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:147-213` still has the intended fallback behavior. `Flush()` uses `_useFallbackPath` to route directly to `DrawFallbackPath()` at `:166-171`, and if `Graphics.DrawMeshInstanced()` throws, it logs the rejection, sets `_useFallbackPath = true`, and falls through to `DrawFallbackPath()` at `:202-214`.
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:228-279` confirms the fallback path renders per-instance via `Graphics.DrawMesh()` and increments `FrameDrawCalls` for each draw.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:337-343` shows the skeletal actor branch still bypasses voxel submission via `RigDriver.SubmitSkinnedActor()`, with no `InstancingBroken` gate present.

Remaining `InstancingBroken` references are diagnostic only:
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:79` exposes the property.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:197-200` contains the removed-code comment.
