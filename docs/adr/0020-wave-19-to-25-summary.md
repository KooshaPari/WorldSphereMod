# ADR-0020 — Wave-19 to Wave-25 polish cascade

## Status

Accepted

## Date

2026-05-21

## Authors

WorldSphereMod release-history synthesis

## Context

This ADR records a continuous polish branch across **waves 19–25** in the `release-`history train where most work was landed as multi-agent "wave" batches before, during, and shortly after the `alpha.10` → `alpha.14` tagging sequence.

The request-side context for this segment is explicit: “50+ codex agents dispatched, ~15 phase flag defaults flipped, alpha.10→.14 tags cut, perf telemetry added.”

## Decision

Treat waves 19–25 as one cumulative hardening cascade (visual correctness first, then staged gate flips + telemetry hardening), with shipped state snapshots anchored by:

- `v2.0.0-alpha.10` (`4abc8e1`/`d1d1421`)
- `v2.0.0-alpha.11` (`6ceb665`/`2115b48`)
- `v2.0.0-alpha.12` (`655a137`/`ff67168`)
- `v2.0.0-alpha.13` (`ce893a2`/`55acdf7`)
- `v2.0.0-alpha.14` (`aa3d87b`/`339d942`)

## Summary by wave

- **Wave-19**
  - `31db096` — LOD ProxyThreshold 0.025→0.020 + PostFx diagnostic logging.
  - `f45bf44` — Codex landings + DecalPool emit wiring + ImpostorBillboard + modal suppress + `ADR-0016`.
  - `3f1048d` — Continuation batch: force fallback off path flip (`ForceFallbackDrawPath=false`), Voxel cache/build behavior, and migration updates.

- **Wave-20**
  - `e866964` — Declares `6` codex agents in batch; includes `HighShadows` default true and cache tuning.
  - `d1d1421` — `SSAO` + `DayNightCycle` defaults on, test + perf review.

- **Wave-21**
  - `2115b48` — `Phase 3/4/5b/7` defaults on, `HANDOFF`/tag updates for `alpha.10`.

- **Wave-22**
  - `aeddf61` — `alpha.11` tag + SavedSettings test updates + flag tweaks.
  - `ff67168` — `SSGI` default-on + non-blocking mesh sync + LOD math verify.

- **Wave-23**
  - `55acdf7` — Release-note scaffold for `alpha.11` + `InstancingEfficiency` metric plumbing.

- **Wave-24 / wave-25**
  - `339d942` — `WaterRender` shader-resolve diagnostics + `ImpostorBillboard` LRU cap + `BuildingProcRender` + phase-1 perf telemetry log path.
  - `4972bb3` is a later wave-25 partial item on the same stream and does not belong to the `alpha.14` cut ancestry.

## Phase-default changes introduced in this cascade

This cascade flips roughly fifteen defaults across phase and tuning knobs:

- `SavedSettings.ForceFallbackDrawPath = false` (`3f1048d`)
- `SavedSettings.HighShadows = true` (`e866964`)
- `SavedSettings.DayNightCycle = true` (`e866964`)
- `SavedSettings.SSAOEnabled = true` (`e866964`)
- `SavedSettings.BiomeBlending = true` (`2115b48`)
- `SavedSettings.MeshWater = true` (`2115b48`)
- `SavedSettings.WorldspaceHealth3D = true` (`2115b48`)
- `SavedSettings.MountainSlopeSmoothing = true` (`2115b48`)
- `SavedSettings.HdrSkybox = true` (`2115b48`)
- `SavedSettings.ColorGradingLut = true` (`2115b48`)
- `SavedSettings.WorldspaceLabel3D = true` (`2115b48`)
- `SavedSettings.PostFX = true` (`2115b48`)
- `SavedSettings.SSGIEnabled = true` (`ff67168`)
- `SavedSettings.WeatherRain = true` (`ff67168`)
- `SavedSettings.LODScale = 0.5f` (`aeddf61`)

## Perf telemetry additions

- `InstancingEfficiency` metric and runtime counters added to `MeshInstanceBatcher` (`55acdf7`).
- Frame-level instancing telemetry logging in `VoxelRender` (`339d942` + related follow-up behavior in `WorldSphereMod/Code/Voxel` files).
- Water material/shader diagnostics in `WaterRender` for launch-time and lifecycle observability (`339d942`).
- Session-level release note captures 15/15 FRs LANDED and 6/7 NFRs MET in the same period (pre-existing post-bridge health context), reinforcing that this wave train was treated as a polish + verification burst.

## References

- [docs/release-notes/v2.0.0-alpha.11.md](/docs/release-notes/v2.0.0-alpha.11.md)
- [docs/HANDOFF.md](/docs/HANDOFF.md)
- [WorldSphereMod/Code/SavedSettings.cs](/WorldSphereMod/Code/SavedSettings.cs)
- [WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs](/WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs)
- [WorldSphereMod/Code/Voxel/VoxelRender.cs](/WorldSphereMod/Code/Voxel/VoxelRender.cs)
- [WorldSphereMod/Code/Water/WaterRender.cs](/WorldSphereMod/Code/Water/WaterRender.cs)
