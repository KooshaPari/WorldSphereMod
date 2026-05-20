# Highest-Leverage Fix Recommendation

The single most impactful fix is to make `WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs:17-31` the authoritative teardown point for lighting and deferred-destroy queues.

Why this is the best leverage:
- `all-phases-on-audit.md` says the runtime has no explicit cross-driver unload ordering, so late-frame work can strand cleanup.
- `integration-risks-top5.md` ranks missing unload teardown for lighting as #2, stranded deferred-destroy queues as #1, and the rig cache pending-destroy bug as #5.
- Those are all the same class of failure: unload happens, but teardown is split across code paths that are not all reached.

Proposed delta:
- Add `WorldSphereMod.Lighting.SunDriver.Teardown();` to the unload sink.
- Drain queue-backed caches explicitly during unload instead of waiting for the next `VoxelRender.LateUpdate()`:
  - Call `WorldSphereMod.Rig.RigCache.DrainPendingDestroy()` before `RigCache.Clear()` because `RigCache.Clear()` currently drops `_pendingDestroy`.
  - Call `WorldSphereMod.ProcGen.ProcGenCache.DrainPendingDestroy()` after `ProcGenCache.Clear()`.
  - Call `WorldSphereMod.Foliage.CrossedQuadMeshCache.DrainPendingDestroy()` after `CrossedQuadMeshCache.Clear()`.
- Keep the existing try/catch wrappers so one teardown failure cannot block the rest.

This one patch closes the cross-world lighting bleed, eliminates the most likely unload-time mesh leaks, and resolves the biggest shared teardown gap across the audits. It does not fully solve live toggle reapplication for `HighShadows` or `DayNightCycle`, but it unblocks the larger unload/reload stability problems first.
