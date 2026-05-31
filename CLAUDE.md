# CLAUDE.md — WorldSphereMod3D fork

Read this first when you join the project cold.

## What this is

A hard fork of `MelvinShwuaner/WorldSphereMod`, a NeoModLoader/Harmony mod
for WorldBox. Upstream renders **terrain** as a real 3D mesh but every
visible entity (actors, buildings, drops, items, projectiles, effects,
shadows) is still a 2D `SpriteRenderer` rotated to face the camera. This
fork — `WorldSphereMod3D` — is finishing the 3D conversion: voxelized
actors/items, procedural building meshes, real-mesh foliage/walls/clouds,
mesh water + slope smoothing, real sun + cascaded shadows, skeletal
animation, worldspace UI, day/night, post-FX, and far-LOD culling.

### Current architecture (post-2026-05-30 — read this, the model shifted)

- **Working tree is on `E:` now:** `E:/Dev/WorldSphereMod` (C: filled up).
  All paths below are relative to that root.
- **Surface geometry (terrain + water + slope smoothing) lives IN the
  Compound-Spheres fork, not the main mod.** The fork's
  `CompoundSpheres/HeightFieldRenderer.cs` emits a single corner-averaged
  height-field mesh with analytic gradient normals; water is a second
  sub-mesh in the same rebuild pass. The main mod is now a pure data/adapter
  provider (`Core.cs ConfigureHeightField` / `ConfigureWater`). The old
  main-mod overlays (`Code/Water/WaterSurface.cs`, `WaterRender.cs`,
  `WaterMaskBuffer.cs`, `Code/Terrain/TerrainSmoothing.cs`) are **retired**.
  See `docs/adr/ADR-fork-terrain-water-slope.md`.
- **No billboards anywhere. "Voxel-or-invisible" policy:** every former
  billboard surface (clouds, buildings, foliage, walls, actors) now renders
  as a real 3D voxel/mesh, or is culled at far LOD — never a camera-facing
  quad. The crossed-quad system is **deleted**
  (`CrossedQuadMeshCache`/`Mesher`, `ImpostorBillboard`, the water-overlay
  stubs, the `TerrainSmoothing` stub). See commits `63940cd8` (enforce
  voxel-or-invisible) and `6bf38fde` (eliminate crossed-quad).
- **Rigging is stably disabled → static voxel mesh.** Skinned actors had a
  bind-pose scale mismatch and flickered; the rig path is force-off and
  actors emit a static voxel mesh. Re-enable later with centroid bind poses.
  See `4f85defa`.
- **Headless bridge controls the whole game (zero clicks).**
  `Code/Bridge/BridgeActions.cs` + `BridgeServer.cs` expose an HTTP API on
  `127.0.0.1:8766`: `/actions/new_world`, `/actions/spawn_units`,
  `/actions/camera`, `/actions/set_speed`, `/actions/select_tool` +
  `/actions/use_tool`, `/world/state`, `/tools`. Drive world-creation,
  spawning and tools without touching the UI. `spawn_units` uses
  `spawnNewUnitByPlayer` so units persist.
- **Machine-readable render diagnostics:** `Code/Voxel/RenderErrorRegistry.cs`
  + `GET /diag/errors` return typed render-failure JSON; in-world error
  markers (`RenderErrorMarkers.cs`, gated by the `RenderErrorProps` flag) and
  a `[WSM3D][ERRORS]` log summary surface failures. See `5c137e10`.
- **GPU-compute renderer base (in progress).** We are adopting Melvin's
  newer GPU-driven instanced engine (compute kernels compute model
  matrices + colors, indirect draw) as the SOTA base, behind an adapter
  shim, incrementally (P1–P3 landed: the authored `.compute` keystone, GPU
  manager + shim, compute-bundle bake wiring). Go-live ships in slices.
  See `docs/adr/ADR-sota-gpu-compute-adoption.md`.
- **Magenta-actor root cause (fixed):** the `OpaqueVertexColor`
  `INSTANCING_ON` shader variant failed going 2022.3.62f3 → 60f1; the fix is
  the buffer-driven `CompoundSphere.shader` (no `_Color` cbuffer). **Unity
  2022.3.60f1 is now installed at `E:/Unity/Hub/Editor/2022.3.60f1`** for
  matched-version AssetBundle bakes.
- **Both forks now track upstream.** `WorldSphereMod` and `Compound-Spheres`
  each have an `upstream` remote (`MelvinShwuaner/*`). Divergence audit:
  `docs/upstream-divergence-audit.md`; trajectory/gap foresight:
  `docs/foresight-melvin-trajectory-and-gaps.md`.

## Start here

1. **`docs/HANDOFF.md`** — current state, what's blocked on a local Unity
   install, prioritized next steps.
