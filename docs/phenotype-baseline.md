# Phenotype Org Baseline Checklist — WorldSphereMod3D

Canonical gate for "is this repo org-compliant?" Future agents and instances
should re-run this checklist against the live filesystem (not against prior
agent assertions) before claiming the repo conforms to Phenotype org
conventions.

- **Repo:** `C:/Users/koosh/Dev/WorldSphereMod`
- **Branch at audit:** `claude/research-ultraplan-fork-DdgI5`
- **Audit date:** 2026-05-18
- **Reference set:** `C:/Users/koosh/Dino/` (DINOForge) + KooshaPari org SDKs
  (`phenodocs`, `PhenoSpecs`, `TestingKit`, `McpKit`, `AuthKit`,
  `ObservabilityKit`).
- **Project kind:** Unity / NeoModLoader / Harmony **mod** (not a service,
  not a library users `npm install`, not a hosted product).

Legend: ✅ done · 🔄 in progress / partial · ❌ missing · ➖ N/A (justified)

---

## Section 1 — Convention Checklist

### 1.1 Top-level files

| File | Status | Notes |
|---|---|---|
| `CLAUDE.md`            | ✅ | Present at repo root. Phase plan + conventions documented. |
| `AGENTS.md`            | ❌ | Missing. Org convention is one peer file per agent host (Claude / OpenAI agents / Gemini). |
| `GEMINI.md`            | ❌ | Missing. Can be a symlink/stub pointing at `CLAUDE.md` if content is identical. |
| `README.md`            | ✅ | Phase table + install/build instructions. |
| `CONTRIBUTING.md`      | 🔄 | Lives at `docs/CONTRIBUTING.md`, not at repo root. Org convention puts it at root. |
| `CODE_OF_CONDUCT.md`   | ❌ | Missing. |
| `SECURITY.md`          | ❌ | Missing. Org convention: vulnerability reporting policy + supported versions. |
| `SUPPORT.md`           | ❌ | Missing. Where users go for help (issues vs. discussions vs. discord). |
| `RELEASING.md`         | ❌ | Missing. How a maintainer cuts a release of this mod (tag, asset upload to NML registry, etc.). |
| `CHANGELOG.md`         | ❌ | Missing. Keep-a-Changelog style is org default. |
| `VERSION`              | ❌ | Missing. Single source of truth for the mod version (today the version lives only in `mod.json`). |
| `LICENSE`              | ❌ | Missing — upstream `WorldSphereMod` repo is unlicensed too; fork needs to either inherit explicitly or pick MIT/Apache. |
| `.gitignore`           | ✅ | Present (24 bytes — very minimal; may not cover `bin/`, `obj/`, `*.binlog`). |

### 1.2 Task runners

| File | Status | Notes |
|---|---|---|
| `Taskfile.yaml`       | ❌ | Missing. Org convention is a `task build` / `task test` / `task lint` / `task docs` entry surface. |
| `Justfile`            | ❌ | Missing. Org keeps both as alternatives; either is acceptable. |
| Local scripts         | 🔄 | `Tools/install.ps1` and `Tools/uninstall.ps1` exist; they cover the "deploy to WorldBox" task but not build/test/lint/docs. |

### 1.3 Tests

| Item | Status | Notes |
|---|---|---|
| `tests/unit`            | ❌ | No `tests/` directory at all. Unit-test scaffolding for the C# code (xUnit / NUnit) does not exist. |
| `tests/integration`     | ❌ | Missing. |
| `tests/e2e`             | 🔄 | `WorldSphereTester/` (a separate NeoModLoader mod that exercises `WorldSphereAPI` in-game) is functionally an e2e smoke harness. Org-shaped name would be `tests/e2e/` or `src/Tests/e2e`. |
| `StrykerConfig.json`    | ❌ | Missing. Mutation testing not yet wired. |
| `TEST_COVERAGE_PLAN.md` | ❌ | Missing. No coverage target documented. |
| Coverage reporting      | ❌ | No `coverlet`, no `codecov.yml`, no coverage artifact in CI. |

### 1.4 Docs site

