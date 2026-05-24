# Contributing to WorldSphereMod3D

This is the governance companion to `CLAUDE.md`. Read that first for the
project overview and conventions. This doc codifies the rules a PR author
is expected to follow.

## 1. Branch convention

`claude/research-ultraplan-fork-DdgI5` is the active development branch.
Push there, **never to `main`**. Sub-branches off the dev branch are fine
for stacked work; merge them back into the dev branch via PR, not into
`main`.

## 2. One PR per phase

Phases 0 through 10 each ship as their own PR. Do not bundle two phases
into a single PR even if they touch adjacent modules. Within a single
phase, commits can be incremental (step 1, step 2, …) — that's the
preferred shape for review.

## 3. Feature flag rule

Every new render mode is gated behind a field in
`WorldSphereMod/Code/SavedSettings.cs`. The flag ships **default-OFF**.
A phase only flips its own flag to `true` after in-game smoke testing
captures expected behavior at 360° camera sweep (see
`docs/smoke-test-phase1.md` for the template). Never flip another
phase's flag from inside your PR.

## 4. Co-installability

The `mod.json` `GUID` is `worldsphere3d.fork`. **Do not change it.** It
is the identity that lets this fork sit beside upstream `WorldSphereMod`
in NeoModLoader. Before opening a PR, install both mods side-by-side
and confirm upstream `THE_3D_WORLDBOX_MOD` still loads with this fork
present (only one enabled at a time to avoid double-patching, but both
must remain installable).

## 5. Build and install loop

The canonical local loop is:

```powershell
./Tools/install.ps1
```

NeoModLoader compiles `Code/*.cs` at runtime, so `install.ps1` copies
**source files** plus `Assemblies/`, `AssetBundles/`, `GameResources/`,
`Locales/`, and `mod.json` into `<WorldBox>/Mods/WorldSphereMod3D/`. The
script runs `dotnet build` first as a sanity gate; pass `-SkipBuild` only
when iterating on non-code resources. Use `Tools/uninstall.ps1` to clean
up between test runs.

## 6. Comment policy

Do not add comments that describe what code does — the code already says
that. The only acceptable new comments are one-liners that capture a
non-obvious *why*: a hidden invariant, a workaround for a WorldBox
behavior, a constraint that isn't visible from the call site (e.g. the
`Constants.ZDisplacement = 100` sentinel, thread-safety of a parallel
postfix, the cylindrical X-wrap). If the *why* takes more than a line,
put it in `docs/` and link to it.

## 7. Cross-phase coupling

When a change in Phase N touches code that a later Phase M depends on,
call it out in the PR description so the M author isn't surprised. The
inherited-upstream files — `Core.cs`, `QuantumSprites.cs`, `3DCamera.cs`,
`Effects.cs`, `Tools.cs`, `DimensionConverter.cs`, `General.cs`,
`TileMapToSphere.cs`, `CompoundSphereScripts.cs` — are tread-carefully:
~80 Harmony patches live across them and unrelated breakage cascades
fast. If you must edit one, keep the change minimal and isolated, and
explain the *why* in the PR body.

## 8. Decompile path

For undocumented WorldBox API surface, decompile
`Assembly-CSharp-Publicized.dll` with `ilspycmd`. Save the findings to a
new file under `docs/` (e.g. `docs/phase3-decompile-findings.md`) so the
next contributor doesn't re-do the investigation. Reference the findings
doc from the PR body.

## 9. Review routing

`.github/CODEOWNERS` now covers the main review routes for core mod code,
the public API, tools, workflows, docs, journeys, tests, and release
metadata. That gives us partial automated ownership routing for PRs, but
it does not replace live proof, in-game smoke verification, or the deeper
policy gates described in the rest of this guide.

## 10. Contributor verification flow

Run these in order before you push. They mirror what CI expects; details
and gate names are in [`MERGE_CHECKLIST.md`](MERGE_CHECKLIST.md).

