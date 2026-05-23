# Handoff — pick up cold

Canonical "next session starts here" doc for WorldSphereMod3D.

**Last updated:** 2026-05-23

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
CI builds only the Unity-free API project.

## Where things are

| Thing | Location |
|---|---|
| Active branch | `claude/research-ultraplan-fork-DdgI5` |
| Open draft PR | https://github.com/KooshaPari/WorldSphereMod/pull/1 |
| Cold-start orientation | `CLAUDE.md` |
| Full 10-phase plan | `docs/PLAN.md` |
| Per-phase architectures | `docs/phase{2..10}-architecture.md` |
| Phase 1 review (fixes already applied) | `docs/phase1-review.md` |
| Phase 1 smoke-test checklist | `docs/smoke-test-phase1.md` |
| `render_data` field map | `docs/render-data-fields.md` |
| Phase 5 prep (Unity 2022.3 + Compound-Spheres-3D submodule) | `docs/phase5-prep.md` |
| Settings | `WorldSphereMod/Code/SavedSettings.cs` |
| Build portability layer | `Directory.Build.props` (env: `WORLDBOX_PATH`) |
| CI | `.github/workflows/build.yml` (API-only) |
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

## What's shipped (per phase)

| Phase | State | Notes |
|---|---|---|
| 0  Fork plumbing                       | ✅ | Build portability, GUID `worldsphere3d.fork`, settings v2, API v2, task/journey gates, capability discovery, opt-in profiler overlay, journey capture tooling |
| 1  Voxel actors + buildings            | ✅ | Current code default: `VoxelEntities = true`. Smoke-test verification is documented elsewhere and should not be inferred from the default alone. |
| 2  Procedural building meshes          | ✅ | Current code default: `ProceduralBuildings = true`. |
| 3a Crossed-quad foliage                | ✅ | Current code default: `CrossedQuadFoliage = true`. |
| 3b Surface overlays + walls            | ✅ | `WorldTilemap.renderTile` Prefix + `drawWallType` Prefix wired |
| 4  Mesh water                          | ✅ | Current code default: `MeshWater = false`. |
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
| `MeshWater` | `false` | 4 | Default-off mesh water |
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
- `CrossedQuadFoliage` — Phase 3a/3b
- `ProceduralBuildings` — Phase 2
- `WorldspaceUI` — Phase 7
- `ParticleEffects` — Phase 9 (decals + bursts)
- `PostFX` — Phase 9
- `HighShadows` — Phase 5
- `SkeletalAnimation` — Phase 6
- `DayNightCycle` — Phase 8
- `FogDensity` — `0.05f` in live settings

### Default-off / opt-in

- `MeshWater` — Phase 4
- `SSGIEnabled` — Phase 9

### Diagnostic / non-phase settings

- `ProfilerDump` — diagnostic overlay default
- `BiomeBlending` — terrain polish
- `MountainSlopeSmoothing` — terrain polish
- `WorldspaceHealth3D` — worldspace HP bar style
- `SSAOEnabled` — Phase 9 ambient occlusion
- `SSAOQuality` — diagnostic tuning value
- `WeatherRain` — weather default
- `WeatherSnow` — weather default
- `WeatherLightning` — weather default
- `LODScale` — LOD tuning default
- `WaterDetail` — LOD tuning default
- `FoliageDensity` — LOD tuning default

## Local build + install

```powershell
$env:WORLDBOX_PATH = "C:/Program Files (x86)/Steam/steamapps/common/worldbox"
dotnet build WorldSphereMod.csproj -c Release    # ~5s, 0 errors
./Tools/install.ps1                              # builds + copies to <WorldBox>/Mods/WorldSphereMod3D/
```

NeoModLoader compiles `Code/*.cs` at runtime, so install copies sources plus
`Assemblies/CompoundSpheres.dll`, `AssetBundles/`, `GameResources/`,
`Locales/`, `mod.json`. CI cannot build the mod itself (needs WorldBox refs);
it only builds `WorldSphereAPI.csproj`.

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

- **Phase 2 procedural buildings smoke test** — use the same toggle + screenshot + diff-vs-canonical flow that proved Phase 1 visibility.
- **Unity 2022.3 install** — required to bake `VoxelLit.shader`, `WaterGerstner.shader`, `ProceduralSky.shader` into AssetBundles for Phases 4, 5, 8.
- **Anthropic API key** (optional) — for live-mode journey verification via `phenotype-journey verify ... --api-key`. Mock mode works without it.

## Recommended next steps

1. Smoke-test Phase 2 procedural buildings the same way Phase 1 was proven: toggle `ProceduralBuildings`, capture screenshots, and diff against canonical output.
2. Implement ADR-0006 (Phase 6 Step 9 DrawProceduralIndirect skinning) — 2–3 day estimate if we decide to replace the visible skinned-mesh path with GPU-resident batching later.
3. Install Unity 2022.3 + clone `Compound-Spheres-3D` submodule; bake the four shaders into platform AssetBundles under `WorldSphereMod/AssetBundles/{win,linux,osx}/worldsphere`.
4. Conditional Harmony patch dispatch (if wave-5 lands the ADR-0007 work).

## Dev tooling

- **CLI:** `pwsh Tools/wsm3d.ps1 help` — 13 subcommands (build, install, launch, relaunch, log, toggle, journey capture, etc.).
- **Slash commands:** `/wsm-status`, `/wsm-validate-all`, `/wsm-build`, `/wsm-install`, `/wsm-relaunch`, `/wsm-log`, `/wsm-toggle`, `/wsm-screenshot`, `/wsm-journey-run`, `/wsm-doctor`.
- **MCP:** `Tools/wsm3d-mcp/` — Python FastMCP with 18 tools, auto-registered via `.claude/mcp-servers.json`.
- **Journey gate:** `.github/workflows/journeys-gate.yml` — OCR-assertion DSL; verify with `phenotype-journey verify <manifest> --mode mock`. Live capture remains the final proof step, and strict-assets checks still gate manifests with screenshots.

## Recent commits (7 most recent)

```
e1fdc45 docs(tooling): CLAUDE.md ref + CONTRIBUTING + journey-authoring + PS completions
1402802 Fix release workflow: GitHub Actions heredoc delimiter
d6f520f Fix release workflow: broken pipe in CHANGELOG extraction
924fc3d feat(tooling,build): net48 retarget + watch + journey capture + tooling docs page + nightly/release CI
8d42378 chore(deps,tooling): bump pkgs to latest CVE-free + Just/Task parity + ADR-0006 + watch + journeys CI
0e4008c feat(tooling): full Phenotype-org dev stack — slash + skill + CLI + MCP + journeys + tests
0e3e424 fix(phase-1): pick instancing-capable shader; guard DrawMeshInstanced
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
- One PR per phase; commits within a phase can be incremental.
- After a phase smoke-tests clean: flip its `SavedSettings` flag default,
  update the README phase table, update this doc's "What's shipped" row.
