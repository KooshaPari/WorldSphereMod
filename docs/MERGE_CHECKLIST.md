# Merge checklist — PR #1 → `main`

**PR:** [WorldSphereMod3D beta stabilization](https://github.com/KooshaPari/WorldSphereMod/pull/1) (`claude/research-ultraplan-fork-DdgI5`)

Use this before merging [PR #1](https://github.com/KooshaPari/WorldSphereMod/pull/1) into `main`.

## CI gates (must be green on the PR)

| Workflow | Role |
|---|---|
| [`build.yml`](../.github/workflows/build.yml) | `WorldSphereAPI` Release build (blocking); mod build best-effort only (`docs/ci-mod-compile-gap.md`) |
| [`test-gate.yml`](../.github/workflows/test-gate.yml) | `dotnet test` — Unit, Integration, E2E |
| [`live-verify-gate.yml`](../.github/workflows/live-verify-gate.yml) | Offline stages 1–2: full test matrix + journey mock via `Tools/wsm-live-verify.ps1` |
| [`journeys-gate.yml`](../.github/workflows/journeys-gate.yml) | Phenotype journey manifests (`--mock`) |
| [`lint-gate.yml`](../.github/workflows/lint-gate.yml) | Format / analyzer gate |
| [`docs-build-gate.yml`](../.github/workflows/docs-build-gate.yml) | VitePress docs build |
| [`dependency-security-audit.yml`](../.github/workflows/dependency-security-audit.yml) | Dependency audit |

**Nightly** ([`nightly.yml`](../.github/workflows/nightly.yml)) reuses `live-verify-gate` offline, then lint/stats extras — confirm on `main` after merge if the PR branch did not run it.

**CI summary (all checks on this PR):** https://github.com/KooshaPari/WorldSphereMod/pull/1/checks

### Current check status (2026-05-23, `b37a14c`)

**PR #1:** OPEN, **MERGEABLE** — https://github.com/KooshaPari/WorldSphereMod/pull/1

| Check | Blocking? | Status | Notes |
|---|---|---|---|
| `dotnet-build` | Yes | pass | [`build.yml`](../.github/workflows/build.yml) |
| `dotnet format` | Yes | pass | [`lint-gate.yml`](../.github/workflows/lint-gate.yml) |
| `dotnet-test / live verify (offline)` | Yes | pass | [`test-gate.yml`](../.github/workflows/test-gate.yml), [`live-verify-gate.yml`](../.github/workflows/live-verify-gate.yml) — **484 total / 481 passed / 0 failed** (Unit 151 + 3 skip, Integration 69, E2E 261) |
| `journeys verify` | Yes | pass | [`journeys-gate.yml`](../.github/workflows/journeys-gate.yml) |
| `VitePress build` | Yes | pass | [`docs-build-gate.yml`](../.github/workflows/docs-build-gate.yml) |
| `docs npm audit` | Yes | pass | [`dependency-security-audit.yml`](../.github/workflows/dependency-security-audit.yml) |
| `NuGet vulnerability audit` | Yes | pass | same workflow |
| `journey-records cargo audit` | Yes | pass | same workflow |
| `semgrep-cloud-platform/scan` | Advisory | pass | Semgrep Cloud |
| Socket Security (PR + project) | Advisory | pass | socket.dev |
| CodeRabbit | Advisory | pass | review skipped |
| **Vercel** (GitHub status) | **No** | fail | [Free-tier deploy rate limit](https://vercel.com/koosha-paridehpours-projects?upgradeToPro=build-rate-limit) — retry ~24h or upgrade |
| **Deploy Vercel Preview** | **No** | fail | Same quota (`api-deployments-free-per-day`); docs proof is **VitePress build** + GitHub Pages |

**Merge readiness:** all **blocking** repo gates green; Vercel preview/production failures are external quota only.

### Known / external check failures (not repo code)

| Check | Status | Owner / action |
|---|---|---|
| **Vercel** (Preview + Production) | Failing | [Vercel build rate limit](https://vercel.com/koosha-paridehpours-projects?upgradeToPro=build-rate-limit) — retry after ~24h or upgrade plan. Docs deploy via GitHub Pages + `VitePress build` gate are green. |
| **docs npm audit** | pass | Fixed in workflow: allow transitive moderate advisories when `via` is only allowlisted deps (`vite` → `vitepress`). |
| **dotnet-test / live verify (offline)** | pass | Integration `skipped_no_fixture` + `verify-journeys.ps1` path fixes landed on branch. |

Re-run failed workflows from the [PR Checks](https://github.com/KooshaPari/WorldSphereMod/pull/1/checks) tab after pushing fixes (Vercel only when quota resets).

## Live-verify offline (local, CI-equivalent)

Matches `live-verify-gate` and nightly offline stages (no bridge, PlayCUA, or SSIM):

```powershell
pwsh Tools/wsm-live-verify.ps1
```

- Report: `Tools/.reports/live-verify-latest.json`
- Details: [`docs/live-verification.md`](live-verification.md), [`docs/HANDOFF.md`](HANDOFF.md) (Dev tooling)

Optional desktop proof (not required to merge): full agentic tier needs **WorldBox running + bridge on `127.0.0.1:8766` + OmniRoute** (for vision):

```powershell
pwsh Tools/wsm-live-verify.ps1 -Live -Vision
```

Without `-Live`, stages 1–2 only (CI-equivalent offline). Without `-Vision`, PlayCUA screenshot checks skip the OmniRoute VLM backend.

For the canonical release/handoff evidence bundle, use [`docs/live-verification.md`](live-verification.md#canonical-live-proof-bundle). That checklist is the source of truth for the required command output, report JSON, PlayCUA artifacts, phase-preview SSIM fixtures, and the explicit `live-playcua-ssim` skip/offline note when applicable.

## Release tag

- Remote latest: **`v2.0.0-beta.6`** — [GitHub Release](https://github.com/KooshaPari/WorldSphereMod/releases/tag/v2.0.0-beta.6) (2026-05-24)

## Submodule pin (do not bump casually)

- Path: `External/Compound-Spheres` → upstream [`MelvinShwuaner/Compound-Spheres`](https://github.com/MelvinShwuaner/Compound-Spheres) `main`
- **Pinned SHA:** `73a7b77` — do not bump casually
- The mod ships **vendored** `WorldSphereMod/Assemblies/CompoundSpheres.dll`, not a submodule MSBuild compile
- Keep the parent-repo submodule SHA on upstream `main` unless we own a fork with write access
- Optional local-only patch `dd78b11` (null guard on `Material.SetTexture`) — see [`docs/ci-mod-compile-gap.md`](ci-mod-compile-gap.md) § "`External/Compound-Spheres` submodule"
- **Push parent repo with:** `git push --no-recurse-submodules origin HEAD` (avoids accidental submodule pointer pushes)

## Co-install GUID warning

- Fork `mod.json` **GUID:** `worldsphere3d.fork` (stable; do not change without an ADR)
- **Co-installable** with upstream `WorldSphereMod` — install paths differ, but **enable only one mod at a time** in NeoModLoader to avoid duplicate Harmony patches and cache conflicts
- Install: `Tools/install.ps1` or `pwsh Tools/wsm3d.ps1 install`

## Post-merge bake (manual, not CI)

After merge, when touching shaders or AssetBundles:

1. Install **Unity 2022.3** and init submodules: `git submodule update --init --recursive`
2. Bake platform bundles per [`docs/phase5-prep.md`](phase5-prep.md): `WorldSphereMod/AssetBundles/{win,linux,osx}/worldsphere` (four WSM3D shaders via `Tools/Unity-Bake-Project`)
3. Commit baked bundles only when shader sources changed; smoke in-game per [`docs/smoke-test-phase1.md`](smoke-test-phase1.md) / [`docs/HANDOFF.md`](HANDOFF.md)

## Quick pre-merge command block

```powershell
dotnet test tests/WorldSphereMod.Tests.Unit
dotnet test tests/WorldSphereMod.Tests.Integration
dotnet test tests/WorldSphereMod.Tests.E2E
pwsh Tools/wsm-live-verify.ps1
```
