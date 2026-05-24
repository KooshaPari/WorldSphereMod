# Top 10 highest-leverage infrastructure investments for WSM3D

Ranked by cross-cutting leverage across the survey set, the perf roadmap, and the buy-vs-build research. I biased toward investments that unlock several downstream fixes instead of single-feature wins.

**Last audit:** 2026-05-20 (initial synthesis, commit `7807279`)  
**Re-audit:** 2026-05-23

## Shipped since last audit

| Item | Status | Advances rank(s) | Evidence |
|------|--------|------------------|----------|
| **CI WorldBox ref stubs** | **Shipped** | 7, 9 | `Tools/ci-stub-worldbox-refs.sh`, `Tools/ci-worldbox-ref-dlls.manifest`; wired in `build.yml`, `test-gate.yml`, `lint-gate.yml`; invariant coverage in `CiWorkflowInvariantsTests` (`tests/WorldSphereMod.Tests.E2E/`). See `docs/ci-mod-compile-gap.md`. |
| **Fuzz / property-style unit tests** | **Shipped** | 3 | `tests/WorldSphereMod.Tests.Unit/Fuzz/` (10 tests): `BridgeRpcJsonFuzzTests`, `SavedSettingsJsonFuzzTests`; Unity-free seams `BridgeSettingParser.cs`, `SavedSettingsJson.cs` compile-linked into the unit project. |
| **Integration test suite** | **Shipped** | 1, 4, 7 | `tests/WorldSphereMod.Tests.Integration/` (38 tests): API surface, `mod.json`/install, journey manifest schema/paths, bake tooling, visual-regression harness contracts. Gated in `test-gate.yml` and `Taskfile.yaml` / `Justfile` `test-integration`. |
| **Bridge hardening + parser fix** | **Shipped (PARTIAL live)** | 2 | 2026-05-23: listener generation guard, `MapBox.renderStuff` + backup queue drain, `VoxelFrameDriver` / `LateUpdate` flush (`BridgeServer.cs`, `BridgePerFrameTick.cs`, `VoxelRender.cs`). Malformed RPC/settings inputs routed through `BridgeSettingParser`. Save-load HTTP reliability still **PARTIAL** — see `bridge-scene-transition-known-issue.md` live checklist. |

**Test gate (2026-05-23):** `dotnet test tests/WorldSphereMod.Tests.Unit/` — 144 passed, 3 skipped; `dotnet test tests/WorldSphereMod.Tests.Integration/` — 38 passed.

---

| Rank | (a) What to do | (b) Survey/research backing | (c) Effort | (d) Risk | (e) Existing gaps closed |
|---|---|---|---|---|---|
| 1 | Stand up a parallel scenario orchestrator with a small declarative DSL, distinct bridge ports, and CI aggregation for multiple WorldBox instances. | `polyglot-architecture-survey.md`, `test-orchestrator-design.md`, `containerized-test-design.md`, `cross-cutting-concerns.md` | L | med | Closes serial single-instance testing, missing aggregate reports, no machine-readable scenario output, and no scalable path to parallel CI. **Partial (2026-05-23):** hosted CI runs unit + integration tiers; journey manifest/schema tests — no multi-instance orchestrator yet. |
| 2 | Define a structured observability contract for bridge/runtime events: request IDs, stable event names, per-request outcomes, and a single metric sink. | `cross-cutting-concerns.md`, `perf-roadmap-2026-05-19.md` | M | low-med | Closes ad hoc logging, missing telemetry fields, no exporter/trace hook, and weak feedback for bridge failures or sustained perf regressions. **Partial (2026-05-23):** bridge drain/generation hardening + fuzzed RPC/settings parsing; structured contract + post-save-load HTTP still open. |
| 3 | Extract the core render/math logic behind ports and adapters, then add mock and headless adapters so domain code can be tested without Unity. | `hexagonal-architecture-proposal.md`, `polyglot-architecture-survey.md` | L | med-high | Closes Unity API leakage into domain logic, brittle render/cache code, and the lack of a Unity-free seam for deterministic tests. **Partial (2026-05-23):** `BridgeSettingParser`, `SavedSettingsJson` + fuzz loops; render/math extraction not started. |
| 4 | Keep `phenotype-journeys`, but add a real pixel-diff backend plus a swappable capture layer so visual regressions become a hard gate. | `replace-journeys-research.md`, `visual-regression-harness-design.md`, `cross-cutting-concerns.md` | M | med | Closes the current screenshot-only gap, the lack of robust diffing, and the inability to swap capture plumbing without changing journey semantics. **Partial (2026-05-23):** integration-tier manifest/path + harness contract tests; no pixel-diff backend yet. |
| 5 | Build an L2 Unity Test Framework suite for EditMode and PlayMode checks against actual Unity render/material behavior. | `L2-unity-test-framework-design.md`, `test-coverage-gaps.md`, `integration-test-proposals.md` | M | med | Closes missing coverage for instancing, materials, camera masks, render visibility, cache behavior, and other real-Unity contract failures. |
| 6 | Add a first-class perf instrumentation path: overlay counters, cache hit/miss reporting, and a repeatable profiler harness for evidence-backed refactors. | `perf-roadmap-2026-05-19.md`, `cross-cutting-concerns.md` | M | low-med | Closes hidden cache behavior, missing hit/miss visibility, and the current need for profiler traces before committing to larger performance refactors. |
| 7 | Consolidate build/test/install/capture verbs into one thin task runner or Justfile so repeat workflows stop drifting across scripts. | `polyglot-architecture-survey.md`, `test-orchestrator-design.md`, `containerized-test-design.md` | S-M | low | Closes command drift, duplicated orchestration logic, and ad hoc per-tool invocation for routine developer workflows. **Partial (2026-05-23):** `Taskfile.yaml` / `Justfile` include `test-integration`; CI stub step shared across workflows. |
| 8 | Add governance gates: ADR enforcement, CODEOWNERS, API compatibility policy, and release provenance checks. | `governance-gaps.md`, `holistic-project-quality.md`, `cross-cutting-concerns.md` | M | low | Closes unowned changes, silent API drift, weak release discipline, and incomplete supply-chain/provenance coverage. |
| 9 | Validate a noninteractive CI launch path end to end, including Steamless-free direct launch first and fallback compatibility only if needed. | `headless-rendering-research.md`, `steamless-research.md`, `containerized-test-design.md` | M | med-high | Closes uncertainty around headless startup, renderer requirements, and whether the game can boot cleanly in automated environments. **Partial (2026-05-23):** CI stubs satisfy HintPaths for API/unit/integration; mod PE compile still blocked (`docs/ci-mod-compile-gap.md`). |
| 10 | Upgrade the docs/onboarding spine with a better getting-started path, troubleshooting, sample scenarios, and clear phase transition guidance. | `holistic-project-quality.md`, `governance-gaps.md`, `replace-journeys-research.md` | S-M | low | Closes install-to-value friction, unclear contributor next steps, and the gap between repo conventions and actually shipping a new phase. |

## Short take

If we only fund three things, I would pick 1, 2, and 3. They create the testing, observability, and architecture seams that every other investment depends on.

Since the 2026-05-20 audit, the highest-leverage **incremental** wins landed: CI ref stubs, fuzz-backed bridge/settings parsing, a real (Unity-free) integration tier, and bridge listener/drain hardening. Next funded slice should still be full rank **1** (parallel orchestrator) plus finishing rank **2** live verification after save-load.
