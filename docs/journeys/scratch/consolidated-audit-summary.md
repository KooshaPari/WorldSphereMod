# Consolidated Audit Summary

Cross-audit read of `docs/journeys/scratch/*.md` on 2026-05-20.

## Overlaps And Contradictions

- The raw-position / lift bug class is the dominant overlap across `phase3-cull-lift-audit.md`, `phase3-plus-latent-cull-audit.md`, `foliage-phase3-audit.md`, `worldui-renderer-audit.md`, and `water-render-audit.md`. They agree the bug is real in `VoxelRender.cs` / `BuildingProcRender.cs`, and absent in the other phase folders.
- `day-night-audit.md` and `high-shadows-audit.md` both point at lifecycle timing, not rendering math: startup-only initialization means late toggles do not re-run the phase-owned setup.
- `test-coverage-gaps.md` and `e2e-coverage-gaps.md` overlap heavily: the repo has happy-path checks, but almost no branch, failure, or cache-invalidation coverage.
- `docs-vs-code-drift.md` contradicts `README.md` / `docs/HANDOFF.md`: several phases are documented as landed/default-on or code-complete, but the code is still default-off and gated.
- `memory-leak-audit.md` and `decalpool-audit.md` mostly agree that core mesh/pool ownership is sound; the only remaining leak is the particle voxel-cube mesh in `ParticleEffectLibrary`.

## Top 10 Actionable Items

1. Fix the Phase 3+ raw-position-after-cull/TRS bug in `VoxelRender.cs` and `BuildingProcRender.cs` (`phase3-cull-lift-audit.md`, `phase3-plus-latent-cull-audit.md`). This is the widest correctness issue because it affects actors, buildings, and procgen meshes.
2. Fix `CrossedQuadMeshCache` keying and invalidation so sway-amplitude changes and building-rule edits cannot reuse stale foliage meshes (`foliage-phase3-audit.md`, `procgen-cache-audit.md`).
3. Make `DayNightCycle` self-advance when enabled, and ensure the sky/time objects are created on late toggle rather than only at startup (`day-night-audit.md`).
4. Re-run `ShadowCascadeConfig.Apply()` when `HighShadows` changes, and remove the “static after init” assumption from the lighting path (`day-night-audit.md`, `high-shadows-audit.md`).
5. Destroy `ParticleEffectLibrary.BuildVoxelCubeMesh()` on clear/unload so Phase 9 does not retain a runtime mesh across worlds (`decalpool-audit.md`, `memory-leak-audit.md`).
6. Add `ProcGenCache` hit/miss counters and surface them in `RuntimeStatsOverlay` so cache regressions become visible instead of inferred (`procgen-cache-audit.md`).
7. Add targeted unit/integration tests for `VoxelRender`, `BuildingProcRender`, `VoxelMeshCache`, `ImpostorBillboard`, and phase-toggle behavior (`test-coverage-gaps.md`, `integration-test-proposals.md`).
8. Add explicit failure-case E2E journeys, not just happy paths, especially for phase off/on, missing assets, and cache invalidation (`e2e-coverage-gaps.md`).
9. Update `README.md`, `docs/HANDOFF.md`, and related phase docs so default-off phases are described as opt-in, not “code complete” (`docs-vs-code-drift.md`).
10. Harden the MCP server surface: fix the startup indentation error, and add a real auth/trust boundary for the loopback HTTP tools (`mcp-server-audit.md`, `security-audit.md`).

## Three Fixes That Unblock The Most Other Items

1. A shared lifted-position helper for the Phase 3+ render paths. This unblocks the main correctness fix, the related refactors in `voxelrender-refactor-opportunities.md`, and several test cases.
2. A phase-lifecycle reinit hook for toggleable systems. This unlocks the day/night and high-shadows fixes, and it also makes phase-toggle tests meaningful.
3. A unified cache-invalidation / observability pass across foliage and procgen caches. This covers the stale-mesh bug, the missing metrics, and the perf harness work.

## Three Audits That Ended Up Being Non-Issues

- `batcher-overflow-audit.md`: instancing overflow is split into 1023-instance draws; no truncation bug.
- `frustum-culler-audit.md`: the culler is using a conservative AABB gate, and the `2f` / `3f` split looks intentional, not a correctness fault.
- `water-render-audit.md`: no cull-lift regression and no obvious lifecycle leak in the water path; the main risk is first-enable shader load hitching.

