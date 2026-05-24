# Handoff — pick up cold

Canonical "next session starts here" doc for WorldSphereMod3D.

**Last updated:** 2026-05-24 (`3bf2ad9`; `f5a93ae` or newer after pull)

## TL;DR

Hard fork of `MelvinShwuaner/WorldSphereMod` that finishes the 3D conversion
of WorldBox: actors, buildings, foliage, water, lighting, animation, UI, sky,
particles and LOD now have real 3D code paths sitting behind per-phase flags
in `SavedSettings`. Phase 0 hardening is documented as landed: task/journey
gates are wired, API capability discovery is exposed, the profiler overlay is
opt-in, and journey capture tooling is in place. The remaining documented gaps
are live capture validation and strict-assets verification for journeys. The
current code defaults are summarized below; they are not the same thing as live
smoke-test verification. Build is local-only (needs WorldBox reference DLLs);
CI builds only the Unity-free API project (see `docs/ci-mod-compile-gap.md`).

## Where things are

| Thing | Location |
|---|---|
| Active branch | `claude/research-ultraplan-fork-DdgI5` |
| Open PR (#1) | https://github.com/KooshaPari/WorldSphereMod/pull/1 — **OPEN**, **MERGEABLE**; blocking CI green except Vercel rate limit |
| Release tag (remote) | **`v2.0.0-beta.6`** — [release](https://github.com/KooshaPari/WorldSphereMod/releases/tag/v2.0.0-beta.6) |
| Offline test matrix | **482 pass / 3 skip** (485 total) — Unit 151 (+ 3 skip), Integration 69, E2E 262 |
| Cold-start orientation | `CLAUDE.md` |
| Full 10-phase plan | `docs/PLAN.md` |
| Per-phase architectures | `docs/phase{2..10}-architecture.md` |
| Phase 1 review (fixes already applied) | `docs/phase1-review.md` |
| Smoke test index (phases 1–10) | `docs/smoke-test-index.md` |
| Phase 1 smoke-test checklist | `docs/smoke-test-phase1.md` |
| Phase 2 smoke-test checklist | `docs/smoke-test-phase2.md` |
| Phase 3 smoke-test checklist | `docs/smoke-test-phase3.md` |
| Phase 4 smoke-test checklist | `docs/smoke-test-phase4.md` |
| Phase 5 smoke-test checklist | `docs/smoke-test-phase5.md` |
| Phase 6 smoke-test checklist | `docs/smoke-test-phase6.md` |
| Phase 7 smoke-test checklist | `docs/smoke-test-phase7.md` |
| Phase 8 smoke-test checklist | `docs/smoke-test-phase8.md` |
| Phase 9 smoke-test checklist | `docs/smoke-test-phase9.md` |
| Phase 10 smoke-test checklist | `docs/smoke-test-phase10.md` |
| `render_data` field map | `docs/render-data-fields.md` |
| Phase 5 prep (Unity 2022.3 + Compound-Spheres-3D submodule) | `docs/phase5-prep.md` |
| Settings | `WorldSphereMod/Code/SavedSettings.cs` |
| Build portability layer | `Directory.Build.props` (env: `WORLDBOX_PATH`) |
| CI | `.github/workflows/build.yml` (API gate; mod compile gap in `docs/ci-mod-compile-gap.md`) |
| Nightly regression | `.github/workflows/nightly.yml` — reuses `live-verify-gate.yml` offline stages, then lint/stats extras |
| Live-verify gate (reusable) | `.github/workflows/live-verify-gate.yml` |
| Dependency/security audit gate | `.github/workflows/dependency-security-audit.yml` |
| Install / uninstall | `Tools/install.ps1`, `Tools/uninstall.ps1` |
| Voxel pipeline | `WorldSphereMod/Code/Voxel/` |
| Procedural buildings | `WorldSphereMod/Code/ProcGen/` |
| Foliage + walls + overlays | `WorldSphereMod/Code/Foliage/` |
| Mesh water | `WorldSphereMod/Code/Water/` |
| Lighting / sun / sky / TOD | `WorldSphereMod/Code/Lighting/` |
| Skeletal animation | `WorldSphereMod/Code/Rig/` |
| Worldspace UI | `WorldSphereMod/Code/Worldspace/` |
| Particles / decals / PostFX | `WorldSphereMod/Code/Fx/` |
| LOD / impostor / culler | `WorldSphereMod/Code/LOD/` |
| Profiler | `WorldSphereMod/Code/Perf/` |
| World-unload sink | `WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs` |
| Live verification (programmatic + agentic gates) | `docs/live-verification.md` — canonical live proof bundle: [`#canonical-live-proof-bundle`](live-verification.md#canonical-live-proof-bundle) |

## What's shipped (per phase)

| Phase | State | Notes |
|---|---|---|
| 0  Fork plumbing                       | ✅ | Build portability, GUID `worldsphere3d.fork`, settings v2, API v2, task/journey gates, capability discovery, opt-in profiler overlay, journey capture tooling; ADR-0007 conditional patch dispatch **landed scaffold** (`PhasePatchGate` + E2E invariants; ADR status still Proposed until per-phase smoke) |
| 1  Voxel actors + buildings            | ✅ | Current code default: `VoxelEntities = true`. Smoke-test verification is documented elsewhere and should not be inferred from the default alone. |
| 2  Procedural building meshes          | ✅ | Current code default: `ProceduralBuildings = true`. |
| 3a Crossed-quad foliage                | ✅ | Current code default: `CrossedQuadFoliage = true`. |
| 3b Surface overlays + walls            | ✅ | `WorldTilemap.renderTile` Prefix + `drawWallType` Prefix wired |
| 4  Mesh water                          | ✅ | Current code default: `MeshWater = true`. |
| 5  Sun + cascaded shadows              | ✅ | Current code defaults: `HighShadows = true`, `HdrSkybox = true`, `ColorGradingLut = true`. |
| 6  Skeletal animation                  | ✅ | Current code default: `SkeletalAnimation = true`. |
| 7  Worldspace UI                       | ✅ | Current code defaults: `WorldspaceUI = true`, `WorldspaceLabel3D = true`. |
| 8  Day/night + sky + fog               | ✅ | Current code default: `DayNightCycle = true`; `FogDensity = 0.05f`. |
| 9  Particles + decals + PostFX         | ✅ | Current code defaults: `ParticleEffects = true`, `PostFX = true`, `SSAOEnabled = true`, `SSGIEnabled = false`. |
| 10 LOD + impostor fallback             | ✅ | Current code defaults: `LODScale = 0.5f`, `WaterDetail = 1.0f`, `FoliageDensity = 1.0f`. |

## Current defaults matrix

These are the live `SavedSettings` defaults in `WorldSphereMod/Code/SavedSettings.cs` as of this doc update. They describe startup state, not proof that every phase has been re-smoke-tested in this session.

| Setting | Default | Phase | Note |
|---|---|---|---|
| `VoxelEntities` | `true` | 1 | Default-on voxel actors/items/projectiles |
| `ProceduralBuildings` | `true` | 2 | Default-on building meshes |
| `CrossedQuadFoliage` | `true` | 3a | Default-on crossed-quad foliage |
| `BiomeBlending` | `true` | n/a | Terrain polish |
| `MeshWater` | `true` | 4 | Default-on mesh water (Phase 4-lite) |
| `WorldspaceHealth3D` | `true` | 7 | Worldspace HP bar style |
| `MountainSlopeSmoothing` | `true` | n/a | Terrain polish |
| `HighShadows` | `true` | 5 | Default-on shadow cascades |
| `HdrSkybox` | `true` | 5 | Current live default |
| `ColorGradingLut` | `true` | 5 | Current live default |
| `SkeletalAnimation` | `true` | 6 | Default-on skeletal path |
| `WorldspaceUI` | `true` | 7 | Default-on worldspace UI |
| `WorldspaceLabel3D` | `true` | 7 | Default-on 3D labels |
| `DayNightCycle` | `true` | 8 | Default-on TOD driver |
| `FogDensity` | `0.05f` | 8 | Current live default |
| `PostFX` | `true` | 9 | Current live default |
| `SSAOEnabled` | `true` | 9 | Default-on SSAO |
| `SSAOQuality` | `Medium` | 9 | Current live default |
| `SSGIEnabled` | `false` | 9 | Default-off SSGI |
| `ParticleEffects` | `true` | 9 | Default-on particle effects |
| `WeatherRain` | `true` | n/a | Weather default |
| `WeatherSnow` | `false` | n/a | Weather default |
| `WeatherLightning` | `false` | n/a | Weather default |
| `LODScale` | `0.5f` | 10 | LOD tuning default |
| `WaterDetail` | `1.0f` | 10 | LOD tuning default |
| `FoliageDensity` | `1.0f` | 10 | LOD tuning default |
| `ProfilerDump` | `true` | 0 | Diagnostic overlay default |

## Current defaults by category

### Default-on / currently enabled

- `VoxelEntities` — Phase 1
- `ProceduralBuildings` — Phase 2
- `CrossedQuadFoliage` — Phase 3a/3b
- `MeshWater` — Phase 4
- `HighShadows` — Phase 5
- `HdrSkybox` — Phase 5b
- `ColorGradingLut` — Phase 5b
- `SkeletalAnimation` — Phase 6
- `WorldspaceUI` — Phase 7
- `WorldspaceLabel3D` — Phase 7
- `DayNightCycle` — Phase 8
- `PostFX` — Phase 9
- `SSAOEnabled` — Phase 9
- `ParticleEffects` — Phase 9 (decals + bursts)
- `BiomeBlending` — terrain polish
- `WorldspaceHealth3D` — worldspace HP bar style
- `MountainSlopeSmoothing` — terrain polish
- `WeatherRain` — weather
- `ProfilerDump` — diagnostic overlay (default-on)
- `FogDensity` — `0.05f` in live settings

### Default-off / opt-in

- `SSGIEnabled` — Phase 9
- `WeatherSnow` — weather
- `WeatherLightning` — weather

### CI dependency audit gate

- `docs/package-lock.json` currently reports moderate `npm audit` advisories
  for `esbuild`, `vite`, and `vitepress` with no available upstream fix.
  The new dependency-security audit workflow documents that waiver and keeps
  the docs job non-blocking until the dependency chain can be updated.
- `Tools/journey-records` is audited with `cargo-audit` in CI.
- NuGet vulnerability scanning is blocking and should fail the gate if any
  project reports vulnerable packages.

### Diagnostic / non-phase settings

- `SSAOQuality` — Phase 9 quality enum (`Medium`)
- `LODScale` — Phase 10 LOD tuning (`0.5f`)
- `WaterDetail` — Phase 10 LOD tuning (`1.0f`)
- `FoliageDensity` — Phase 10 LOD tuning (`1.0f`)

## Local build + install

```powershell
$env:WORLDBOX_PATH = "C:/Program Files (x86)/Steam/steamapps/common/worldbox"
dotnet build WorldSphereMod.csproj -c Release    # ~5s, 0 errors
./Tools/install.ps1                              # builds + copies to <WorldBox>/Mods/WorldSphereMod3D/
```

NeoModLoader compiles `Code/*.cs` at runtime, so install copies sources plus
`Assemblies/CompoundSpheres.dll`, `AssetBundles/`, `GameResources/`,
`Locales/`, `mod.json`. CI cannot compile a loadable mod DLL without WorldBox
refs (`docs/ci-mod-compile-gap.md`); it gates `WorldSphereAPI.csproj` and
workflow/manifest invariants via E2E tests.

**Compound-Spheres submodule** — `External/Compound-Spheres` is pinned to upstream
`MelvinShwuaner/Compound-Spheres` `main` at SHA **`73a7b77`** (we cannot push fork
fixes without write access). The mod still ships the vendored `CompoundSpheres.dll`,
not a submodule build. Optional local patch `dd78b11` (null guard on
`Material.SetTexture` in `SphereManager` init) or an equivalent Harmony patch —
see `docs/ci-mod-compile-gap.md` § "`External/Compound-Spheres` submodule".

**Push hygiene** — push the parent repo with
`git push --no-recurse-submodules origin HEAD` so submodule pointers are not
accidentally updated on remote.

## In-game smoke test (gates Phases 2, 5, 8)

The user-driven gate. See `docs/smoke-test-phase1.md` for the full checklist.
Short form:

1. `./Tools/install.ps1` → launch WorldBox → enable mod in NeoModLoader.
2. Confirm vanilla-3D path is regression-clean with `VoxelEntities = false` for the smoke-test gate, even though the live default is `true`.
3. Flip `VoxelEntities = true` in the in-game settings tab, generate a
   500-unit kingdom, sweep camera 360°.
4. Verify: voxel actors render, no body topple while walking (review fix #4),
   tail batches tinted correctly (review fix #1), no flicker on 1023-unit
   boundaries.
5. Flip `ProceduralBuildings = true` and verify heuristic produces reasonable
   geometry on vanilla `BuildingAsset` set. Tweak `BuildingMeshGen` thresholds
   if false-positive gables/hipped roofs appear.
6. Capture before/after into `docs/screenshots/phase-{1,2,...}-*.png`.

## What's blocked

- **Phase 2 procedural buildings in-game smoke** — PlayCUA capture + telemetry passed for `ProceduralBuildings` with `Tools/wsm3d-playcua/sample-scenarios/phase-2-procedural-buildings.yaml`; generated proof: `artifacts/phase-2-procedural-buildings/buildings.png`. Visual/vision approval and strict journey capture remain separate proof.
- **Cloud crossed-quad in-game smoke** — PlayCUA capture + telemetry passed for `CloudCrossedQuadRender` with `Tools/wsm3d-playcua/sample-scenarios/phase-3b-cloud-crossed-quad.yaml`; generated proof: `artifacts/phase-3b-cloud-crossed-quad/foliage.png` and `artifacts/phase-3b-cloud-crossed-quad/clouds.png`. Visual/vision approval and strict journey capture remain separate proof.
- **Unity 2022.3 install** — required to bake `VoxelLit.shader`, `WaterGerstner.shader`, `ProceduralSky.shader` into AssetBundles for Phases 4, 5, 8.
- **Live verification (agentic tier)** — `.github/workflows/live-verify-gate.yml` runs offline programmatic stages (`dotnet test` + journey mock via `Tools/wsm-live-verify.ps1`, report `Tools/.reports/live-verify-latest.json`). **Nightly** (`.github/workflows/nightly.yml`) calls the same reusable workflow for offline stages before lint/stats extras. Full agentic tier on a Windows desktop requires **WorldBox running + bridge on `127.0.0.1:8766` + OmniRoute** for vision: `pwsh Tools/wsm-live-verify.ps1 -Live -Vision`. See `docs/live-verification.md`.
- Current live status: fresh `pwsh Tools/wsm-live-verify.ps1 -Live -SkipOffline` passed with bridge-health-vision and bridge-save-load-smoke; pre/post health + telemetry passed without vision. Full `-Live -Vision`, strict journey capture, and a true `load_save` transition remain open/manual/partial, with `load_save` still sometimes skipped on `non-dict response: null`.
- **PlayCUA sample scenarios (live runs)** — 13 YAML files in `Tools/wsm3d-playcua/sample-scenarios/` (see list below). E2E guards in `PlaycuaSampleScenarioInvariantsTests.cs`; OmniRoute vision steps need a running game + bridge (`127.0.0.1:8766`).
- **OmniRoute API key** (optional) — for PlayCUA screenshot vision via `OMNROUTE_API_KEY` + `OMNROUTE_VISION_COMBO` (or `ANTHROPIC_API_KEY` fallback). Journey mock and offline live-verify gate work without either.

## Recommended next steps

1. Smoke-test Phase 2 procedural buildings the same way Phase 1 was proven: toggle `ProceduralBuildings`, capture screenshots, and diff against canonical output.
2. Implement ADR-0006 (Phase 6 Step 9 DrawProceduralIndirect skinning) — 2–3 day estimate if we decide to replace the visible skinned-mesh path with GPU-resident batching later.
3. Install Unity 2022.3 + clone `Compound-Spheres-3D` submodule; bake the four shaders into platform AssetBundles under `WorldSphereMod/AssetBundles/{win,linux,osx}/worldsphere`.
4. Flip ADR-0007 status to **Accepted** after safe-min / per-phase toggle smoke confirms init gate matches runtime `PhasePatchManager` behavior.

## PlayCUA sample scenarios (13 YAML)

All paths under `Tools/wsm3d-playcua/sample-scenarios/`:

| File | Purpose |
|---|---|
| `bridge-health-vision.yaml` | Bridge `/health` + vision screenshot smoke |
| `bridge-save-load-smoke.yaml` | Pre/post health, optional `load_save`, manual notes |
| `phase-1-voxel-actors.yaml` | `VoxelEntities` toggle + voxel vision asserts |
| `phase-2-procedural-buildings.yaml` | `ProceduralBuildings` toggle + building vision |
| `phase-3-crossed-quad-foliage.yaml` | Crossed-quad foliage toggle + screenshots |
| `phase-3b-cloud-crossed-quad.yaml` | `CrossedQuadFoliage` + cloud crossed-quad vision |
| `phase-4-mesh-water.yaml` | Mesh water phase smoke |
| `phase-5-high-shadows.yaml` | High shadows + HDR skybox |
| `phase-6-skeletal-animation.yaml` | Skeletal animation phase smoke |
| `phase-7-worldspace-ui.yaml` | Worldspace UI / labels |
| `phase-8-day-night.yaml` | Day/night cycle + sky |
| `phase-9-postfx-particles.yaml` | PostFX + particles |
| `phase-10-lod.yaml` | LOD / impostor tuning |

(`smoke-test.sh` is a helper script, not counted in the 13.)

## Dev tooling

- **CLI:** `pwsh Tools/wsm3d.ps1 help` — 13 subcommands (build, install, launch, relaunch, log, toggle, journey capture, etc.).
- **Slash commands:** `/wsm-status`, `/wsm-validate-all`, `/wsm-build`, `/wsm-install`, `/wsm-relaunch`, `/wsm-log`, `/wsm-toggle`, `/wsm-screenshot`, `/wsm-journey-run`, `/wsm-doctor`.
- **MCP:** `Tools/wsm3d-mcp/` — Python FastMCP with 18 tools, auto-registered via `.claude/mcp-servers.json`.
- **Journey gate:** `.github/workflows/journeys-gate.yml` — OCR-assertion DSL; verify with `phenotype-journey verify <manifest> --mock`. Live capture remains the final proof step; entry point: `docs/live-verification.md`.
- **Live-verify gate (CI):** `.github/workflows/live-verify-gate.yml` — offline `dotnet test` + journey mock (stages 1–2 of `Tools/wsm-live-verify.ps1`; **482 pass / 3 skip**, 485 total locally). Reused by **nightly** (`nightly.yml` → `live-verify-offline` job). Full harness: `pwsh Tools/wsm-live-verify.ps1` (add `-Live -Vision` for PlayCUA + SSIM + OmniRoute vision on a desktop with WorldBox + bridge).
- **ADR-0007 (conditional patch dispatch):** Landed scaffold — `PhasePatchGate.ShouldApplyHarmonyPatch` wired from `Core.Patch()`; `docs/adr/ADR-0007-conditional-patch-dispatch.md` remains **Proposed** until acceptance smoke. E2E: `ConditionalPatchDispatchInvariantsTests`.
- **Live verify:** `docs/live-verification.md` — programmatic (`dotnet test`, journey mock, optional SSIM ≥ 0.95) vs agentic (`wsm3d-playcua` sample scenarios, OmniRoute combo, bridge save/load checklist).

When you need a release or handoff bundle, use the canonical checklist in [`docs/live-verification.md`](live-verification.md#canonical-live-proof-bundle). It is the single place that names the required live verifier command, `Tools/.reports/live-verify-latest.json`, PlayCUA artifacts, phase-preview SSIM fixtures, and the explicit skip/offline note when `live-playcua-ssim` does not run.

## Recent commits (7 most recent)

```
3bf2ad9 docs: add canonical live proof bundle checklist
1bf9540 docs: HANDOFF release tag v2.0.0-beta.6
9aaa2dd docs: point handoff docs at v2.0.0-beta.6 release
c73f85b chore(release): bump to v2.0.0-beta.6
05961b6 test(e2e): building style procgen opt-in invariants
32bd8bd test(e2e): terrain biome blending invariants
4ddddec test(e2e): voxel frame driver postfx reconciler invariants
57ab9a7 test(e2e): phase 3b tile overlay and wall patch invariants
```

## Important caveats / non-obvious gotchas

- **Cylindrical X-wrap.** When `CurrentShape == 0` (cylinder), X coords wrap.
  Use `Tools.Dist` / `Tools.WrappedDist`, never raw `Vector3.Distance`.
- **Z-displacement sentinel.** `Constants.ZDisplacement = 100` flags
  "already converted to 3D space" on a `Vector3`. Don't naively add height.
- **Parallel render passes.** `ActorManager.precalculateRenderDataParallel`
  and `BuildingManager.precalculateRenderDataParallel` run on a worker pool.
  Postfix code runs after `Parallel.For` exits, but be explicit.
- **Compute-shader gate.** `Mod.OnLoad` throws `IncompatibleHardwareException`
  if the GPU fails instancing/compute/indirect-args. Phase 10's
  `ImpostorBillboard` is the fallback path — don't relax the gate.
- **Settings migration.** `Version` is `"2.0"`. v1.5 prefs migrate forward
  in `Core` settings load; don't bump the version casually.
- **World-unload sink.** Single Harmony Prefix on `Core.Sphere.Finish`
  drains every fork cache (`VoxelMeshCache`, `ProcGenCache`,
  `CrossedQuadMeshCache`, `RigCache` + RigDriver GPU buffers, `LOD`
  hysteresis, `WorldUIRenderer`, batcher buckets). Add new caches here.
- **`MapBox.world_time` may not exist.** `TimeOfDay.cs` reflection-probes
  the field at startup and falls back to autonomous mode if absent. Don't
  hard-bind to it.
- **GUID is `worldsphere3d.fork`.** Co-installable with upstream
  `WorldSphereMod`. Enable only one at a time in NeoModLoader.
- **Compound-Spheres pin.** Submodule stays at upstream `main` SHA **`73a7b77`**;
  fork commit `dd78b11` is optional locally only (`docs/ci-mod-compile-gap.md`).

## Where to look for what

| You want to… | Look in |
|---|---|
| "voxel" / actor or item meshes | `WorldSphereMod/Code/Voxel/` |
| "procgen" / building meshes | `WorldSphereMod/Code/ProcGen/` |
| "foliage" / trees, bushes, rocks, overlays, walls | `WorldSphereMod/Code/Foliage/` |
| "water" / mesh water surface | `WorldSphereMod/Code/Water/` |
| "rig" / skeletal animation | `WorldSphereMod/Code/Rig/` |
| "lighting" / sun, shadows, sky, TOD | `WorldSphereMod/Code/Lighting/` |
| "ui" / nameplate, HP bar, popups, selection | `WorldSphereMod/Code/Worldspace/` |
| "fx" / particles, decals, PostFX | `WorldSphereMod/Code/Fx/` |
| "lod" / culling, impostor, tiers | `WorldSphereMod/Code/LOD/` |
| "perf" / frame profiler | `WorldSphereMod/Code/Perf/` |
| Add a new flag | `WorldSphereMod/Code/SavedSettings.cs` |
| Add a public API call | `WorldSphereAPI/WorldSphereAPI.cs` + `WorldSphereMod/Code/WorldSphereAPI.cs` |
| Convert 2D ↔ 3D coords | `Tools.To3D`, `Tools.To3DTileHeight`, `Tools.To2D` |
| Get tile height | `Tools.GetTileHeightSmooth` |
| Submit a mesh for instanced draw | `MeshInstanceBatcher.Submit` + `Flush` |
| Voxelize a sprite | `VoxelMeshCache.Get(sprite)` |
| Inherited-from-upstream files (tread carefully) | `Core.cs`, `QuantumSprites.cs`, `3DCamera.cs`, `Effects.cs`, `Tools.cs`, `DimensionConverter.cs`, `General.cs`, `TileMapToSphere.cs`, `CompoundSphereScripts.cs` |

## Branch / PR hygiene

- Push to `claude/research-ultraplan-fork-DdgI5`, not `main`.
- Use `git push --no-recurse-submodules origin HEAD` (submodule pinned at `73a7b77`).
- **PR #1** is OPEN and MERGEABLE; all blocking CI gates green except Vercel deploy rate limit (non-blocking).
- Pre-merge checklist: [`docs/MERGE_CHECKLIST.md`](MERGE_CHECKLIST.md).
- One PR per phase; commits within a phase can be incremental.
- After a phase smoke-tests clean: flip its `SavedSettings` flag default,
  update the README phase table, update this doc's "What's shipped" row.
