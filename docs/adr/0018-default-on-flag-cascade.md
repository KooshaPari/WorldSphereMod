# ADR-0018 — v2.0.0-alpha.10 phase-flag default-on cascade

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-05-21 |
| Deciders | Core mod team |
| Depends On | [ADR-0005](/adr/0005-default-on-flags-per-phase-ship-gate) |

## Context

`SavedSettings` currently gates all runtime feature phases behind boolean toggles. As of the `v2.0.0-alpha.10` milestone, the release goal moved from “phase flags stay safe-by-default until individually validated” to a **controlled cascade** of turning phase defaults on once each phase had passed repeated smoke-test + instrumentation + bridge verification.

This ADR records why that was done and the exact commit provenance for each phase flag flip.

## Decision

For `v2.0.0-alpha.10`, the 10+ phase booleans that represent shipped player-visible work are defaulted to `true` in the same release cohort unless a phase is still explicitly blocked by hardware or visual-risk gates. This is a deliberate departure from the earlier “always-off until local smoke-test” policy in ADR-0005, while preserving that policy for post-ship follow-up toggles (e.g., future phase-hardening gates).

## Flip map: phase flag → commit SHA (alpha.10 cascade)

| Phase flag | Phase | Commit SHA | Evidence / notes |
|---|---|---|---|
| `VoxelEntities` | 1 | `908e846fe7375b94fa2f9528cdec849446323a0a` | `feat(phase-1): default VoxelEntities=true + DebugVoxelOutline=false` |
| `ProceduralBuildings` | 2 | `6674c16365705ab81f8f539cd23d0d1f8858753a` | `config: ProceduralBuildings default true + AutoTest default false` |
| `CrossedQuadFoliage` | 3 | `2b8160a909605c46c016ddf76bcea48408fc1c31` | `phase 3a ship + phase 6 step 3: foliage default ON + real rig mesh` |
| `MeshWater` | 4 | `51fe1f81975031fe74a8868ad66ff5f4ca666e8a` | `phase 4 ship: flip MeshWater default true; README phase table` |
| `HighShadows` | 5 | `390c742fe740f6ea8be9b74c8c86dc3763d3cbd1` | PRD lane marks `HighShadows enabled=true patches=1` (release-on visibility point) |
| `SkeletalAnimation` | 6 | `a13be4b7cb6e03a568e8f1f88fde6a7f7e9ef97f` | `fix(phase-6): SkeletalAnimation default true` |
| `WorldspaceUI` | 7 | `4d6378d37f7933929aea29147294036f7c247fe1` | `phase 7+8+9+10 ship gates + phase 8 step 4 ProceduralSky load` |
| `DayNightCycle` | 8 | `d1d142135cd63c334416924db3abc338b6cefed6` | `wave-20 batch 2: SSAO+DayNight default on + test coverage + perf review` |
| `PostFX` | 9 | `4d6378d37f7933929aea29147294036f7c247fe1` | `phase 7+8+9+10 ship gates` and adjacent `PostFx` diagnostics/fix commits (`c22be5a156d5764...`, `ff5898f532b1ef...`) |
| `ParticleEffects` | 9 | `04c0aeee6c43e5b6c67161eab3bb54a51b12fe61` | `phase 7 step 5 + 8 step 1 + 9 step 4: HP bar + day driver + fx lifecycle` |

## Rationale

1. **Release-level consistency.** Shipping the alpha with phase defaults aligned avoided mixed default behavior where only some players got a “new default world” and others stayed half-migrated.
2. **Operational safety checks already existed.** Each flip was preceded by:
   - phase-specific default toggles in `SavedSettings`,
   - phase patch registration for runtime toggles,
   - bridge/telemetry or visual checkpoints,
   - handoff/readme updates and test surface updates.
3. **Known exceptions are intentional, not accidental.** Non-shipped diagnostics (for example `ProfilerDump`) remain default-off and are not part of this cascade.

## Consequences

- **Positive:** A fresh install at `v2.0.0-alpha.10` now exercises the full phase stack by default.
- **Negative:** New regressions from any one flag can be masked behind combined defaults; mitigation is to keep the in-game phase tab + bridge toggles available and retain one-flag-off bisect workflows.
- **Process follow-up:** The ADR now supersedes prior language in ADR-0005 for this release boundary only; post-alpha hardening still uses the per-phase flip discipline.

## References

- `WorldSphereMod/Code/SavedSettings.cs`
- `docs/HANDOFF.md` (`v2.0.0-alpha.10` milestone)
- `docs/phase*.md`
- `.git/logs/HEAD` (commit provenance used above)