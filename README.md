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
| 0  | landed       | Fork metadata, build portability (`WORLDBOX_PATH`), CI, settings/API v2 |
| 1  | code-complete (opt-in) | Voxelized actors + buildings (with greedy meshing); visibly rendering after Wave-14 root-cause resolution (v2.0.0-alpha.7, 2026-05-19) |
| 2  | code-complete (opt-in) | Procedural building meshes (heuristic landed); visibly rendering in-game after `cullPos.To3DTileHeight` lift before FrustumCuller (2026-05-19, commit `3448c1f`, v2.0.0-alpha.5) — AutoTest telemetry: drawCalls=377 instances=37760 on save2 |
| 3  | code-complete | 3a trees/bushes/rocks crossed-quads + 3b surface overlays + walls as 3D prisms |
| 4  | code-complete (opt-in, lite) | Mesh water — WaterGerstner shader source landed; AssetBundle bake deferred to Phase 5b |
| 5  | code-complete (opt-in, SSAO pending) | Sun driver + shadow cascade config + procedural sky landed (full 360° cycle as of v2.0.0-alpha.6); SSAO not yet implemented. |
| 6  | in-progress  | Skeletal pipeline scaffold (rig cache + segmented humanoid skinned-mesh path + driver hooks) is now visibly deforming actors; GPU skinning path is still deferred per ADR-0006; flag default OFF. |
| 7  | code-complete | Worldspace UI: nameplate, HP bar, damage popups, selection ring all landed; SelectionHooks wired via `SelectedUnit`. |
| 8  | code-complete (opt-in, autonomous) | TimeOfDay autonomous driver + SunRig color gradient; ProceduralSky shader source landed; MapBox.world_time probe falls back since field absent. |
| 9  | partial      | Particle bursts on 5 effect IDs ✅ + URP PostFX volume ✅; `DecalPool` is initialized + ticked + cleared but no `Emit()` call site exists in code → decals are effectively absent (task #143). |
| 10 | code-complete (no proxy) | FrustumCuller + LodSelector + ImpostorBillboard + softened hardware gate; Proxy tier still routes to Voxel. |

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

| Component | Purpose | Invocation |
|---|---|---|
| **wsm3d.ps1** | 540-LOC CLI: build, install, reload, toggle phases, screenshot, profiler, startup profile parse | `./Tools/wsm3d.ps1 build` / `install` / `reload-nml` / `toggle-phase` / `profile` / etc. (14 subcommands) |
| **wsm3d profile** | Parse `[WSM3D] InitProfiler` startup buckets from latest `Player.log`, sort by slowest, and show per-bucket totals | `./Tools/wsm3d.ps1 profile` / `./Tools/wsm3d.ps1 profile -DryRun` / `/wsm-profile` |
| **wsm3d-stats.ps1** | Auto-generates stats dashboard (tests, LOC, patches, journeys, git, CI) to `docs/dashboard.md` | `pwsh Tools/wsm3d-stats.ps1` (runs nightly + on-demand) |
| **wsm3d tab-completion** | PowerShell argument completer for all CLI subcommands | See "Enable tab-completion" below |
| **MCP server** | Python FastMCP on port 8766; exposes phase state, build logs, manifest queries | `python Tools/wsm3d-mcp/main.py` (auto-launched by Claude commands) |
| **/wsm-* slash commands** | 10 Claude Code shortcuts: build, install, reload, phases, tests, profile | `/wsm-build`, `/wsm-install`, `/wsm-reload`, `/wsm-toggle-phase`, etc. |
| **wsm3d skill** | Auto-routed `.claude/skills/wsm3d/SKILL.md` for guided dev tasks | Invoked via Claude Code agent dispatch |
| **phase journeys** | 10 Phenotype-org manifests (Phase 1–10) with checklist + validation steps | `docs/journeys/manifests/us-wsm-phase-*/` |
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