### 10.1 Doctor (environment)

Checks WorldBox path, .NET SDK, Python (PlayCUA), git submodules, and
optional services (phenotype-journey, bridge, OmniRoute):

```powershell
pwsh Tools/wsm3d.ps1 doctor
pwsh Tools/wsm3d.ps1 doctor -Json   # machine-readable
```

Fix required failures first (`worldbox_path`, `dotnet_sdk`, `python`,
`git_submodules`). Warnings on bridge or journey are expected until the
game is running or tools are built locally.

Equivalent: `task doctor` or `/wsm-doctor` (see `.claude/commands/wsm-doctor.md`).

### 10.2 Live-verify offline (CI-equivalent)

Matches [`live-verify-gate.yml`](../.github/workflows/live-verify-gate.yml)
stages 1–2: full `dotnet test` matrix + phenotype journey mock. No game,
bridge, PlayCUA, or SSIM required.

```powershell
pwsh Tools/wsm-live-verify.ps1
pwsh Tools/wsm-live-verify.ps1 -ListScenarios   # enumerate PlayCUA scenarios for -Live
```

- Offline matrix: **482 pass / 3 skip** (485 total) — Unit 151 (+ 3 skip), Integration 69, E2E 262 via `dotnet test`
- Report: `Tools/.reports/live-verify-latest.json`
- Deep dive: [`live-verification.md`](live-verification.md); live proof bundle: [`#canonical-live-proof-bundle`](live-verification.md#canonical-live-proof-bundle)

Optional desktop proof (not required to merge): `pwsh Tools/wsm-live-verify.ps1 -Live`
with WorldBox + mod installed and BridgeRPC on `127.0.0.1:8766`. See the
[canonical live proof bundle](live-verification.md#canonical-live-proof-bundle).

### 10.3 PlayCUA (agentic / live desktop)

PlayCUA drives the running game via YAML scenarios (bridge health, toggles,
telemetry, screenshots, optional vision). Use after install + launch when
validating bridge behavior or phase smoke paths.

```powershell
pip install -r Tools/wsm3d-playcua/requirements.txt
python Tools/wsm3d-playcua/smoke.py
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/bridge-health-vision.yaml
```

Run every sample scenario (what `-Live` live-verify uses):

```powershell
pwsh Tools/wsm3d.ps1 playcua run-all
pwsh Tools/wsm3d.ps1 playcua run-all -VisionBackend omniroute   # optional VLM gate
```

Scenarios live under `Tools/wsm3d-playcua/sample-scenarios/` — see
[`sample-scenarios/README.md`](../Tools/wsm3d-playcua/sample-scenarios/README.md)
(catalog, vision gates, run matrix). Phase-specific in-game checklists:
`docs/smoke-test-phase*.md`.

### 10.4 Commit and PR conventions

| Rule | Detail |
|------|--------|
| **Branch** | `claude/research-ultraplan-fork-DdgI5` (dev branch). Do not push directly to `main`. |
| **PR shape** | One PR per phase (0–10). Incremental commits *within* a phase are encouraged. |
| **Commit messages** | [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `chore:`, `test:`, `ci:`. Example: `feat: journey for Phase 1 voxel actors`. |
| **Traceability** | When a change satisfies a requirement, cite `FR-WSM-NNN` or `NFR-WSM-NNN` in the PR title or commit body (see [`PRD.md`](PRD.md)). |
| **Journey-only work** | Follow [`journeys/CONTRIBUTING.md`](journeys/CONTRIBUTING.md) for manifest IDs, capture, and `phenotype-journey verify --mock`. |

### 10.5 Pre-merge checklist

Before marking a PR ready or merging to `main`, walk
[`MERGE_CHECKLIST.md`](MERGE_CHECKLIST.md): CI gate table, offline
live-verify, submodule pin, co-install GUID, and post-merge bake notes.
