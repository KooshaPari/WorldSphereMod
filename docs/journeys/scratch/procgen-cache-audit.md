# ProcGen Cache Audit

## 1) Capacity / eviction
- `ProcGenCache` is capped at `512` entries and keys only on `asset.id` (`WorldSphereMod/Code/ProcGen/ProcGenCache.cs:8-18`, `:31-36`).
- `GetOrGenerate()` treats `null` as non-cacheable, but a fallback `UnitCube()` mesh is cached for missing/unreadable sprites, so broken assets can still occupy cache slots (`WorldSphereMod/Code/ProcGen/BuildingMeshGen.cs:14-24`, `WorldSphereMod/Code/ProcGen/ProcGenCache.cs:48-66`).
- Eviction is not strict LRU; it is the same bottom-decile-by-frame-range policy used elsewhere, based on `_frame` / `LastFrame` (`WorldSphereMod/Code/ProcGen/ProcGenCache.cs:118-139`). `Invalidate()` and `Clear()` only queue `Object.Destroy`; the actual destroy happens later from `VoxelRender` (`WorldSphereMod/Code/ProcGen/ProcGenCache.cs:70-95`, `WorldSphereMod/Code/Voxel/VoxelRender.cs:569-580`).

## 2) Hit/miss observability
- `ProcGenCache` has `Count`, but no `HitCount` / `MissCount` counters (`WorldSphereMod/Code/ProcGen/ProcGenCache.cs:20-29`, `:31-66`).
- The runtime overlay currently reports only `ProcGenCache.Count`, while `VoxelMeshCache` already exposes `HitCount` / `MissCount` and the overlay prints them (`WorldSphereMod/Code/Worldspace/RuntimeStatsOverlay.cs:98-108`, `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs:40-43`).
- Recommendation: add `HitCount` / `MissCount` to `ProcGenCache`, increment on cache hit/miss like `VoxelMeshCache`, reset in `Clear()`, and surface them in `RuntimeStatsOverlay` for tuning.

## 3) Thread safety
- The cache dictionary is lock-protected, so concurrent hits/inserts/evictions do not race on the container (`WorldSphereMod/Code/ProcGen/ProcGenCache.cs:20-29`, `:38-67`, `:73-95`).
- The generation path is not worker-thread-safe by itself: `BuildingMeshGen.Generate()` touches Unity objects (`asset.checkSpritesAreLoaded()`, `sprite.textureRect`, `sprite.texture.width`, `SpriteVoxelizer.GetPixelsCached()`), and `GetPixelsCached()` calls `Texture2D.GetPixels32()` (`WorldSphereMod/Code/ProcGen/BuildingMeshGen.cs:14-33`, `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs:35-40`).
- Repo guidance says `BuildingManager.precalculateRenderDataParallel` runs on a worker pool but the postfix runs after `Parallel.For` exits, so this path is intended to be effectively single-threaded today; if that contract changes, the cache lock still protects the map, but Unity API calls would need marshaling (`docs/HANDOFF.md:150-152`, `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs:19-139`).
- There is one benign race: two threads can both generate the same mesh, and the loser is queued for deferred destruction (`WorldSphereMod/Code/ProcGen/ProcGenCache.cs:54-66`).

## 4) Stress memory
- At the cap, the cache can retain `512` building meshes (`WorldSphereMod/Code/ProcGen/ProcGenCache.cs:8-18`, `:63-66`).
- `BuildingMeshGen.BuildMesh()` does not call `UploadMeshData(true)`, so the mesh keeps its CPU-side data as well as GPU upload state (`WorldSphereMod/Code/ProcGen/BuildingMeshGen.cs:426-472`).
- The generated meshes are fairly small structurally: four walls, optional openings, and one roof primitive; most assets should land in the tens to low hundreds of verts (`WorldSphereMod/Code/ProcGen/BuildingMeshGen.cs:426-472`, `:485-720`).
- Rough stress estimate: about `~5-10 MB` steady-state for 512 cached procgen meshes, with extra transient memory during bursts from duplicate generations and the pending-destroy queue. That is an inference from the mesh shape, not a measured number.