2. **`docs/PLAN.md`** — the full 10-phase plan with file paths and
   verification per phase.
3. **PR #1** on GitHub — draft, all CI green at handoff, phase-by-phase commits.

## Conventions

- **Branch:** current active branch is `fix/shader-standard-fallback`. Push
  to the active feature branch, not `main`. Push the parent with
  `git push --no-recurse-submodules` and keep the submodule on `wsm3d/main`.
- **One PR per phase.** Don't bundle multiple phases into one PR.
- **`mod.json` GUID** is `worldsphere3d.fork` — co-installable with upstream.
  Don't change it casually.
- **`SavedSettings` flags** gate every phase. New phases ship default-OFF until
  validated in-game.
- **No new comments** explaining what code does. Comments only when they
  capture a non-obvious *why* (invariant, workaround, hidden constraint).
- **External assemblies** are referenced via `$(WorldBoxPath)` from
  `Directory.Build.props`; never hard-code Steam paths in a `.csproj`.
- **Tooling first**: prefer `pwsh Tools/wsm3d.ps1` and `/wsm-*` slash commands over raw `dotnet` / file operations. See the Dev tooling section.

## Build

```bash
# Linux/macOS
export WORLDBOX_PATH="$HOME/.steam/steam/steamapps/common/worldbox"
dotnet build WorldSphereMod.csproj -c Release

# Windows PowerShell
$env:WORLDBOX_PATH = "C:/Program Files (x86)/Steam/steamapps/common/worldbox"
dotnet build WorldSphereMod.csproj -c Release
```

After a successful `dotnet build`, always launch through NML and check
`Player.log` for **both** `error CS` **and** `Failed to compile` after
EVERY launch before treating the build as valid.

CI in `.github/workflows/build.yml` builds only `WorldSphereAPI.csproj`
(it's Unity-free, targets netstandard2.0). The main mod can't be built in
CI because it needs WorldBox's reference DLLs — that's local-only.

## Dev tooling (use these instead of raw commands)

The repo ships a full dev toolchain. Prefer it over raw `dotnet` / `pwsh`
invocations:

- **CLI**: `pwsh Tools/wsm3d.ps1 <cmd>` — 13 subcommands: build, install,
  launch, kill, relaunch, log, screenshot, settings get/set, toggle,
  phases list, status [-Json], journey list/run/verify/capture.
  Run `pwsh Tools/wsm3d.ps1 help` for the surface.
- **MCP server**: `Tools/wsm3d-mcp/` — Python FastMCP exposing 18 tools
  for the whole dev loop (game_launch, log_grep, settings_toggle,
  journey_run, etc). Auto-registered via `.claude/mcp-servers.json` on
  stdio + HTTP :8766. Install once with `pip install -e Tools/wsm3d-mcp`.
- **Slash commands**: `.claude/commands/wsm-*.md` — 10 in-Claude
  shortcuts: /wsm-status, /wsm-build, /wsm-install, /wsm-relaunch,
  /wsm-log, /wsm-toggle, /wsm-screenshot, /wsm-journey-run,
  /wsm-validate-all, /wsm-doctor.
- **Skill**: `.claude/skills/wsm3d/SKILL.md` — auto-invoked when the
  task description matches dev-loop work; encodes the seven pitfalls
  we've actually hit (CompoundSpheres dep, net48 retarget, instancing
  silent-fail, etc).
- **Phenotype journeys**: `docs/journeys/manifests/us-wsm-phase-{1..10}-*/`
  with OCR-assertion DSL. Capture: `pwsh Tools/wsm3d.ps1 journey capture
  -Id <id>`. Verify: `phenotype-journey verify <manifest> --mode mock`.

Critical paths a session must know:
- Player.log: `$env:USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log`
- SavedSettings: `$env:USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/mods_config/WorldSphereMod.json` (snake_case folder)
- Game install: `C:/Program Files (x86)/Steam/steamapps/common/worldbox`
- Mod dest: `<install>/Mods/WorldSphereMod3D`
- Built DLL: `bin/Release/net48/WorldSphereMod3D.dll` (net48 — Mono-loadable)

## Where to make changes

