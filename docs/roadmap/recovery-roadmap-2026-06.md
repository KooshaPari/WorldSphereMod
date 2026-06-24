# WorldSphereMod 3D Recovery Roadmap (June 2026)

Date: 2026-06-18

## Objective
Recover the practical 3D conversion state and ship a stable rendering ladder from the point where work regressed in early June 2026.

## Current state (observed)
- Branching status is mixed; root docs and branch consolidation show work split across `feat/render-foundation-builtin` and `feat/phase-7-ui-kickoff`.
- The mod code now has broad phase scaffolding and settings, but runtime verification for many conversion phases is not yet complete.
- Known hard evidence indicates `VoxelEntities` and `ACESTonemapping` are currently clear in constructor defaults, while `MeshWater` is explicitly forced off by `ApplyPhaseDefaults`.
- Current testing artifacts are mostly source-shape tests and route checks rather than in-engine proof, so shipped status does not imply visible gameplay confidence.
- Known hazards remain: stale caches, source-only test drift, and shader/substrate coupling.
- Runtime startup and exception-baseline checks are currently blocked pending a live launch environment.
- Static phase-default drift has been captured in `WSM-R1-phase-flags.txt`; this is a source snapshot only and not runtime proof.
- WSM-R1 batch notes separate the pass into blocked runtime gates and completed static-snapshot work; use those labels consistently in follow-on artifacts.

## Consolidated blocker state (2026-06-20)
- WSM-R1 remains `in_progress`; do not close it until live startup smoke and Player.log exception baseline artifacts are captured from an actual WSM runtime session.
- Static phase flag snapshot is complete only for source defaults:
  - `MeshWater` constructor default is `true`.
  - `ApplyPhaseDefaults` resets `MeshWater` to `false`.
  - Many later phase flags remain `false`/static OFF by design until phase proof exists.
  - `ACESTonemapping` inherits constructor default `true` because it is not explicitly reset by `ApplyPhaseDefaults`.
- Live evidence still missing:
  - WSM startup smoke execution.
  - Player.log exception baseline and trend count.
  - Screenshot proof/hash for launch baseline.
  - WSM-R2 phase-0 proof packet.
- WSM-R2 prep artifacts exist as templates and ownership maps only; they are not proof that phase-0 ran.
- The next execution order is:
  1. Reattach a live WSM runtime session.
  2. Complete WSM-R1 startup smoke, Player.log exception baseline, and screenshot baseline.
  3. Close WSM-R1 only if DoD evidence is attached and blocked placeholders are replaced by live artifacts.
  4. Run WSM-R2 phase-0 baseline proof using the R2 evidence template.
  5. Advance the phase ladder one phase per proof packet, leaving unproven defaults OFF.

## Regressions to explicitly acknowledge
1. After 2026-06-01, implementation moved faster than verification in places, so status reports started reflecting intent over runtime proof.
2. Stale sources and stale cache states were allowed to influence confidence signals.
3. Telemetry and compile checks were over-relied on for proof where frame-level evidence was required.
4. Phase defaults changed faster than compatibility and smoke checkpoints were stabilized.
5. Substrate alignment lagged upstream patterns, which increased merge risk and reduced predictability.
6. Live-proof language blurred source/static readiness with installed-game runtime behavior; this pass restores that boundary.

## Completed static gates
- Branch/cache/source snapshot artifacts exist for WSM-R1 and are valid only as source/static evidence.
- Phase-default snapshot is complete as a source read: `MeshWater` starts `true` in the constructor but is reset to `false` by `ApplyPhaseDefaults`; later phase flags stay OFF until proof exists.
- WSM-R2 evidence template, blocker map, and proof directory schema are planning artifacts ready for the live owner.
- R1 runbook prerequisites now distinguish build/install commands, bridge health probe, Player.log copy, screenshot/pixel checks, and optional non-live parser/test checks.
- Prior L1 report context is documented as non-current: build succeeded with warnings, tests were non-green, bridge timeout was mixed, and `HEIGHTFIELD_BLOCKING` plus `BRIDGE_FLAKY`, `FALLBACK_PATH`, `NO_INSTANCING`, and `UI_SCREENSHOT_FAIL` remain historical retry context.

## Current live blockers
- `/health` refused on `127.0.0.1:8766`; this is a bridge/listener blocker artifact, not proof that WSM started correctly or incorrectly.
- `Player.log` proof is missing for the current run; any stale 2026-06-18 log must not close R1.
- Current-session screenshot/hash proof is missing; stale or wrong-window screenshots are rejected.
- WSM-R2 remains `blocked_live_runtime` until R1 has current startup smoke, fresh Player.log, screenshot/hash, and result classification.
- `HEIGHTFIELD_BLOCKING` and bridge flakiness from prior reports must be retested or explicitly carried as accepted risks before phase-ladder advancement.

