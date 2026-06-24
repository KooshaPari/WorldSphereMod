# WorldSphereMod 3D Retroactive Changelog (June 2026)

Date: 2026-06-18

## Scope
This changelog is a retroactive reconstruction of work drift that started around 2026-06-01 and became misrepresented by mixed branch states and verification assumptions.

## Added
- Added `WorldSphereMod` consolidation artifacts for phase hardening and merge sequencing:
  - shader fallback consolidation in render foundation branch
  - phase 7 UI salvage branch merge
  - gpu-compute p4 bridge tracing and cache cleanup work
- Added traceability matrix and gap map from phase-default drift to runtime proof gaps.
- Added consolidated state tracking in `.claude` planning notes for post-2026-06-14 branch merges.

## Changed
- Changed public state framing from “default-on phase completion” to “phase presence with partial runtime proof”.
- Shifted trust from speculative screenshots and stale telemetry alone to explicit evidence-first defaults.
- Changed failure interpretation to separate source-shape drift from live rendering failures.

## Fixed
- Fixed status drift where source-shape regressions were listed as runtime-ready.
- Recovered branch consolidation plan and removed many duplicate branch references from the active lane.
- Reintroduced hard checkpoints around shader loading, bridge telemetry behavior, and phase default baselines.

## Regressions and unresolved gaps
- The following remain open despite documentation progress:
  - Exception spikes in runtime loop when validation is incomplete.
- Several source-shape tests still fail on consolidation branch and are known to be representation-level, not always runtime blockers.
- `SafeShaders` runtime path still only shows partial load success in current snapshots.
- F9/F10 phase behavior, water/terrain/mesh post-processing, and full phase ladder proof are not yet fully verified.

## Acceptance notes for next sprint
- Continue with phase-by-phase smoke PRs, each with:
  - local installed build validation
- one proof artifact
- and changelog evidence.

## 2026-06-18 continuation checkpoint
- WSM branch lane: continued with proof-first tracking in WSM-R1. Branch/commit and cache-audit artifacts were confirmed; phase-default drift now has a static snapshot from `SavedSettings.cs` (constructor + `ApplyPhaseDefaults`) recorded in `WSM-R1-phase-flags.txt`.
- Runtime smoke startup, launch exceptions, and screenshot proof are still blocked in this pass due missing game runtime context.
- Dino lane: branch/CI checkpoint artifact was added (`Dino-D1-branch-ci`), with all gameplay and symbol/runtime checkpoints tracked in artifact files but not executed.
- Roadmap action: queue remains active at WSM next-work items 1-10 and Dino runtime checkpoint items 1-5, with the next phase focused on blocking off unresolved live checks before any phase ladder handoff.
