# Memory Leak Audit

Scope: `SpriteVoxelizer.Build`, `ImpostorBillboard.BuildQuad`, `CrossedQuadMesher.Build`, `BuildingMeshGen.Generate`, plus `MaterialPropertyBlock`, `Texture2D`, and `AssetBundle.GetObject<T>()`.

## Mesh ownership

- `SpriteVoxelizer.Build` does **not** own lifetime. `VoxelMeshCache.Get` is the owner: it builds on miss, stores the mesh, and evicts via `_pendingDestroy`/`Evict()`; world unload calls `VoxelMeshCache.Clear()` and `DrainPendingDestroy()` destroys queued meshes (`WorldSphereMod/Code/Voxel/VoxelMeshCache.cs:52-83,183-233`, `WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs:20-21`).
- Permanent orphan path: **no obvious path**. `SpriteVoxelizer.Build` returns `null` on unreadable fallback failure, and the cache rejects `null`/0-vertex results (`WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs:100-131`, `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs:76,162`).
- Eviction: meshes are enqueued for later destroy, not leaked (`WorldSphereMod/Code/Voxel/VoxelMeshCache.cs:214-233`).

- `ImpostorBillboard.BuildQuad` is owned by the impostor atlas cache. `GetOrCreate` builds the quad, stores it immediately, and `Evict()` destroys removed meshes (`WorldSphereMod/Code/LOD/ImpostorBillboard.cs:100-115,166-184`).
- Permanent orphan path: **no obvious path** in normal flow; allocation is immediately followed by `_atlas[key] = ...` (`WorldSphereMod/Code/LOD/ImpostorBillboard.cs:112-115`).
- Eviction: `Clear()` destroys cached meshes synchronously, and `Evict()` destroys each removed entry (`WorldSphereMod/Code/LOD/ImpostorBillboard.cs:126-129,166-184`). Note: `Reset()` destroys the shared material singleton, but world unload only calls `Clear()`, not `Reset()`, so that material persists across worlds (`WorldSphereMod/Code/LOD/ImpostorBillboard.cs:30-42,134-138`, `WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs:26`).

- `CrossedQuadMesher.Build` does **not** own lifetime. `CrossedQuadMeshCache.GetOrBuild` stores the mesh, resolves races via `_pendingDestroy`, and `Evict()`/`Clear()` destroy or enqueue removed meshes (`WorldSphereMod/Code/Foliage/CrossedQuadMesher.cs:17-127`, `WorldSphereMod/Code/Foliage/CrossedQuadMeshCache.cs:32-63,77-125`).
- Permanent orphan path: **no obvious path**. The only early returns are empty meshes for null/non-readable sprites; those are still cache-owned if returned (`WorldSphereMod/Code/Foliage/CrossedQuadMesher.cs:19-23,127`).
- Eviction: correct. Removed meshes are queued then drained (`WorldSphereMod/Code/Foliage/CrossedQuadMeshCache.cs:106-125`, `WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs:23`).

- `BuildingMeshGen.Generate` does **not** own lifetime. `ProcGenCache.GetOrGenerate` owns storage, race loss, eviction, and unload cleanup (`WorldSphereMod/Code/ProcGen/BuildingMeshGen.cs:10-73,426-724`, `WorldSphereMod/Code/ProcGen/ProcGenCache.cs:31-137`, `WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs:22`).
- Permanent orphan path: **no obvious path**. `Generate` returns `null` for blank sprites before cache insertion; non-null meshes are cached immediately by `ProcGenCache` (`WorldSphereMod/Code/ProcGen/BuildingMeshGen.cs:48-73`, `WorldSphereMod/Code/ProcGen/ProcGenCache.cs:52-64`).
- Eviction: correct. `Evict()` queues destroyed meshes and `DrainPendingDestroy()` performs the destroy calls (`WorldSphereMod/Code/ProcGen/ProcGenCache.cs:106-137`).

## Other allocation sites

- `MaterialPropertyBlock`: there is **no dedicated pool**. `MeshInstanceBatcher` keeps one block per bucket (`Bucket.Block`) and clears buckets on reset; `HealthBar` and `SanityTestCube` each keep one block per instance/singleton (`WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:39-43,344`, `WorldSphereMod/Code/Worldspace/HealthBar.cs:9,39,53`, `WorldSphereMod/Code/Voxel/SanityTestCube.cs:12,118-123`). This is reuse, not a leak pattern.
- `Texture2D`: only `SpriteVoxelizer.Build` allocates one fallback texture; it is destroyed on success and in the exception path, and the temp `RenderTexture` is released (`WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs:100,121,195,203`). No permanent orphan path found.
- `AssetBundle.GetObject<T>()`: `Core.LoadAssets()` pulls mesh/material objects from the bundle and stores them in static fields, so they stay rooted for process lifetime by design (`WorldSphereMod/Code/Core.cs:323-324,456-462`). I did not find any unload path for those assets.
