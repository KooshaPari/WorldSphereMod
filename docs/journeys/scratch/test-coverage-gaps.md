# WSM3D test coverage gaps

Unit coverage in `tests/WorldSphereMod.Tests.Unit/*.cs` is currently focused on source-text or reflection invariants only: `SavedSettingsTests.cs:7`, `PublicApiSurfaceTests.cs:7`, `LocaleKeyCoverageTests.cs:10`, `InstallScriptInvariantsTests.cs:7`, and `DelegateBindingTests.cs:10`. I found no unit test that directly exercises the newly landed render, cache, or overlay branches below.

## Coverage map

- `WorldSphereMod/Code/Voxel/VoxelRender.cs:289` has no matching unit coverage for `ActorVoxelEmit` or `BuildingVoxelEmit`.
- `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs:19` has no matching unit coverage for `ProcMeshEmit.EmitMeshes`.
- `WorldSphereMod/Code/ProcGen/BuildingRules.cs:111` has no matching unit coverage for `BuildingRulesRegistry.Resolve` or `AutoRouteFactory`.
- `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs:45` has no matching unit coverage for `Get`, hit/miss accounting, eviction, or `Clear`.
- `WorldSphereMod/Code/LOD/ImpostorBillboard.cs:91` has no matching unit coverage for `GetOrCreate`, hit/miss accounting, or `Clear`.
- `WorldSphereMod/Code/Worldspace/RuntimeStatsOverlay.cs:83` has no matching unit coverage for the profiler overlay readout.

## Top 5 untested paths

1. `BuildingProcRender.EmitMeshes` regular branch for non-impostor buildings: the `Resolve` -> shape branch -> `ProcGenCache.GetOrGenerate` / `CrossedQuadMeshCache.GetOrBuild` path decides whether Phase 2 buildings render as foliage, procgen mesh, or nothing, and whether `rd.scales[i]` is zeroed after submit. Risk is highest because this is the main Phase 2 production path. `BuildingProcRender.cs:93`
2. `VoxelRender.ActorVoxelEmit` non-impostor branch: terrain lift, voxel submission, and `has_normal_render[i] = false` suppression all happen here. A regression would put actors back on z=0 or leave both sprite and voxel visible. `VoxelRender.cs:376`
3. `VoxelRender.BuildingVoxelEmit` non-impostor branch: same terrain-lift and suppression failure mode, but for buildings. This path also depends on `VoxelMeshCache.Get` and `rd.scales[i] = Vector3.zero`. `VoxelRender.cs:506`
4. `BuildingRulesRegistry.Resolve` / `AutoRouteFactory`: prefix routing now memoizes into `_rules` via `GetOrAdd`. A bug here would misclassify `tree_`/`rock_` assets or reintroduce per-frame allocations. `BuildingRules.cs:119`
5. `VoxelMeshCache.Get` and `ImpostorBillboard.GetOrCreate`: the new hit/miss counters and `Clear()` reset are untested, so cache behavior can regress silently while the overlay still shows plausible numbers. `VoxelMeshCache.cs:46`; `ImpostorBillboard.cs:91`

Recommended next tests: one branch-focused unit for Phase 2 building routing, one for actor/building z-lift suppression, and one cache-behavior test that asserts hit/miss and clear semantics.
