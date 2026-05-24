# ADR 0005 — Default-off flags per phase, flip on ship

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-05-17 |
| Deciders | KooshaPari |

## Context

The fork ships in 10 numbered phases, each adding a new render mode (voxel actors, procgen buildings, mesh water, skeletal animation, etc). Each phase has a `SavedSettings` flag (`VoxelEntities`, `ProceduralBuildings`, `MeshWater`, ...).

In the Phase 0 plumbing commit the flags were all defaulted `= true`. This caused two problems:

1. **Users pulling the dev branch mid-phase saw broken visuals.** A phase whose code was 60% landed would activate by default and produce missing meshes / null-ref errors at runtime.
2. **Smoke-test gating was meaningless.** "Toggle this flag on to test the phase" only works if the flag was off before the test.

## Decision

**Every phase flag defaults to `false` until the phase has passed an in-game smoke test.** The phase's own ship-gate commit flips its specific flag to `true` and updates the README phase-table entry. Phases that depend on external prerequisites (Unity 2022.3 install, WorldBox API exposure) stay default-off even after code-complete, with an explicit note.

## Consequences

- **Positive.** Pulling any commit from any phase gives a working render path — either the phase is on (validated) or it's off (vanilla 2D billboard fallback).
- **Positive.** The README phase table column "default on?" is now load-bearing documentation, not just status.
- **Negative.** First-time users have to find each flag in the in-game settings tab to see new features. Acceptable — the README documents which flags are ON by default.
- **Negative.** A flag flip can hide a subtle regression if it ships as part of a single commit that touches both the flag and the implementation. Mitigation: split the ship-gate commit into "code lands (flag off)" then "flag default flip + README update" so the validation step is separable.

## Current default state (snapshot of `SavedSettings.cs` at this ADR's date)

| Flag | Default | Reason |
|---|---|---|
| `VoxelEntities` | `false` | Awaits Phase 1 smoke test |
| `ProceduralBuildings` | `false` | Awaits Phase 2 smoke test |
| `CrossedQuadFoliage` | `true` | Phase 3 shipped (3a + 3b) |
| `MeshWater` | `true` | Phase 4-lite shipped |
| `HighShadows` | `false` | Phase 5b shader bake pending |
| `SkeletalAnimation` | `false` | Phase 6 cost gate; CPU bind-pose only |
| `WorldspaceUI` | `true` | Phase 7 shipped |
| `DayNightCycle` | `false` | Phase 8 opt-in (subtle, not all users want it) |
| `PostFX` | `false` | Phase 9 opt-in (cost-sensitive) |
| `ParticleEffects` | `true` | Phase 9 burst path shipped |
| `ProfilerDump` | `false` | Diagnostic only |

## References

- `WorldSphereMod/Code/SavedSettings.cs`
- `CHANGELOG.md` — every "Phase N ship gate" entry corresponds to a flag flip
- `AGENTS.md` — agent rules incorporate this as a hard convention
