# WorldSphereMod3D (hard fork)

Docs: <https://kooshapari.github.io/WorldSphereMod/>

A hard fork of [MelvinShwuaner/WorldSphereMod](https://github.com/MelvinShwuaner/WorldSphereMod)
that finishes the 3D conversion of WorldBox.

Upstream `WorldSphereMod` puts the **terrain** in 3D but leaves every visible
entity — actors, buildings, drops, items, projectiles, effects, talk bubbles,
shadows, even Crabzilla and dragons — as a 2D `SpriteRenderer` that's been
rotated to face the camera. UI is flat Canvas, water is tile colour, foliage
is sprite cards, lighting is a skybox + per-tile baked colour, animation is
frame-swap sprite arrays.

This fork lands a real 3D pipeline on top of that foundation:

| Phase | Status | What changes |
|---|---|---|
| 0  | CODE_LANDED | Fork metadata, build portability (`WORLDBOX_PATH`), CI, settings/API v2 |
| 1  | PROVEN | Voxelized actors + buildings. **default ON** — `VoxelEntities = true`. visible_units=46 confirmed in-game. |
| 2  | CODE_LANDED | Procedural building meshes. **default OFF** — `ProceduralBuildings = false`. |
| 3  | CODE_LANDED | 3a trees/bushes/rocks crossed-quads + 3b surface overlays + walls as 3D prisms. Current code default: `CrossedQuadFoliage = false`. |
| 4  | CODE_LANDED | Mesh water — WaterGerstner shader source landed; AssetBundle bake deferred. **default OFF** — `MeshWater = false`. |
| 5  | CODE_LANDED | Sun driver + shadow cascade config + procedural sky landed. Current code defaults: `HighShadows = false`, `HdrSkybox = false`, `ColorGradingLut = false`. |
| 6  | CODE_LANDED | Skeletal pipeline. Current code default: `SkeletalAnimation = false`. |
| 7  | CODE_LANDED | Worldspace UI: nameplate, HP bar, damage popups, selection ring all landed; SelectionHooks wired via `SelectedUnit`. Current code defaults: `WorldspaceUI = false`, `WorldspaceLabel3D = false`. |
| 8  | CODE_LANDED | TimeOfDay autonomous driver + SunRig color gradient; ProceduralSky landed. **default OFF** — `DayNightCycle = false`; `FogDensity = 0.05f`. |
| 9  | CODE_LANDED | Particle bursts on 5 effect IDs + URP PostFX volume. Current code defaults: `ParticleEffects = false`, `PostFX = false`, `SSAOEnabled = false`, `SSGIEnabled = false`. |
| 10 | CODE_LANDED | FrustumCuller + LodSelector + ImpostorBillboard + softened hardware gate; Proxy tier still routes to Voxel. |

`v2.0.0-beta.0` marks the start of beta in the project history. Phase 1 voxel actors are now PROVEN with visible_units=46 confirmed in-game. Remaining phases await runtime validation per `docs/issue-triage.md`.

The full plan, including file-by-file changes and verification steps, lives
at `docs/PLAN.md`.

## Installation

This fork uses a different `GUID` (`worldsphere3d.fork`) than upstream so it
is **co-installable** with the original `WorldSphereMod`. Enable only one at a
time in NeoModLoader to avoid double-patching.

NeoModLoader compiles `Code/*.cs` at runtime, so the install copies sources
(plus `Assemblies/`, `AssetBundles/`, `GameResources/`, `Locales/`, `mod.json`)
into `<WorldBox>/Mods/WorldSphereMod3D/`. On Windows the fastest path is:

```powershell
./Tools/install.ps1
```

The script auto-detects the default Steam install at
`C:/Program Files (x86)/Steam/steamapps/common/Worldbox/`. Override with
`-WorldBoxPath`, `-InstallFolderName`, or by setting `$env:WORLDBOX_PATH`.
Pass `-SkipBuild` to skip the `dotnet build` sanity check.

## Building

The build uses your local WorldBox install for reference assemblies. Point
`WORLDBOX_PATH` at it (the folder containing `worldbox_Data/`):

```bash
# Linux/macOS
export WORLDBOX_PATH="$HOME/.steam/steam/steamapps/common/worldbox"
dotnet build WorldSphereMod.csproj -c Release

# Windows PowerShell
$env:WORLDBOX_PATH = "C:/Program Files (x86)/Steam/steamapps/common/worldbox"
dotnet build WorldSphereMod.csproj -c Release
```

If `WORLDBOX_PATH` is unset the build falls back to the default Steam
location for the host OS (see `Directory.Build.props`).

## Testing

Full gate layout (programmatic vs agentic, OmniRoute vision, bridge checklist):
[`docs/live-verification.md`](docs/live-verification.md).

### dotnet test

Fast, CI-friendly checks — API surface, install/manifest contracts, bridge source
invariants, harness preflight:

```powershell
dotnet test tests/WorldSphereMod.Tests.Unit
dotnet test tests/WorldSphereMod.Tests.Integration
dotnet test tests/WorldSphereMod.Tests.E2E
```

Or run the full suite via the CLI: `./Tools/wsm3d.ps1 test` (same three projects).

### wsm-live-verify.ps1

Orchestrates the programmatic pipeline and optional live stages; writes
`Tools/.reports/live-verify-latest.json`:

```powershell
# Offline (CI-equivalent): dotnet test + journey mock verify
pwsh Tools/wsm-live-verify.ps1

# With WorldBox + bridge on :8766: PlayCUA scenarios + optional SSIM
pwsh Tools/wsm-live-verify.ps1 -Live
pwsh Tools/wsm-live-verify.ps1 -Live -Vision   # OmniRoute vision on screenshot steps
```

Stages: (1) `dotnet test` unit/integration/e2e, (2) `phenotype-journey verify --mock`,
(3) `[-Live]` bridge + all `sample-scenarios/*.yaml` + SSIM vs `phase-previews/`,
(4) JSON report.

### playcua run-all

Agentic gate — requires a running game, mod installed, and BridgeRPC on
`127.0.0.1:8766`. Runs every YAML under `Tools/wsm3d-playcua/sample-scenarios/`:

```powershell
pip install -r Tools/wsm3d-playcua/requirements.txt
pwsh Tools/wsm3d.ps1 launch
Start-Sleep -Seconds 20
pwsh Tools/wsm3d.ps1 playcua run-all
pwsh Tools/wsm3d.ps1 playcua run-all -VisionBackend omniroute
```

Per-scenario artifacts land in `Tools/wsm3d-playcua/.reports/run-all-artifacts/`.

### CI gates

| Workflow | What it enforces |
|---|---|
| `build.yml` | `dotnet build` Release |
| `test-gate.yml` | Unit + integration tests (E2E not in this gate) |
| `live-verify-gate.yml` | Offline stages 1–2 of `wsm-live-verify.ps1` (`dotnet test` + journey mock) |
| `lint-gate.yml` | Format / analyzers |
| `journeys-gate.yml` | Journey JSON + fixture PNGs when `docs/journeys/**` changes |
| `docs-build-gate.yml` | Docs site build when docs change |
| `nightly.yml` | Full test matrix including E2E (integration may be allowed to fail) |

**PR merge bar:** green `build`, `test-gate`, `lint-gate`, `journeys-gate` (when
journeys change), and `docs-build-gate` (when docs change). PlayCUA, OmniRoute
vision, and live journey capture stay local until a Windows game runner exists.

## API

External mods linking against `WorldSphereAPI.dll` keep the v1 surface
unchanged. New v2 calls (`IsModel3D`, `RegisterCustomMesh`, …) safely no-op
when the connected host is upstream rather than this fork.

```csharp
if (WorldSphereAPI.Connect(out var api))
{
    if (api.IsModel3D) {
        api.RegisterCustomMesh("my_unit", myHandMadeMesh, myAlbedo);
    }
}
```

## Compatibility & hardware

The compute-shader / GPU-instancing / indirect-args gate from upstream is
preserved (`Mod.cs:21`). Hardware that fails the gate gets the same red icon
as before. Phase 10 will add an impostor-billboard fallback path so the mod
still does *something* useful on incompatible GPUs.

## Credits

- Upstream mod and `Compound-Spheres` rendering backend: **Melvin Shwuaner**.
- This fork: documented in `docs/PLAN.md`.

## Dev tooling

Parallel topic work uses git worktrees under `%USERPROFILE%\.cursor\worktrees\WorldSphereMod` (`wt/<topic>` branches, one subagent each).
Merge to `claude/research-ultraplan-fork-DdgI5` before a single `pwsh Tools/do-all.ps1 -Vision` from the main repo (OmniRoute vision degrades to off when the laptop probe fails).
Full workflow: [`docs/HANDOFF.md`](docs/HANDOFF.md#parallel-development-worktrees).

| Component | Purpose | Invocation |
|---|---|---|
| **wsm3d.ps1** | 540-LOC CLI: build, install, reload, toggle phases, screenshot, profiler, startup profile parse | `./Tools/wsm3d.ps1 build` / `install` / `reload-nml` / `toggle-phase` / `profile` / etc. (14 subcommands) |
| **wsm3d profile** | Parse `[WSM3D] InitProfiler` startup buckets from latest `Player.log`, sort by slowest, and show per-bucket totals; profiler overlay remains opt-in | `./Tools/wsm3d.ps1 profile` / `./Tools/wsm3d.ps1 profile -DryRun` / `/wsm-profile` |
| **wsm3d-stats.ps1** | Auto-generates stats dashboard (tests, LOC, patches, journeys, git, CI) to `docs/dashboard.md` | `pwsh Tools/wsm3d-stats.ps1` (runs nightly + on-demand) |
| **wsm3d tab-completion** | PowerShell argument completer for all CLI subcommands | See "Enable tab-completion" below |
| **MCP server** | Python FastMCP on port 8766; exposes phase state, build logs, manifest queries | `python Tools/wsm3d-mcp/main.py` (auto-launched by Claude commands) |
| **/wsm-* slash commands** | 10 Claude Code shortcuts: build, install, reload, phases, tests, profile | `/wsm-build`, `/wsm-install`, `/wsm-reload`, `/wsm-toggle-phase`, etc. |
| **wsm3d skill** | Auto-routed `.claude/skills/wsm3d/SKILL.md` for guided dev tasks | Invoked via Claude Code agent dispatch |
| **phase journeys** | 10 Phenotype-org manifests (Phase 1–10) with checklist + validation steps; phase-0 hardening journeys cover capability discovery, profiler overlay, and capture tooling | `docs/journeys/manifests/us-wsm-phase-*/` |
| **test suite** | 42 unit + 15 e2e tests (27 new); all green | `dotnet test` or `/wsm-test` |

### Day in the life of an iteration

```powershell
# 1. Make a code change
# 2. Build + install in one step
./Tools/wsm3d.ps1 install

# 3. Launch WorldBox with NML, wait for hot-reload (auto)
# 4. Toggle Phase 1 in NML settings or via:
./Tools/wsm3d.ps1 toggle-phase 1

# 5. Screenshot in-game result
./Tools/wsm3d.ps1 screenshot

# 6. Verify with test suite
./Tools/wsm3d.ps1 test

# 7. Commit
git add -A && git commit -m "Phase 1: ..."
```

### Slash commands available in Claude Code

| Command | Tooltip |
|---|---|
| `/wsm-build` | Build WorldSphereMod.csproj to bin/Release |
| `/wsm-install` | Install mod sources + assemblies to `<WorldBox>/Mods/WorldSphereMod3D/` |
| `/wsm-reload` | Signal NML to hot-reload this mod (if running) |
| `/wsm-toggle-phase` | Toggle a phase flag (1–10) on/off in SavedSettings |
| `/wsm-current-phase` | Show which phases are active |
| `/wsm-test` | Run full test suite (unit + e2e) |
| `/wsm-test-unit` | Run unit tests only |
| `/wsm-test-e2e` | Run e2e tests only |
| `/wsm-profile` | Start frame profiler, collect 60s, export CSV |
| `/wsm-manifest-query` | Search phase manifests by keyword (e.g., "water", "shadow") |

### Enable tab-completion (PowerShell)

The `wsm3d.ps1` CLI ships with a PowerShell argument completer. To enable it,
add this line to your PowerShell `$PROFILE`:

```powershell
. "$env:USERPROFILE\Dev\WorldSphereMod\Tools\wsm3d.completion.ps1"
```

Or run this one-liner from the repo root:

```powershell
Add-Content $PROFILE "`n. `"$pwd\Tools\wsm3d.completion.ps1`""
```

Then reload your shell. Type `wsm3d.ps1 <TAB>` to see completions for all
subcommands, phase toggles, and journey IDs.

## Backend

Upstream ships `CompoundSpheres.dll` as a vendored binary built from
[MelvinShwuaner/Compound-Spheres](https://github.com/MelvinShwuaner/Compound-Spheres).
Phase 0 keeps the vendored DLL for compatibility; Phase 5 replaces it with a
submodule build (`External/Compound-Spheres-3D/`) that emits per-vertex normals
and exposes a water-mask buffer.