| You want to… | Look in |
|---|---|
| Add a new render mode flag | `WorldSphereMod/Code/SavedSettings.cs` |
| Add a public API method | `WorldSphereAPI/WorldSphereAPI.cs` (external) + `WorldSphereMod/Code/WorldSphereAPI.cs` (internal). Update both. |
| Hook a new WorldBox method | Add `[HarmonyPatch]` types to a new file under `WorldSphereMod/Code/` and they'll be picked up by `Patcher.PatchAll()` in `Core.Patch`. |
| Add a per-frame driver | Drop a `MonoBehaviour` and `AddComponent` it to `Mod.Object` in `Mod.Init`. See `Voxel/VoxelFrameDriver` for the pattern. |
| Convert 2D coords to 3D | `Tools.To3D`, `Tools.To3DTileHeight`, `Tools.To2D`. Don't re-derive. |
| Get a tile's terrain height | `Tools.GetTileHeightSmooth`. |
| Add a 3D mesh draw | `Voxel/MeshInstanceBatcher.Submit(mesh, material, matrix, color)` then call `Flush()` once per frame. |
| Voxelize a sprite | `Voxel/VoxelMeshCache.Get(sprite)` — cached. |
| Change terrain / water / slope geometry | **The fork**, not the main mod: `External/Compound-Spheres/CompoundSpheres/HeightFieldRenderer.cs` (on `wsm3d/main`). Then rebuild `CompoundSpheres.dll` and copy it to `WorldSphereMod/Assemblies/`. |
| Feed terrain/water data to the fork | `Core.cs ConfigureHeightField` / `ConfigureWater` — adapter callbacks (height/color/texture/isWater/waterLevel/seabed). The main mod adapts WorldBox types → primitives; the fork knows no WorldBox types. |
| Drive the game headlessly (no clicks) | `Code/Bridge/BridgeActions.cs` + `BridgeServer.cs` — HTTP on `127.0.0.1:8766`: `/actions/new_world`, `/spawn_units`, `/camera`, `/set_speed`, `/select_tool`, `/use_tool`, `/world/state`, `/tools`. |
| Report / inspect a render failure | `Voxel/RenderErrorRegistry.cs` + `GET /diag/errors`; flip `RenderErrorProps` for in-world markers. Never silently fall back to a billboard — there are none. |
| Work on the GPU-compute base | `External/Compound-Spheres` `wsm3d/main` (GPU manager + shim + `.compute`); see `docs/adr/ADR-sota-gpu-compute-adoption.md`. |

## What's a fork-specific concern vs. upstream

If you're touching anything in `WorldSphereMod/Code/Voxel/`, the
`SavedSettings` v2 fields, the new `WorldSphereAPI` v2 methods
(`IsModel3D`, `RegisterCustomMesh`, `OnTimeOfDayChanged`), or the
`Directory.Build.props` portability layer — that's fork-specific, ship it.

If you're touching `Core.cs`, `QuantumSprites.cs`, `3DCamera.cs`,
`Effects.cs`, `Tools.cs`, `DimensionConverter.cs`, `General.cs`,
`TileMapToSphere.cs`, `CompoundSphereScripts.cs` — those are inherited
from upstream. Tread carefully. The mod has ~80 Harmony patches across
those files; changes can cascade.

## Pitfalls and surprises

- **No billboards — don't add a fallback quad.** The policy is
  voxel-or-invisible: if an object can't render as a real mesh it is culled,
  not drawn as a camera-facing sprite. `CrossedQuadMeshCache`/`Mesher`,
  `ImpostorBillboard`, the water-overlay stubs and the `TerrainSmoothing`
  stub are deleted. Don't resurrect them as a "safe fallback."
- **Terrain/water/slope are the fork's job.** Don't re-add a main-mod water
  surface or slope-quad overlay — that was the band-aid era that drifted
  (see `ADR-fork-terrain-water-slope.md`). Surface geometry is emitted by
  `HeightFieldRenderer` in the fork; the main mod only supplies data via
  `ConfigureHeightField`/`ConfigureWater`.
- **Submodule must stay on `wsm3d/main`, not detached upstream.** The fork
  was found detached at Melvin's upstream merge (`73a7b77`), which silently
  dropped `HeightFieldRenderer`/`FrustumCuller` and would have rebuilt a
  flat renderer + broken `Core.cs` compile. It is now re-attached to
  `wsm3d/main` (`9e69b64b`). If `git diff HEAD wsm3d/main` is non-empty or
  the checkout is detached, fix it before building the DLL.
- **Rigging is intentionally off.** `SkeletalAnimation` is force-disabled and
  actors emit a static voxel mesh (bind-pose scale mismatch caused broken /
  flickering skinned actors). Re-enabling needs centroid bind poses — don't
  just flip the flag.
- **Magenta actors = shader variant, not material.** Neon-magenta actors come
  from the `OpaqueVertexColor` `INSTANCING_ON` variant failing across Unity
  62f3→60f1. The fix is the buffer-driven `CompoundSphere.shader` (no
  `_Color` cbuffer). Bake with the matched **2022.3.60f1** at
  `E:/Unity/Hub/Editor/2022.3.60f1` — a version-mismatched bundle reintroduces it.
