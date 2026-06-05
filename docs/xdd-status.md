# xDD rigor status

This document records the current state of the test suite as of the current branch before any new behavior work lands.

## Last run

- Date: 2026-06-05
- Branch: `wip/208-height-fix`
- HEAD: `582df09` (post-scaffold)
- Toolchain: `dotnet test --collect:"XPlat Code Coverage"` + `reportgenerator` 5.5.1
- Assemblies in scope: `WorldSphereAPI` (only Unity-free surface compile-linked to test projects)
- **Line coverage: 80%** (40 of 50 coverable lines)
- **Branch coverage: 55.5%** (10 of 18)
- **Method coverage: 100%** (16 of 16)
- Full-method coverage: 50% (8 of 16)
- HTML report: `docs/coverage/index.html`

## Current inventory

- Total xUnit tests: `538` (current count after the latest run)
- By tier:
  - `WorldSphereMod.Tests.Unit`: `161` (8 pre-existing failures on phase-default source-shape mismatches)
  - `WorldSphereMod.Tests.Integration`: `69` (1 pre-existing failure)
  - `WorldSphereMod.Tests.E2E`: `308` (16 pre-existing failures)
- Test types present: unit, integration, e2e
- Test types missing: chaos, performance
- Note: the 25 pre-existing failures are unrelated to coverage scaffolding; they assert exact `SavedSettings` / `wsm3d.ps1` defaults that drift as phase flags land. Filed as a separate `fix(xdd): reconcile phase-default source-shape drift` task — see `docs/xdd-status.md` § Open failures.

## What is covered well

- Source-shape and contract checks for the Unity-free surface.
- Repo-shape and CI/workflow invariants.
- Bridge and settings contracts that can be verified without launching WorldBox.
- Selected phase invariants for features that already have a stable RPC or log contract.

## Missing test types

- Chaos tests
  - No systematic kill/restart, race, or mid-transaction fault injection coverage.
  - No adversarial state-reset suite for toggle churn, partial asset availability, or teardown during phase switches.

- Property-based tests (`FsCheck`)
  - No generator-driven invariants for voxel geometry, cache behavior, or settings round-trips.
  - Current coverage is still mostly fixed-fixture and source/invariant driven.

- Load / stress tests
  - No repeatable stress harness for large actor counts, long-running frame stability, or cache churn under sustained load.
  - Perf claims remain mostly one-off measurements and not a recurring gate.

- Mutation tests
  - No `Stryker.NET` or equivalent mutation pass to prove that the assertions actually fail when logic flips.
  - This is the biggest meta-gap because it tells us whether the current suite is sensitive or just present.

## Prioritized plan to reach 85-100% holistic coverage

1. Add a stable coverage gate and trend it in CI.
   - Keep the collector/reporting path working on every test project.
   - Publish the HTML summary so drift is visible.

2. Add property-based tests for the highest-risk pure logic.
   - Focus first on geometry/mesh invariants, cache semantics, and settings serialization.
   - These give the fastest return because they expand input space without requiring the game runtime.

3. Add adversarial parser and state-fault tests.
   - Fuzz `SavedSettings` and bridge payload parsing.
   - Add restart/rollback-style tests for phase toggles and config persistence.

4. Add load and stress coverage around the perf-sensitive paths.
   - Validate that warm caches stay warm.
   - Add repeated-iteration checks around phase toggles and any pure allocator/caching surfaces.

5. Add mutation testing after the suite is less fixture-heavy.
   - Use it to identify assertions that are too weak.
   - Treat failing mutants as a signal to strengthen coverage, not as a release blocker on day one.

6. Extend the runtime-facing harnesses only after the cheaper guards are in place.
   - The current suite is good at contracts and repository shape.
   - It is weak at true adversarial and long-duration validation, which is where the remaining risk sits.

## Honest bottom line

The repo is not near 85-100% holistic coverage yet. It has broad structural coverage and many phase-specific invariants, but the missing categories are the ones that catch the hardest bugs: generated-input edge cases, long-running load behavior, and fault injection.
