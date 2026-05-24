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

### Known / external check failures (not repo code)

| Check | Status | Owner / action |
|---|---|---|
| **Vercel** (Preview + Production) | Failing | [Vercel build rate limit](https://vercel.com/koosha-paridehpours-projects?upgradeToPro=build-rate-limit) — retry after ~24h or upgrade plan. Docs deploy via GitHub Pages is green. |
| **docs npm audit** | Was failing (`vitepress:unknown`) | Fixed in workflow: allow transitive moderate advisories when `via` is only allowlisted deps (`vite` → `vitepress`). Job uses `continue-on-error` but should pass after fix. |
| **dotnet-test / live verify (offline)** | Was failing | Integration tests still expected `skipped_no_fixture` after harness change in `ea16da2`; fixed in tests on this branch. |

Re-run failed workflows from the [PR Checks](https://github.com/KooshaPari/WorldSphereMod/pull/1/checks) tab after pushing fixes.

## Live-verify offline (local, CI-equivalent)

Matches `live-verify-gate` and nightly offline stages (no bridge, PlayCUA, or SSIM):

```powershell
pwsh Tools/wsm-live-verify.ps1
```

- Report: `Tools/.reports/live-verify-latest.json`
- Details: [`docs/live-verification.md`](live-verification.md), [`docs/HANDOFF.md`](HANDOFF.md) (Dev tooling)

Optional desktop proof (not required to merge): `pwsh Tools/wsm-live-verify.ps1 -Live` with WorldBox + bridge on `127.0.0.1:8766`.

## Submodule pin (do not bump casually)

- Path: `External/Compound-Spheres` → upstream [`MelvinShwuaner/Compound-Spheres`](https://github.com/MelvinShwuaner/Compound-Spheres) `main`
- The mod ships **vendored** `WorldSphereMod/Assemblies/CompoundSpheres.dll`, not a submodule MSBuild compile
- Keep the parent-repo submodule SHA on upstream `main` unless we own a fork with write access
- Optional local-only patch `dd78b11` (null guard on `Material.SetTexture`) — see [`docs/ci-mod-compile-gap.md`](ci-mod-compile-gap.md) § "`External/Compound-Spheres` submodule"

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