- **CompoundSpheres.dll is rebuilt from the fork, by hand.** The main
  `.csproj` references the prebuilt DLL by `HintPath`, not a project
  reference. After changing fork source you MUST rebuild the fork and copy
  `CompoundSpheres.dll` → `WorldSphereMod/Assemblies/` (mind the stale-DLL
  trap — confirm the new symbol is in the DLL, e.g. `strings | grep ConfigureWater`).
- **Z-displacement sentinel.** `Constants.ZDisplacement = 100` is used as a
  magic value to detect "this Vector3 was already converted to 3D space."
  Don't naively add height to a position without checking.
- **Cylindrical X-wrapping.** When `CurrentShape == 0` (the default), X
  coordinates wrap around the world. Use `Tools.Dist`/`Tools.WrappedDist`
  for any distance math, never raw `Vector3.Distance`.
- **Parallel render passes.** `ActorManager.precalculateRenderDataParallel`
  and `BuildingManager.precalculateRenderDataParallel` run on a worker
  pool. Anything you do in a Postfix on those needs to be thread-safe or
  to run synchronously after `Parallel.For` exits. Most Postfix code does
  run after, but be explicit.
- **Compute-shader gate.** `Mod.OnLoad` throws `IncompatibleHardwareException`
  if the GPU doesn't support instancing/compute/indirect-args. The mod
  icon turns red. The GPU-compute renderer base (`ADR-sota-gpu-compute-adoption.md`)
  hard-relies on compute support, so this gate stays. There is no
  billboard/impostor fallback any more (voxel-or-invisible) — incompatible
  hardware is simply unsupported.
- **AssetBundle paths.** Bundles live in `WorldSphereMod/AssetBundles/{win,
  linux,osx}/worldsphere`. The platform-specific files are binary blobs
  rebuilt from Unity 2022.3 — not editable by hand.
- **SavedSettings folder is snake_case.** NML's `Paths.ModsConfigPath` resolves to `mods_config/` on Windows (lowercase + underscore), NOT `ModsConfig/`. The CLI hardcodes the right path; the MCP server auto-discovers via glob.
- **CompoundSpheres.dll is a runtime dep, not stale.** `WorldSphereMod/Assemblies/CompoundSpheres.dll` (23KB) must stay shipped — `Mod.cs`, `Tools.cs`, `Core.cs`, `WaterRender.cs`, `TileMapToSphere.cs`, `CompoundSphereScripts.cs` all `using CompoundSpheres;`. Without it, NML's Roslyn compile fails with ~60 CS0246 errors.
- **NML Roslyn Compatibility.** `dotnet build` uses net48 Roslyn, but NML compiles the mod with Unity's embedded Roslyn, which is stricter. Known incompatibilities:
  - `.Length` on non-array types is treated as a method group.
  - `tiles_list.Length` breaks for that reason — use an explicit `WorldTile[]` local variable instead.
  - Always test with NML and check `Player.log` for `error CS` after launch.
- **NML Publicizer trap.** NML compiles against the original (non-publicized) WorldBox DLL, not publicized assemblies. Accessing private WorldBox fields requires reflection at runtime — `AccessTools.Field` / `Traverse` — even if your IDE shows them as accessible via a publicizer.
- **Harmony Prefix return-false kills ALL Postfixes.** In Harmony 2.x, a Prefix that returns `false` (skip original) also suppresses every Postfix on that method. If you need Postfix logic to still run, call the Postfix methods explicitly from within the Prefix.
- **SmoothLoader.add signature.** `SmoothLoader.add` takes `MapLoaderAction`, not `System.Action`. Passing a plain `Action` compiles in dotnet but fails at NML load time.
- **Large maps hang in 3D mode.** Maps larger than ~316x316 hang because `CompoundSpheres` renders all tiles per frame with no culling or chunking. This is a known upstream limitation — don't increase default map size until a chunked renderer is in place.
- **World.world.tiles doesn't exist.** Use `MapBox.instance.tiles_list` (or the locally cached array) to iterate tiles. `World.world.tiles` is a common guess that won't compile.
- **SaveManager.loadWorld may not resolve in Harmony patches.** The method name can be obfuscated or renamed across WorldBox versions. Verify the exact method signature in the target DLL before writing a `[HarmonyPatch]` for it.
- **Never squash-merge the megabase/integration branch** — squash dropped the SafeShaders crash-fix and reverted the submodule pointer (see `docs/squash-merge-postmortem.md`). Use merge-commit or fast-forward.

## When you're done with a phase

1. Toggle the corresponding `SavedSettings` flag to `true` by default.
2. Update `README.md`'s phase table from `planned` / `scaffolding` to
   `landed`.
3. Update `docs/HANDOFF.md`'s "Recommended next steps" list.
4. Commit, push, mark the PR ready for review (still draft until then),
   and let CodeRabbit do a pass.
