# Contributor onboarding gaps: Phase 11 weather

The current onboarding docs are strong for the existing fork, but they stop at a Phase 0-10 mental model. A new contributor trying to add a hypothetical Phase 11 weather system would learn the repo’s conventions, but not the actual path to ship a new phase.

## What the docs already tell them

- `CLAUDE.md` says this fork is organized as **10 phases**, that new phases ship behind `SavedSettings` flags defaulting off, and that contributors should start with `docs/HANDOFF.md` and `docs/PLAN.md` ([CLAUDE.md:11-14](../../CLAUDE.md#L11), [CLAUDE.md:18-21](../../CLAUDE.md#L18), [CLAUDE.md:30-36](../../CLAUDE.md#L30)).
- `docs/HANDOFF.md` gives a current-state snapshot, a phase table that runs through **Phase 10**, and a “where to look” map for the existing feature areas ([docs/HANDOFF.md:47-63](../HANDOFF.md#L47), [docs/HANDOFF.md:168-195](../HANDOFF.md#L168)).
- `docs/PLAN.md` is only a pointer; the canonical plan lives at repo root. The root `PLAN.md` is a detailed Phase 0-10 implementation plan with file locations and verification notes ([docs/PLAN.md:1-9](../PLAN.md#L1), [PLAN.md:42-275](../../PLAN.md#L42)).

## Gaps

1. **P0: No Phase 11 entry exists anywhere.** The docs define a complete 10-phase roadmap and stop there, so a weather contributor has no explicit phase boundary, no success criteria, and no “done” definition for the new work ([CLAUDE.md:11-14](../../CLAUDE.md#L11), [docs/HANDOFF.md:47-63](../HANDOFF.md#L47), [PLAN.md:42-196](../../PLAN.md#L42)).

2. **P0: No weather ownership map.** The “where to look” tables cover Voxel, ProcGen, Foliage, Water, Lighting, Rig, Worldspace, Fx, LOD, and Perf, but nothing tells a new dev whether weather belongs in a new `Code/Weather/` area, in `Code/Lighting/`, or split across both. That forces guesswork on file placement and patch boundaries ([docs/HANDOFF.md:168-195](../HANDOFF.md#L168), [CLAUDE.md:86-97](../../CLAUDE.md#L86)).

3. **P1: No weather-specific architecture or integration contract.** The existing plan documents patterns for sun, fog, sky, and time-of-day, but there is no note explaining how a weather system should interact with those systems, what state it owns, what it reads from `SavedSettings`, or how it should avoid conflicting with Phase 8 / Phase 5 behavior ([PLAN.md:168-176](../../PLAN.md#L168), [PLAN.md:122-133](../../PLAN.md#L122), [CLAUDE.md:112-133](../../CLAUDE.md#L112)).

4. **P1: No validation rubric for a new phase.** The docs describe build/install commands and some phase-specific smoke tests, but a new phase needs its own acceptance checklist, reference scenes, and screenshot/metric expectations. Without that, a weather contributor cannot tell whether “working” means visual precipitation, gameplay impact, performance budget, or save/load persistence ([docs/HANDOFF.md:82-116](../HANDOFF.md#L82), [PLAN.md:248-261](../../PLAN.md#L248)).

5. **P2: No contributor checklist for adding a new phase.** `CLAUDE.md` explains how to finish an existing phase, but not how to introduce a brand-new one: add a new flag, update the phase tables, add docs, wire the install/verify flow, and decide whether the phase is opt-in or default-on after smoke tests ([CLAUDE.md:135-142](../../CLAUDE.md#L135), [docs/HANDOFF.md:190-195](../HANDOFF.md#L190)).

## Prioritized fixes

1. Add a **Phase 11 weather section** to the root `PLAN.md` with goals, file ownership, integration points, and verify steps.
2. Add a matching **Phase 11 row** to `docs/HANDOFF.md` plus a short “weather next steps” list.
3. Create `docs/phase11-architecture.md` covering weather state, rendering hooks, save/load, and interactions with sun/sky/fog/time-of-day.
4. Add a **weather smoke-test checklist** with one or more reference scenes and explicit performance/visual expectations.
5. Add a short **“how to add a new phase”** checklist to `CLAUDE.md` so future contributors do not have to infer the release workflow from old phases.