## Recovery roadmap
### R0: State reset and evidence lock (2 days)
- Lock a single working branch and document one canonical trunk in `docs/HANDOFF.md` and `docs/MERGE_CHECKLIST.md`.
- Archive unrelated local session threads and stale stashes; keep only one active consolidation lane.
- Install latest clean build from canonical sources and rerun a fresh baseline:
  - `dotnet build WorldSphereMod.csproj -c Release`
  - `./Tools/install.ps1`
  - baseline `Player.log` collection and hash capture for world launch.
- Add an explicit exception baseline from one clean launch to compare all future runs.

### R1: Honest gates before phase flips
- Make coverage and reporting honest in every run:
  - either install a data collector (coverlet path) or publish explicit "no coverage in this lane" tags.
- Fix all source-shape failures that materially gate default behavior.
- Reconcile Harmony patch contradictions first, before feature expansion.
- Enable one compile lane for a single mod build against installed game layout only; keep this as CI-equivalent signal.
- Treat blocked runtime checks as blocked until a live launch exists; do not convert static snapshots into runtime claims.
- R1 cannot be marked complete from the phase-default snapshot alone; live smoke and Player.log evidence remain required.

### R2: Phased visual ladder (7–10 days)
- Flip and verify one conversion phase per branch:
  1. Voxel Entities base + actor render behavior
  2. Buildings and impostor + LOD path with no exception storm
  3. Water and terrain integration
  4. Lighting/day-night pass and shadows
  5. Worldspace UI + selection layer
  6. PostFX bundle and particles
- For each phase define pass/fail with both:
  - non-visual checks: build, exceptions, phase-state logs
  - visual checks: deterministic camera/terrain/world snapshots
- Keep all disabled phase defaults as OFF until phase smoke passes.
- Phase-0 baseline is blocked until a live runtime session can produce startup, Player.log, and screenshot artifacts.

### R3: Substrate ownership hardening (1–2 weeks)
- Complete upstream-owned CompoundSpheres dependency handling.
- Remove duplicate or bypassed voxelizers and reconcile shader load path precedence.
- Introduce deterministic shader fallback policy and telemetry markers where silent fallback currently hides faults.
- Resolve phase flags and defaults so users can rely on one source of truth.

### R4: Stability and readiness (1–2 weeks)
- Add regression gates for:
  - per-frame exception count
  - world load and reload stability
  - scene transition and camera capture consistency
  - mesh actor fallback and impostor switching
- Close the visible loop by running at least one smoke scenario for each phase through installed game path.
- Merge into `main` only after both:
  - consolidated changelog updates
  - proof artifacts attached to release notes.

## Finish criteria
- No known critical blockers blocking phase progression.
- Phase-by-phase defaults only enabled after live proof, not docs-only assumptions.
- No stale-cache or stale-process assumptions in the verify gate.
- Every merged phase has:
  - build success
- no repeated exception storm
- one world launch smoke artifact.

## Execution checklist (one-owner handoff)

### R0 evidence lock
- Owner: Orchestrator
- DoD:
  - single branch selected and pinned in `docs/HANDOFF.md`
- DoD:
  - stashes and old lane artifacts archived
- DoD:
  - fresh build + install run recorded

### R1 truth gates
- Owner: qa-engineer
- DoD:
  - source-shape failures reduced to intentional scope only
- DoD:
  - coverage strategy set (collector or explicit "no coverage" evidence)
- DoD:
  - Harmony contradiction resolved and documented
- DoD:
  - static phase-default snapshot recorded without implying runtime proof

### R2 phase ladder
- Owner: runtime-specialist
- DoD:
  - each phase has one proof ticket and one launch artifact
- DoD:
  - each phase keeps default OFF until proof complete
- DoD:
  - exceptions per frame under agreed threshold in smoke run

### R3 substrate work
- Owner: architecture
- DoD:
  - shader fallback and load order policy documented and enforced
- DoD:
  - duplicate renderer paths removed or quarantined
- DoD:
  - no silent silent-fail fallbacks for critical path

### R4 ship readiness
- Owner: docs-curator
- DoD:
  - changelog + release notes reference roadmap checkpoints
- DoD:
  - one full phase-complete merge PR ready
- DoD:
  - known-issues section reflects remaining runtime blockers only
