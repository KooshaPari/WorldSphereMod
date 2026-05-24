# FrustumCuller audit

Scope: `WorldSphereMod/Code/LOD/FrustumCuller.cs`

## Findings

1. `UpdatePlanes()` is wired to the active mod camera and refreshed once per frame when there is render work. It pulls `CameraManager.MainCamera`, early-outs on null, and caches by `Time.frameCount`. The frame driver calls it from `LateUpdate()` before flushing render submissions, so the planes used by the culler are the current camera frustum for that frame. I did not find a stale-camera path in the current call graph.  
   References: `WorldSphereMod/Code/LOD/FrustumCuller.cs:11-17`, `WorldSphereMod/Code/Voxel/VoxelRender.cs:537-558`, `WorldSphereMod/Code/3DCamera.cs:83-92`, `WorldSphereMod/Code/3DCamera.cs:125-133`.

2. `IsVisible(worldPos, radius)` does not perform a literal sphere-vs-plane sign test. It builds a cubic `Bounds` centered at `worldPos` with side length `2 * radius`, then calls `GeometryUtility.TestPlanesAABB`. That is a conservative AABB-frustum test, not a sphere test. It is valid as an approximate visibility gate, but the `radius` parameter is acting as a half-size margin, not a geometric sphere radius.  
   References: `WorldSphereMod/Code/LOD/FrustumCuller.cs:20-25`.

3. I did not find shared mutable frustum state being touched from worker threads in the current code path. The only shared state is the static `_planes` / `_frameCached` pair in `FrustumCuller`, and the only call sites are Harmony postfixes that loop serially after `precalculateRenderDataParallel` completes. No direct parallel reads/writes to the culler were found. If a future caller invokes it concurrently, the statics are unsynchronized and would need protection.  
   References: `WorldSphereMod/Code/LOD/FrustumCuller.cs:8-17`, `WorldSphereMod/Code/Voxel/VoxelRender.cs:285-314`, `WorldSphereMod/Code/Voxel/VoxelRender.cs:441-466`, `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs:15-47`.

4. The `2f` vs `3f` split appears intentional but undocumented. Actor voxelization and procedural buildings both use `2f`, while the voxel-building fallback uses `3f`. Given the culler’s conservative AABB approximation, this looks like a tuning difference for object footprint rather than a correctness bug. I would still treat it as a maintenance risk because the meaning of the number is not centralized.  
   References: `WorldSphereMod/Code/Voxel/VoxelRender.cs:313-314`, `WorldSphereMod/Code/Voxel/VoxelRender.cs:465-466`, `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs:47-49`.