| Item | Status | Notes |
|---|---|---|
| `docs/.vitepress/`    | 🔄 | Present as a docs site entrypoint via `docs/package.json`; the site still needs a few content/validation passes, but the docs tree is no longer absent. |
| `docs/journeys/`      | ✅ | Present with manifests, assets, authoring guidance, and capture runbooks. Remaining gap is live-capture coverage for the latest phase-0 hardening changes. |
| `docs/adr/`           | 🔄 | Present with ADRs and phase findings. Additional ADR coverage may still be warranted for phase-0 hardening and journey-gate details. |
| `docs/` content       | 🔄 | Rich phase-by-phase markdown exists (`PLAN.md`, `HANDOFF.md`, `phase{1..10}-architecture.md`, `performance.md`, `render-data-fields.md`) and is now paired with journeys content, but it still needs continued curation rather than being treated as a finished reference site. |
| `llms.txt` / `llms-full.txt` | ❌ | Missing. Org convention surfaces these at repo root for LLM-friendly indexing. |

### 1.5 CI gates

Reference repo runs ~40 workflow files. This repo runs 1.

| Gate | Status | Notes |
|---|---|---|
| `build-gate`          | 🔄 | `.github/workflows/build.yml` gates `WorldSphereAPI`; mod build is best-effort (`docs/ci-mod-compile-gap.md`). Stub paths centralized in `Tools/ci-stub-worldbox-refs.sh`. |
| `test-gate`           | ❌ | No test workflow because no tests. |
| `lint-gate`           | ❌ | No `dotnet format`, no analyzers config, no editorconfig enforcement. |
| `docs-build-gate`     | 🔄 | VitePress build entrypoint exists, but link-check and full journey-quality gating are still not complete. |
| `release` workflow    | ❌ | No tag-driven release pipeline. Mod is distributed by manual ZIP / `Tools/install.ps1`. |
| `codeql` / security   | ❌ | Missing. |
| `sbom`                | ❌ | Missing. |
| `scorecard`           | ❌ | Missing. |
| `changelog-lint`      | ❌ | Missing (and moot until `CHANGELOG.md` exists). |
| Custom code gates (sync-over-async, configureawait, datetime, httpclient, etc., as in DINOForge) | ➖ | N/A for now — see Section 2. |

### 1.6 Org SDK integrations

| SDK | Status | Notes |
|---|---|---|
| `KooshaPari/phenodocs`        | ❌ | Not referenced. Would be the source of canonical org conventions if/when published. |
| `KooshaPari/PhenoSpecs`       | ❌ | No ADR template / spec template pulled. |
| `KooshaPari/TestingKit`       | ➖ planned | This is a C# Unity-runtime mod; TestingKit (if it grows .NET bindings) would be the right home for the `WorldSphereTester` harness. Track but don't block. |
| `KooshaPari/McpKit`           | ➖ planned | Possibly applicable if we expose an MCP control surface for in-game scripted tests (drive camera, toggle phase flags, trigger spawns). Worth a spike, not required. |
| `KooshaPari/AuthKit`          | ➖ N/A | This is a single-player game mod. No user accounts, no service surface to authenticate against. |
| `KooshaPari/ObservabilityKit` | ➖ N/A (mostly) | Mod logs through NeoModLoader; emitting OTLP from inside WorldBox is out of scope. A minimal "phase render counters → log line" hook could borrow ObservabilityKit conventions if it lands as a .NET nuget. |

---

## Section 2 — What's Explicitly Not Relevant

This is a Unity / NeoModLoader / Harmony mod that ships as a folder of C#
sources + AssetBundles dropped into `<WorldBox>/Mods/`. Several Phenotype
org conventions assume a service or library shape and do not apply:

- **AuthKit** — N/A. No authentication boundary in a single-player mod.
- **Service-shaped CI gates** — N/A. No HTTP surface means `httpclient-gate`,
  `configureawait`, `sync-over-async-gate`, `event-lifecycle-gate`,
  `blocking-poll-gate`, `di-validation-gate` from the DINOForge reference
  list don't apply.
- **SBOM / supply-chain attestation at release time** — Deferred, not N/A.
  Mod ships as source; supply chain is "whatever NuGet packages the user's
  `dotnet build` resolves." Worth a SBOM once we ship a binary release.
- **Cloud deploy / `Dockerfile` / `deploy-debug.sh`** — N/A. Distribution is
  install-into-game-folder, not container.
