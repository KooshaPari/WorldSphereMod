# Handoff — pick up cold

Canonical "next session starts here" doc for WorldSphereMod3D.

**Last updated:** 2026-05-28 (`117746e` on `feat/phase-7-ui-kickoff`; `main` at `4efa128` after PR #7 squash-merge)

**Active branch:** `feat/phase-7-ui-kickoff` — Phase 7 worldspace UI kickoff ([`docs/phases/phase-7-worldspace-ui.md`](phases/phase-7-worldspace-ui.md)). Synced with `origin/main` after [PR #7](https://github.com/KooshaPari/WorldSphereMod/pull/7) landed (`4efa128` — automation, PlayCUA gates, live-verify harness).

**Latest `do-all-latest` (desk, vision off):** PlayCUA **passed on 1st attempt** (`run-all`, `-VisionBackend off`); **`live-verify-live`** and **`audit-tick`** stages failed. OmniRoute funnel (`https://omniroute-a6e82363-1.tail2b570.ts.net/v1`) often times out from the desk — use `-SkipLive` / vision-off PlayCUA for laptop-only loops; see [OmniRoute (kooshas-laptop)](#omniroute-kooshas-laptop). Report: `Tools/.reports/do-all-latest.json`.

Recent validation:

- **Runtime smoke test PASS** (2026-05-28): 8/8 runtime tests pass, frame time 32ms (30 FPS), 46 visible voxel actors on kingdom, heightfield terrain active, biome blending active, slope smoothing with MPB push verified.
- Shader bake pipeline is functional via headless `Tools/bake-shaders.ps1` (Unity `-batchmode -nographics`; **not** a `wsm3d.ps1` subcommand).
- `wsm3d-shaders` bundle manifest lists **10 shaders** (rebaked 2026-05-26; GerstnerWater depth pass in `0fe30b1`). Runtime still loads **3** via `Core.Sphere.SafeShaders` only — expansion blocked until in-game proof (see [SafeShaders human gate](#safeshaders-human-gate)).

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
| Active branch | `feat/phase-7-ui-kickoff` — Phase 7 worldspace UI ([`docs/phases/phase-7-worldspace-ui.md`](phases/phase-7-worldspace-ui.md)) |
| Merged PR (#7) | https://github.com/KooshaPari/WorldSphereMod/pull/7 — **MERGED** → `main` @ `4efa128` (automation, PlayCUA gates, live-verify harness) |
| Integration branch | `claude/research-ultraplan-fork-DdgI5` (historical; merge topic worktrees here before `main`) |
| Release tag (remote) | **`v2.0.0-beta.6`** — [release](https://github.com/KooshaPari/WorldSphereMod/releases/tag/v2.0.0-beta.6) |
| Offline test matrix | **525 pass / 3 skip** (528 total) — Unit 154 (+ 3 skip), Integration 69, E2E 301 pass / 1 skip (302 total, NML compat) |
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

## Phenotype shared packages

These shared packages are extracted dependencies that this repo now consumes
instead of carrying the underlying implementation inline.

1. `phenotype-postfx` (`C:/Users/koosh/Dev/phenotype-postfx`) — BRP
   post-processing stack extracted from `WSM3DPostStack`.
2. `phenotype-voxel` (`C:/Users/koosh/Dev/phenotype-voxel`) — shared voxel
   substrate with depth extrusion, instancing, and shape registry upstreamed
   from WSM3D.

## What's landed in code (runtime unverified)

| Phase | State | Notes |
|---|---|---|
| 0  Fork plumbing                       | code landed | Build portability, GUID `worldsphere3d.fork`, settings v2, API v2, task/journey gates, capability discovery, opt-in profiler overlay, journey capture tooling; ADR-0007 conditional patch dispatch **Accepted** (`PhasePatchGate` + E2E invariants; phase 1/2 PlayCUA smoke). |
| 1  Voxel actors + buildings            | code present, runtime unverified | Current code default: `VoxelEntities = true`. Triage says Harmony patches are still not applying in-game, so do not treat the default as proof. |
| 2  Procedural building meshes          | code present, runtime unverified | Current code default: `ProceduralBuildings = false`. |
| 3a Crossed-quad foliage                | code present, runtime unverified | Current code default: `CrossedQuadFoliage = true`. |
| 3b Surface overlays + walls            | code present, runtime unverified | `WorldTilemap.renderTile` Prefix + `drawWallType` Prefix wired. |
| 4  Mesh water                          | code present, runtime unverified | Current code default: `MeshWater = false`. |
| 5  Sun + cascaded shadows              | code present, runtime unverified | Current code defaults: `HighShadows = false`, `HdrSkybox = false`, `ColorGradingLut = false`. |
| 6  Skeletal animation                  | code present, runtime unverified; **disable-gate PROVEN** | Current code default: `SkeletalAnimation = false`. Core.LoadSettings force-overrides to `false` on every load (Core.cs L70 + L79). ActorVoxelEmit.EmitVoxels gates BOTH `ResolveRigType` and `RigDriver.SubmitSkinnedActor` behind `if (Core.savedSettings.SkeletalAnimation && tier != LodTier.Impostor)` (VoxelRender.cs L630). Invariant locked by `SkeletalAnimationDisabledGateTests` (E2E). DIAG-SUBMIT `skel(attempt=0 ok=0 fail=0)` is the runtime confirmation. |
| 7  Worldspace UI                       | code present, runtime unverified | Current code defaults: `WorldspaceUI = false`, `WorldspaceLabel3D = false`. |
| 8  Day/night + sky + fog               | code present, runtime unverified | Current code default: `DayNightCycle = false`; `FogDensity = 0.05f`. |
| 9  Particles + decals + PostFX         | code present, runtime unverified | Current code defaults: `ParticleEffects = false`, `PostFX = false`, `SSAOEnabled = false`, `SSGIEnabled = false`. |
| 10 LOD + impostor fallback             | code present, runtime unverified | Current code defaults: `LODScale = 0.5f`, `WaterDetail = 1.0f`, `FoliageDensity = 1.0f`. |

## Current defaults matrix

These are the live `SavedSettings` defaults in `WorldSphereMod/Code/SavedSettings.cs` as of this doc update. They describe startup state, not proof that every phase has been re-smoke-tested in this session.

| Setting | Default | Phase | Note |
|---|---|---|---|
| `VoxelEntities` | `true` | 1 | Default-on voxel actors/items/projectiles |
| `ProceduralBuildings` | `false` | 2 | Default-off building meshes |
| `CrossedQuadFoliage` | `true` | 3a | Default-on crossed-quad foliage |
| `BiomeBlending` | `false` | n/a | Terrain polish |
| `MeshWater` | `false` | 4 | Default-off mesh water (Phase 4-lite) |
| `WorldspaceHealth3D` | `false` | 7 | Worldspace HP bar style |
| `MountainSlopeSmoothing` | `false` | n/a | Terrain polish |
| `HighShadows` | `false` | 5 | Default-off shadow cascades |
| `HdrSkybox` | `false` | 5 | Current live default |
| `ColorGradingLut` | `false` | 5 | Current live default |
| `SkeletalAnimation` | `false` | 6 | Default-off skeletal path |
| `WorldspaceUI` | `false` | 7 | Default-off worldspace UI |
| `WorldspaceLabel3D` | `false` | 7 | Default-off 3D labels |
| `DayNightCycle` | `false` | 8 | Default-off TOD driver |
| `FogDensity` | `0.05f` | 8 | Current live default |
| `PostFX` | `false` | 9 | Current live default |
| `SSAOEnabled` | `false` | 9 | Default-off SSAO |
| `SSAOQuality` | `Medium` | 9 | Current live default |
| `SSGIEnabled` | `false` | 9 | Default-off SSGI |
| `BloomEnabled` | `false` | 9 | Default-off bloom (BRP shader shipped) |
| `ACESTonemapping` | `true` | 9 | Default-on ACES filmic tonemap |
| `ParticleEffects` | `false` | 9 | Default-off particle effects |
| `WeatherRain` | `false` | n/a | Weather default |
| `WeatherSnow` | `false` | n/a | Weather default |
| `WeatherLightning` | `false` | n/a | Weather default |
| `LODScale` | `0.5f` | 10 | LOD tuning default |
| `WaterDetail` | `1.0f` | 10 | LOD tuning default |
| `FoliageDensity` | `1.0f` | 10 | LOD tuning default |
| `ProfilerDump` | `false` | 0 | Diagnostic overlay default-off |

## Current defaults by category

### Default-on / currently enabled

- `VoxelEntities` — Phase 1
- `CrossedQuadFoliage` — Phase 3a/3b
- `ACESTonemapping` — Phase 9

### Default-off / opt-in

- `ProceduralBuildings` — Phase 2
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
- `ProfilerDump` — diagnostic overlay (default-off)
- `FogDensity` — `0.05f` in live settings
- `SSGIEnabled` — Phase 9
- `BloomEnabled` — Phase 9
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

- **Phase 2 procedural buildings in-game smoke** — PlayCUA capture + telemetry passed for `ProceduralBuildings` with `Tools/wsm3d-playcua/sample-scenarios/phase-2-procedural-buildings.yaml`, but `docs/issue-triage.md` says the current visual evidence is unreliable and no phase has been visually verified in WorldBox. Re-run PlayCUA + `sync-playcua-screenshots.ps1` after the Win32Capture `worldbox_window` fix; treat pre-fix artifact paths as untrusted until refreshed.
- **Cloud crossed-quad in-game smoke** — PlayCUA capture + telemetry passed for `CloudCrossedQuadRender` with `Tools/wsm3d-playcua/sample-scenarios/phase-3b-cloud-crossed-quad.yaml`, but `docs/issue-triage.md` says historical screenshots captured the wrong window. Re-run capture + sync; confirm step details show `capture_target: worldbox_window`.
- **Unity 2022.3 install** — required to bake `VoxelLit.shader`, `WaterGerstner.shader`, `ProceduralSky.shader` into AssetBundles for Phases 4, 5, 8.
- **Live verification (agentic tier)** — `.github/workflows/live-verify-gate.yml` runs offline programmatic stages (`dotnet test` + journey mock via `Tools/wsm-live-verify.ps1`, report `Tools/.reports/live-verify-latest.json`). **Nightly** (`.github/workflows/nightly.yml`) calls the same reusable workflow for offline stages before lint/stats extras. Full agentic tier on a Windows desktop requires **WorldBox running + bridge on `127.0.0.1:8766` + OmniRoute** for vision: `pwsh Tools/wsm-live-verify.ps1 -Live -Vision`. See `docs/live-verification.md`.
- Visual/vision approval and strict journey capture remain separate proof from PlayCUA capture + telemetry. phase SSIM still need inference/OmniRoute vision backend (Fireworks `fpk_*` keys return HTTP 403).
- Current live status: `docs/issue-triage.md` says the runtime is still broken: Harmony patches are not applying, historical screenshot PNGs captured the wrong window (tooling fixed 2026-05-26 — `Win32Capture` now sets `capture_target: worldbox_window`), and no phase has been visually verified in actual WorldBox gameplay with post-fix captures. Until Harmony + refreshed screenshots are fixed, treat PlayCUA pass counts and stale artifact PNGs as automation claims, not proof of shipped visuals. `BridgeLoadSaveHooks` must patch `loadWorld(string, bool)` explicitly or `Core.Init` fails (loading screen stall). Shader **rebake is done** (10 shaders in bundle); runtime still loads **3** via `SafeShaders` — see [SafeShaders human gate](#safeshaders-human-gate).
- **PlayCUA sample scenarios (live runs)** — 13 YAML files in `Tools/wsm3d-playcua/sample-scenarios/` (see list below). E2E guards in `PlaycuaSampleScenarioInvariantsTests.cs`; OmniRoute vision steps need a running game + bridge (`127.0.0.1:8766`).
- **OmniRoute API key** (optional) — for PlayCUA screenshot vision via `OMNROUTE_API_KEY` + `OMNROUTE_VISION_COMBO` (or `ANTHROPIC_API_KEY` fallback). Journey mock and offline live-verify gate work without either.

## Phase 7 branch goals (`feat/phase-7-ui-kickoff`)

Kickoff doc: [`docs/phases/phase-7-worldspace-ui.md`](phases/phase-7-worldspace-ui.md). Architecture reference: [`docs/phase7-architecture.md`](phase7-architecture.md), evaluation: [`docs/phase7-evaluation.md`](phase7-evaluation.md).

1. Wire `WorldUIRenderer` + nameplate/HP/damage/faction path per kickoff (world-space canvas + batcher quads; LOD-tier culling).
2. Prove `WorldspaceUI` / `WorldspaceLabel3D` toggles in-game and via PlayCUA (`phase-7-worldspace-ui.yaml`).
3. Flip phase-7 defaults only after smoke + refreshed screenshots (`sync-playcua-screenshots.ps1`).

**Next commands (from repo root):**

```powershell
git fetch origin main
dotnet test tests/WorldSphereMod.Tests.E2E --filter "FullyQualifiedName~Worldspace"
pwsh Tools/wsm3d.ps1 install
pwsh Tools/wsm3d.ps1 playcua run Tools/wsm3d-playcua/sample-scenarios/phase-7-worldspace-ui.yaml -VisionBackend off
pwsh Tools/do-all.ps1 -SkipLive -PlaycuaRetries 1    # offline + playcua; no funnel
pwsh Tools/sync-playcua-screenshots.ps1
```

## Recommended next steps

1. **Phase 7 implementation** — follow kickoff FRs; keep `WorldspaceUI` / `WorldspaceLabel3D` default-off until PlayCUA + human smoke pass.
2. **Visual verification with populated world** — latest desk `do-all`: PlayCUA 13/13 @1× with vision off; fix `live-verify-live` + `audit-tick` before claiming full green. Sync captures: `pwsh Tools/sync-playcua-screenshots.ps1` (see [Screenshot sync workflow](#screenshot-sync-workflow) below). Human still judges kingdom/actor visuals in-game.
2. Smoke-test Phase 2 procedural buildings the same way Phase 1 was proven: toggle `ProceduralBuildings`, capture screenshots, and diff against canonical output.
3. **Shader bundle rebake — DONE (2026-05-26).** Headless: `pwsh Tools/bake-shaders.ps1` (optional `-UnityExe` when Hub auto-detect fails). Log: `Tools/bake-shaders.log`. Manifest: 10 shaders in `WorldSphereMod/AssetBundles/win/wsm3d-shaders.manifest`. **Human gate:** confirm in-game `LoadedShaders[count=3]` and phase visuals before adding names to `SafeShaders` (see below).
4. Implement ADR-0006 (Phase 6 Step 9 DrawProceduralIndirect skinning) — 2–3 day estimate if we decide to replace the visible skinned-mesh path with GPU-resident batching later.
5. ~~Flip ADR-0007 status to **Accepted**~~ — **done** (`docs/adr/ADR-0007-conditional-patch-dispatch.md`). `PhaseToast` ships in-game phase feedback.

## SafeShaders human gate

`Core.Sphere.LoadAssets` iterates **`SafeShaders` only** (3 names). The other **7** shaders are present in the rebaked `wsm3d-shaders` bundle but **must not** be added to `SafeShaders` until each is proven safe in-game — loading them previously triggered Unity native **ManagedStream** errors / crash reporter uploads even when C# caught exceptions (`Core.cs` comments; E2E `Core_shader_load_list_matches_SafeShaders_exactly`).

| Loaded at runtime (`SafeShaders`) | In bundle, load gated |
|---|---|
| `OpaqueVertexColor` | `StratumVoxelPBR` |
| `GerstnerWater` | `ProceduralSky` |
| `ColorGradingLUT` | `Impostor` |
| | `ScreenSpaceAO` |
| | `ScreenSpaceGI` |
| | `BrpBloom` |
| | `BrpACES` |

**Before expanding `SafeShaders`:** (1) `./Tools/install.ps1` + relaunch WorldBox; (2) grep Player.log for `LoadedShaders[count=3]` and three `Loaded shader from wsm3d-shaders bundle` lines with non-empty resolved names; (3) smoke voxel + mesh water + PostFX toggles; (4) add **one** gated shader at a time, relaunch, watch for `Uploading Crash Report` / empty `.name` skips; (5) update E2E invariant if the permanent list changes.

**Headless rebake (no Unity GUI):** `pwsh Tools/bake-shaders.ps1` — Unity `-batchmode -nographics -quit -executeMethod BakeShaders.BakeAll`. There is **no** `wsm3d.ps1 bake` subcommand; use the standalone script only when Unity 2022.3 is installed locally.

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

## Automation (desktop)

Requires WorldBox installed, mod enabled, and bridge on `127.0.0.1:8766`. `Ensure-BridgeReady` in `Tools/wsm3d.ps1` polls `/health` (default 90s); with `-RelaunchIfDown` it relaunches via `wsm3d relaunch -NoBuild` and waits up to 5 minutes. `playcua run-all` calls it before each scenario and again on per-scenario retry (2× max).

```powershell
# One-shot: offline gates → relaunch → bridge wait → journey mock → PlayCUA 13/13 (3 run-all attempts) → screenshot sync → live verify
pwsh Tools/do-all.ps1
pwsh Tools/do-all.ps1 -SkipRelaunch          # bridge already up
pwsh Tools/do-all.ps1 -SkipLive              # offline gates only (no journey/PlayCUA)
pwsh Tools/do-all.ps1 -PlaycuaRetries 3      # default; relaunch + 30s settle between run-all attempts

# Periodic audit (/loop, ~5m tick)
pwsh Tools/wsm3d-audit-tick.ps1 -RelaunchIfBridgeDown
pwsh Tools/wsm3d-audit-tick.ps1 -SkipLive -Quiet   # offline-only tick

# Reports
Get-Content Tools/.reports/do-all-latest.json | ConvertFrom-Json
Get-Content Tools/.reports/audit-tick-latest.json | ConvertFrom-Json
```

**`do-all.ps1` stages:** (1) offline `wsm-live-verify.ps1`, (2) optional relaunch + bridge wait (5m) + `bridge-save-load-smoke` bootstrap when `isWorld3D=false`, (3) journey mock, (4) PlayCUA `run-all` (default **`-VisionBackend off`** on desk; pass `-Vision` only when OmniRoute probe succeeds), up to **3** full attempts, (5) `sync-playcua-screenshots.ps1`, (6) `wsm-live-verify.ps1 -Live -SkipOffline`, (7) final offline verify, (8) quiet `wsm3d-audit-tick.ps1`. Writes `Tools/.reports/do-all-latest.json`. **Desk note:** funnel timeouts → vision off; PlayCUA can still pass while stages (6) and (8) fail.

**`wsm3d-audit-tick.ps1` stages:** git/dirty check → `dotnet test` → `wsm3d doctor` → phase-*.png manifest gate → bridge `/health` (optional relaunch with **15m cooldown** via `-RelaunchIfBridgeDown`) → journey mock → PlayCUA `run-all` (**2** attempts, relaunch between) → screenshot sync. Writes `Tools/.reports/audit-tick-latest.json`; lists human blockers (screenshots, vision backend, shader log) without failing exit on those alone.

**`Ensure-BridgeReady`** (`Tools/wsm3d.ps1`): polls `http://127.0.0.1:8766/health` every 5s (default **90s**); with `-RelaunchIfDown` runs `wsm3d relaunch -NoBuild` then polls up to **5 minutes**. Called before each PlayCUA scenario and again on per-scenario retry.

**PlayCUA retry layers:** per-scenario **2×** inside `wsm3d.ps1 playcua run-all` (with `Ensure-BridgeReady -RelaunchIfDown` on retry); full `run-all` **3×** via `do-all.ps1` (`-PlaycuaRetries`, default 3); audit tick uses **2** run-all attempts.

### OmniRoute (kooshas-laptop)

`do-all.ps1 -Vision` uses OmniRoute from `Tools/omniroute-vision.env`. **Desk funnel:** `https://omniroute-a6e82363-1.tail2b570.ts.net/v1`. **Fallback tailnet (pre-funnel):** `http://100.112.14.98:20128/v1` on **kooshas-laptop**. After a DB wipe, refresh `OMNROUTE_API_KEY` in the local env file. **Laptop setup:** [`Tools/setup-omniroute-laptop.md`](../Tools/setup-omniroute-laptop.md). **Desk check:** `pwsh Tools/verify-omniroute-remote.ps1`.

**Cursor on the laptop** can use `localhost:20128`; the **desk** must reach **`/chat/completions`**, not only `/models` (2214 models over Tailscale does *not* imply vision works). If desk gets **502** or timeout on chat while CC works locally, expose the API on the tailnet, e.g. on the laptop:

```bash
# OmniRoute bound to 127.0.0.1 only — forward over Tailscale:
tailscale serve --bg http://127.0.0.1:20128
# or configure OmniRoute to listen on 0.0.0.0:20128 (check OmniRoute docs)
```

When chat probe fails, `do-all` runs PlayCUA with **vision off** (`visionDegraded: true`). **Desk runs:** prefer `-SkipLive` or default vision-off `run-all`; funnel `https://omniroute-a6e82363-1.tail2b570.ts.net/v1` often times out even when `/models` responds. If funnel is slow, raise probe timeout via HTTPS (see `verify-omniroute-remote.ps1`) or use tailnet `http://100.112.14.98:20128/v1`. Local secrets: copy `Tools/omniroute-vision.env.example` → `Tools/omniroute-vision.env` (gitignored; never commit).

## Screenshot sync workflow

`docs/screenshots/` is gitignored (`**/screenshots/` in `.gitignore`); there is no tracked `docs/screenshots/README.md`. PlayCUA writes PNGs under artifact trees; sync copies phase captures into the local gate folder for manifest/doctor checks.

```powershell
# After do-all, PlayCUA run-all, or manual live runs:
pwsh Tools/sync-playcua-screenshots.ps1
```

**Source directories** (script scans each for `*.png` under `phase-*` folders):

| Path | Notes |
|---|---|
| `artifacts/` | Legacy PlayCUA output |
| `Tools/wsm3d-playcua/artifacts/` | Default scenario artifacts |
| `Tools/wsm3d-playcua/.reports/run-all-artifacts/artifacts/` | `run-all` batch output |
| `Tools/wsm3d-playcua/.reports/live-verify-artifacts/artifacts/` | Live-verify gate captures |

**Destination naming:** `docs/screenshots/phase-{N}-{slug}.png` — `{N}` from the first `phase-*` directory segment; `{slug}` from the source filename (e.g. `phase-1-voxel-actors/actors.png` → `phase-1-actors.png`).

**Window targeting:** `Win32Capture` in `Tools/wsm3d-playcua/main.py` enumerates WorldBox by process name and window title, captures the client area, and sets `capture_target: worldbox_window` in PlayCUA step details (desktop fallback only when no hwnd). Confirm this field in scenario JSON before trusting PNGs; pre-2026-05-26 captures may show the wrong foreground window.

## Dev tooling

- **Do all (one shot):** `pwsh Tools/do-all.ps1` — see [Automation (desktop)](#automation-desktop) above; report `Tools/.reports/do-all-latest.json`.
- **Audit loop (5m):** `pwsh Tools/wsm3d-audit-tick.ps1 -RelaunchIfBridgeDown` — git/dirty, offline tests, doctor, screenshot manifest, bridge (15m relaunch cooldown), journey mock, PlayCUA `run-all` (2 attempts + relaunch), screenshot sync; report `Tools/.reports/audit-tick-latest.json`.
- **Bridge recovery:** `Ensure-BridgeReady` in `Tools/wsm3d.ps1` — health poll, optional relaunch; used by `playcua run-all` and indirectly by `do-all.ps1` relaunch/wait helpers.
- **PlayCUA flakes:** per-scenario 2× in `wsm3d.ps1 playcua run-all`; full `run-all` 3× in `do-all.ps1`; audit tick 2×.
- **CLI:** `pwsh Tools/wsm3d.ps1 help` — 13 subcommands (build, install, launch, relaunch, log, toggle, journey capture, etc.).
- **Slash commands:** `/wsm-status`, `/wsm-validate-all`, `/wsm-build`, `/wsm-install`, `/wsm-relaunch`, `/wsm-log`, `/wsm-toggle`, `/wsm-screenshot`, `/wsm-journey-run`, `/wsm-doctor`.
- **MCP:** `Tools/wsm3d-mcp/` — Python FastMCP with 18 tools, auto-registered via `.claude/mcp-servers.json`.
- **Journey gate:** `.github/workflows/journeys-gate.yml` — OCR-assertion DSL; verify with `phenotype-journey verify <manifest> --mock`. Live capture remains the final proof step; entry point: `docs/live-verification.md`.
- **Live-verify gate (CI):** `.github/workflows/live-verify-gate.yml` — offline `dotnet test` + journey mock (stages 1–2 of `Tools/wsm-live-verify.ps1`; **525 pass / 3 skip**, 528 total locally). Reused by **nightly** (`nightly.yml` → `live-verify-offline` job). Full harness: `pwsh Tools/wsm-live-verify.ps1` (add `-Live -Vision` for PlayCUA + SSIM + OmniRoute vision on a desktop with WorldBox + bridge). Desktop one-shot: `pwsh Tools/do-all.ps1`.
- **ADR-0007 (conditional patch dispatch):** **Accepted in code, runtime still unproven** — `PhasePatchGate.ShouldApplyHarmonyPatch` is wired from `Core.Patch()`, but `docs/issue-triage.md` reports `0/4 Harmony types affected` for VoxelEntities. E2E: `ConditionalPatchDispatchInvariantsTests`.
- **Live verify:** `docs/live-verification.md` — programmatic (`dotnet test`, journey mock, optional SSIM ≥ 0.95) vs agentic (`wsm3d-playcua` sample scenarios, OmniRoute combo, bridge save/load checklist).

When you need a release or handoff bundle, use the canonical checklist in [`docs/live-verification.md`](live-verification.md#canonical-live-proof-bundle). It is the single place that names the required live verifier command, `Tools/.reports/live-verify-latest.json`, PlayCUA artifacts, phase-preview SSIM fixtures, and the explicit skip/offline note when `live-playcua-ssim` does not run.

## Recent commits (7 most recent on `feat/phase-7-ui-kickoff`)

```
117746e merge: sync origin/main after PR7 squash merge
1d232e8 docs(phase7): scaffold phase-7 worldspace UI kickoff doc
4efa128 WorldSphereMod3D: automation, PlayCUA gates, and live-verify harness (#7)
0c1f11e test(playcua): run-all passes without OmniRoute; align E2E invariants
5855111 fix(live-verify): bridge telemetry probe for PlayCUA drawCalls
971b47e fix(bridge): VoxelFrameDriver on bridge host; wsm-live-verify -SkipOffline
47484d6 fix(bridge): lastNonZeroDrawCalls telemetry and PlayCUA baseline checks
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

## Parallel development (worktrees)

Worktree root: `%USERPROFILE%\.cursor\worktrees\WorldSphereMod`. One worktree + subagent per `wt/<topic>` branch; merge each topic into `claude/research-ultraplan-fork-DdgI5` before `do-all`. **Game-bound:** WorldBox allows one live instance — run a single `do-all` from the main repo after merges, not from every worktree.

```powershell
# Add (from main repo)
git worktree add "$env:USERPROFILE\.cursor\worktrees\WorldSphereMod\wt-<topic>" -b wt/<topic>

# Remove when merged
git worktree remove "$env:USERPROFILE\.cursor\worktrees\WorldSphereMod\wt-<topic>"
```

**Merge order:** (1) merge each `wt/<topic>` → `claude/research-ultraplan-fork-DdgI5` and resolve conflicts; (2) checkout integration branch in main repo; (3) `pwsh Tools/do-all.ps1 -Vision`.

**OmniRoute:** `-Vision` uses funnel `https://omniroute-a6e82363-1.tail2b570.ts.net/v1` or tailnet `http://100.112.14.98:20128/v1`. When the probe fails, PlayCUA runs with vision off (`visionDegraded: true`; see [OmniRoute (kooshas-laptop)](#omniroute-kooshas-laptop)).

## Branch / PR hygiene

- **Current work:** push to `feat/phase-7-ui-kickoff`, not `main`.
- **PR #7** is **MERGED** on `main` @ `4efa128` — https://github.com/KooshaPari/WorldSphereMod/pull/7. This branch rebased/merged `origin/main` after that squash-merge.
- Use `git push --no-recurse-submodules origin HEAD` (submodule pinned at `73a7b77`).
- Topic branches / worktrees still merge into `claude/research-ultraplan-fork-DdgI5` before `main` when batching automation fixes.
- Post-merge / phase checklist: [`docs/MERGE_CHECKLIST.md`](MERGE_CHECKLIST.md).
- One PR per phase; commits within a phase can be incremental.
- After a phase is proven in actual WorldBox gameplay: flip its `SavedSettings` flag default,
  update the README phase table, update this doc's landed/runtime status row.
- **Secrets:** `Tools/*-vision.env` and `Tools/.reports/` are gitignored — commit only `*.env.example` templates.
