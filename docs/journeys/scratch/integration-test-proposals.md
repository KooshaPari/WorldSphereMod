# Integration Test Proposals

Four integration tests, one per cross-module seam that has regressed in this session.

1. `VoxelRender_Submit_QueuesAndConfiguresMaterial_ThenBatcherFlushesAcrossCameras`
   - Setup: enable Phase 1, create two visible cameras, seed one voxel actor, and force `VoxelRender.Submit` on a worker-thread-style enqueue path followed by the frame driver flush.
   - Assertions: `MeshInstanceBatcher` receives exactly one submission; the submission survives a `LogAllCameras` pass; `ConfigureVoxelMaterial` has set the instancing/shadow/camera-layer state before the batcher draws; no camera-local render path rebinds the voxel material after enqueue.
   - Bug caught: a missing queue handoff or late material configuration would have produced a silent no-op or camera-specific rendering loss.

2. `BuildingProcRender_UsesRegistryRules_AndInvalidatesProcGenCache_OnRuleChange`
   - Setup: register one building asset with `BuildingRulesRegistry`, render it once through `BuildingProcRender`, then change the registry entry and render again without restarting the world.
   - Assertions: the first frame stores a cache entry in `ProcGenCache`; the second frame recomputes the mesh instead of reusing the stale one; the emitted mesh reflects the updated rules, not the original cached geometry.
   - Bug caught: stale procedural buildings after rule edits or asset reloads because the registry update did not invalidate the cached mesh.

3. `To3DTileHeight_LiftApplied_ToAllPhase3ToPhase10TrsSites`
   - Setup: feed a raw 2D tile position with `z = 0` into each of the eight audited TRS sites that now rely on the lift guard, spanning the voxel, procgen, foliage, rig, and worldspace render paths.
   - Assertions: every site produces a TRS translation with lifted Z, not raw ground-plane Z; the cull position and final TRS position agree; no site falls back to terrain depth when the guard should have lifted it.
   - Bug caught: the “cull lifted, TRS still raw” regression that let several meshes sink into the terrain when scale or visibility assumptions changed.

4. `PhaseFlags_AttributeAnchor_PatchManager_Enablement_IsConsistent`
   - Setup: define one dummy Harmony patch class marked with `[Phase(nameof(SavedSettings.SomeFlag))]`, load the assembly through the same `PhaseAttribute -> PhasePatchManager -> Core.Patch()` path, and toggle the flag off then on.
   - Assertions: when the flag is off, the patch is not applied; when turned on, the class is discovered via the `PhaseAttribute` assembly anchor and patched exactly once; repeated toggles do not duplicate or lose the patch set.
   - Bug caught: a phase patch that was present in the assembly but never enabled, or enabled more than once because the attribute scan and patch manager state disagreed.