- **Polyglot build (`POLYGLOT_BUILD.md`)** — N/A. Single language (C#).
- **Fuzzing corpus (`src/Tests/FuzzCorpus`)** — N/A in the strict sense. The
  mod has no parser surface that ingests untrusted bytes. Could be relevant
  if `WorldSphereAPI.RegisterCustomMesh` becomes a content pipeline.
- **`McpKit`** — *Possibly* applicable, not currently. An MCP control surface
  for the in-game tester would let CI drive deterministic scenarios.
  Tracked under "planned" rather than "N/A."

---

## Section 3 — Migration Plan

Sequenced from cheapest / highest-value to most expensive. Each item lists an
estimate (S = under an hour, M = a few hours, L = a day or more).

### Wave 1 — Org hygiene (S each, ~half a day total)

1. **`LICENSE`** — Pick one (suggest MIT to match the NeoModLoader / WorldBox
   modding ecosystem norm). Add author + year. **S.**
2. **`VERSION`** — Single line, e.g. `0.1.0`. Wire `WorldSphereMod.csproj` and
   `mod.json` to read from it (or vice versa). **S.**
3. **`CHANGELOG.md`** — Initialize with Keep-a-Changelog header and one
   `0.1.0 — Unreleased` section summarizing Phases 0–10 as landed. **S.**
4. **`CODE_OF_CONDUCT.md`** — Contributor Covenant v2.1 verbatim. **S.**
5. **`SECURITY.md`** — Where to email + supported versions table. **S.**
6. **`SUPPORT.md`** — Point to GitHub Issues + the upstream mod's Discord if
   we want to share that channel. **S.**
7. **Move `docs/CONTRIBUTING.md` → `CONTRIBUTING.md`** at repo root. **S.**
8. **`AGENTS.md` + `GEMINI.md`** — Either real per-agent guidance, or stubs
   that `See CLAUDE.md`. **S.**
9. **`.gitignore`** — Expand to cover `bin/`, `obj/`, `*.binlog`,
   `WorldSphereMod/AssetBundles/*/*.manifest`, `.vs/`, `.idea/`. **S.**

### Wave 2 — Task surface + docs site (M, ~one day)

10. **`Taskfile.yaml`** with `build`, `build:api`, `test`, `lint`, `install`,
    `uninstall`, `docs:dev`, `docs:build`. Forward to existing
    `dotnet build` / `Tools/install.ps1`. **M.**
11. **`Justfile`** as a thin wrapper around the same targets for users who
    prefer `just`. **S.**
12. **`docs/.vitepress/config.mts`** scaffolded with nav: Plan, Handoff,
    Phases 1–10, Phase 5 Prep, Performance, Render-data Fields, Smoke Test.
    **M.**
13. **`docs/adr/`** — Backfill ADRs for the load-bearing decisions we already
    made: ADR-001 Hard-fork strategy and GUID split, ADR-002 Z-displacement
    sentinel value, ADR-003 Compute-shader hardware gate, ADR-004
    `WORLDBOX_PATH` portability, ADR-005 Render-mode `SavedSettings` flags
    default-OFF until in-game smoke. **M.**
14. **`docs/journeys/`** — One journey per phase the user can perceive
    in-game (J1 launch with mod, J2 voxel actors visible, J3 procedural
    buildings, J7 worldspace UI, J8 day/night cycle, J9 postFX, J10 LOD
    impostor on weak GPU). **M.**

### Wave 3 — Tests + gates (L, multi-day)

15. **`tests/` reorganization** — Create `tests/unit` (xUnit project against
    `WorldSphereAPI`, since it's the only Unity-free assembly we can run
    headless), `tests/integration` (placeholder), and either move
    `WorldSphereTester/` to `tests/e2e/WorldSphereTester/` or symlink it.
    **M.**
16. **`StrykerConfig.json`** — Target `WorldSphereAPI` only (the mod itself
    needs Unity to run). **S.**
17. **`TEST_COVERAGE_PLAN.md`** — State the coverage policy honestly: 80%
    line on `WorldSphereAPI`, 0% achievable on Unity-dependent code, e2e
    smoke is mandatory per phase. **S.**
18. **Real `test-gate` workflow** running `dotnet test` on `WorldSphereAPI`
    tests. **S.**
19. **`lint-gate`** — `dotnet format --verify-no-changes` + an `.editorconfig`
    that matches the existing brace / spacing style. **M.**
20. **`docs-build-gate`** — `pnpm vitepress build docs` + a link checker.
    **S** after Wave 2 lands.
21. **Improve `build-gate`** — Either generate non-zero-byte stub DLLs (e.g.
    via `AsmResolver`) so the main mod build is a real gate, or keep it as
    "best-effort" and stop calling it `build`. Document either way. **L.**

### Wave 4 — Release pipeline (L, future)

22. **`RELEASING.md`** — Document tag → build → ZIP `<WorldBox>/Mods/`
    layout → GitHub release with the ZIP attached.
23. **`release.yml`** workflow that triggers on `v*` tags.
24. **`changelog-lint`** workflow once `CHANGELOG.md` exists.
25. **SBOM + scorecard + CodeQL** — Org-standard, low value for a mod, defer.

### Wave 5 — Optional org-SDK integrations (spikes, no commitment)

26. **McpKit spike** — Stand up an MCP control surface inside
    `WorldSphereTester` so an external Claude/agent run can drive in-game
    state transitions (toggle SavedSettings, jump phases, capture
    screenshots). Would unblock real e2e gating from CI.
27. **TestingKit + ObservabilityKit** — Adopt once they ship .NET bindings.

---

## Section 4 — Sources

### DINOForge files compared against

- `C:/Users/koosh/Dino/CLAUDE.md`, `AGENTS.md`, `GEMINI.md`,
  `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md`,
  `RELEASING.md`, `CHANGELOG.md`, `VERSION`, `LICENSE`
- `C:/Users/koosh/Dino/Taskfile.yaml`, `C:/Users/koosh/Dino/Justfile`
- `C:/Users/koosh/Dino/StrykerConfig.json`,
  `C:/Users/koosh/Dino/TEST_COVERAGE_PLAN.md`
- `C:/Users/koosh/Dino/codecov.yml`, `C:/Users/koosh/Dino/lefthook.yml`,
  `C:/Users/koosh/Dino/docfx.json`
- `C:/Users/koosh/Dino/llms.txt`, `C:/Users/koosh/Dino/llms-full.txt`
- `C:/Users/koosh/Dino/docs/.vitepress/config.mts`,
  `C:/Users/koosh/Dino/docs/.vitepress/theme/`
- `C:/Users/koosh/Dino/docs/adr/ADR-001-agent-driven-development.md` …
  `ADR-019-mod-manager-client.md`, `docs/adr/index.md`
- `C:/Users/koosh/Dino/docs/journeys/manifests/{us-f1-1-game-launch,
  us-f2-1-unit-spawn, us-f3-1-debug-overlay, us-f4-1-menu-nav}/`
- `C:/Users/koosh/Dino/.github/workflows/{ci.yml, lint.yml, release.yml,
  release-drafter.yml, codeql.yml, sbom.yml, scorecard.yml,
  changelog-lint.yml, mutation-test.yml, journey-quality-gates.yml,
  api-docs.yml, polyglot-build.yml, …}`
- `C:/Users/koosh/Dino/src/Tests/{Benchmarks, Bridge, CliToolTests,
  CompanionTests, e2e, ECS, Fixtures, FuzzCorpus, FuzzTargets}`

### Org SDKs referenced

- `KooshaPari/phenodocs` — canonical doc conventions (assumed; not yet
  cloned locally).
- `KooshaPari/PhenoSpecs` — ADR + spec templates (assumed).
- `KooshaPari/TestingKit` — shared test harness (assumed; .NET bindings
  status unknown).
- `KooshaPari/McpKit` — MCP server scaffolding (assumed).
- `KooshaPari/AuthKit` — N/A for this project.
- `KooshaPari/ObservabilityKit` — OTel + structured logging (assumed).

### Live repo state inspected for this audit

- `C:/Users/koosh/Dev/WorldSphereMod/` (root file listing)
- `C:/Users/koosh/Dev/WorldSphereMod/.github/workflows/build.yml`
- `C:/Users/koosh/Dev/WorldSphereMod/docs/` (full listing)
- `C:/Users/koosh/Dev/WorldSphereMod/WorldSphereTester/` (code + csproj)
- `C:/Users/koosh/Dev/WorldSphereMod/CLAUDE.md`,
  `C:/Users/koosh/Dev/WorldSphereMod/README.md`

---

## Summary

- **Top-level org files:** 2 / 13 satisfied (CLAUDE.md, README.md). 11 gaps.
- **Task runners:** 0 / 2.
- **Tests:** 0 / 5 in canonical shape (e2e is present but mis-located).
- **Docs site:** 0 / 3 canonical subtrees (`.vitepress`, `journeys`, `adr`).
- **CI gates:** ~0.5 / 5 (build is partial; everything else missing).
- **Org SDK integrations:** 0 active, 2 planned, 1 N/A.

**Overall org-baseline conformance: roughly 10%.** Most of the gap is in
Wave 1 (org hygiene files) and Wave 2 (task surface + docs site
structure), both of which are cheap. The expensive items (real CI gates,
release pipeline, MCP control surface) can wait until the phase work is
in-game-validated.
