# Handoff — pick up cold

Canonical "next session starts here" doc for WorldSphereMod3D.

**Last updated:** 2026-05-19

## TL;DR

Hard fork of `MelvinShwuaner/WorldSphereMod` that finishes the 3D conversion
of WorldBox: actors, buildings, foliage, water, lighting, animation, UI, sky,
particles and LOD now have real 3D code paths sitting behind per-phase flags
in `SavedSettings`. All 10 phases are code-complete; **Phase 1 is visibly
working in-game** after the `VoxelScaleMultiplier=8.0f` fix. Phases 2 and 5
still need in-game smoke tests before their flags flip to default-on.
Build is local-only (needs WorldBox reference DLLs); CI builds only the
Unity-free API project.

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
| 0  Fork plumbing                       | ✅ | Build portability, GUID `worldsphere3d.fork`, settings v2, API v2 |
| 1  Voxel actors + buildings            | ✅ | Visibly rendering as of 2026-05-19 (commit `94030fb`); `VoxelScaleMultiplier=8.0f` resolves sub-pixel issue (sprite voxel meshes are 11×5×1 local-space × 0.1 sprite-scale → ~1.1×0.5×0.1 world-units = sub-pixel at default zoom). See [ADR-0011](adr/0011-phase-1-visibility-postmortem.md). |
| 2  Procedural building meshes          | 🔄 | Heuristic + roof inference shipped; **awaits smoke test**, flag still OFF |
| 3a Crossed-quad foliage                | ✅ | Trees/bushes/rocks ship as crossed quads; flag default ON |
| 3b Surface overlays + walls            | ✅ | `WorldTilemap.renderTile` Prefix + `drawWallType` Prefix wired |
| 4  Mesh water                          | ✅ | Gerstner surface + mask buffer + lifecycle Postfix; flag default ON |
| 5  Sun + cascaded shadows              | 🔄 | `SunDriver`/`SunRig`/`ShadowCascadeConfig` landed; **lit shader (`VoxelLit.shader`) requires Unity 2022.3 AssetBundle bake** — flag OFF |
| 6  Skeletal animation                  | ⚠️  | Humanoid/quadruped rigs + `RigDriver` + CPU fallback shipped; GPU compute (`VoxelSkin.compute`) **gated off** — ADR-0006 documents DrawProceduralIndirect rewrite path forward; flag default OFF |
| 7  Worldspace UI                       | ✅ | Nameplate + HP bar + damage popups; selection ring stub awaits `SelectionManager` hook; flag default ON |
| 8  Day/night + sky + fog               | 🔄 | Autonomous TOD driver + sun gradient + ProceduralSky landed; `MapBox.world_time` probe falls back gracefully; flag default OFF |
| 9  Particles + decals + PostFX         | ✅ | 5 effect IDs bursted; 3-channel `DecalPool`; URP PostFX volume; particles ON, PostFX OFF |
| 10 LOD + impostor fallback             | ✅ | `FrustumCuller` + `LodSelector` + `ImpostorBillboard` + soft hardware gate; **no dedicated Proxy mesh — Proxy tier falls through to Voxel path** |

## Default-on flags (the user sees these without opting in)

- `CrossedQuadFoliage` — Phase 3a/3b
- `MeshWater` — Phase 4
- `WorldspaceUI` — Phase 7
- `ParticleEffects` — Phase 9 (decals + bursts)

## Default-off flags (opt-in)

- `VoxelEntities` — Phase 1 (visible in-game; flag remains opt-in until ship gate)
- `ProceduralBuildings` — Phase 2 (blocked on smoke test)
- `HighShadows` — Phase 5 (needs lit shader from Unity bake)
- `SkeletalAnimation` — Phase 6 (cost gate)
- `DayNightCycle` — Phase 8
- `PostFX` — Phase 9
- `ProfilerDump` — diagnostic
- `FogDensity` — float, defaults `0.0`

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
2. Confirm vanilla-3D path is regression-clean with `VoxelEntities = false` (default).
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
2. Implement ADR-0006 (Phase 6 Step 9 DrawProceduralIndirect skinning) — 2–3 day estimate.
3. Install Unity 2022.3 + clone `Compound-Spheres-3D` submodule; bake the four shaders into platform AssetBundles under `WorldSphereMod/AssetBundles/{win,linux,osx}/worldsphere`.
4. Conditional Harmony patch dispatch (if wave-5 lands the ADR-0007 work).

## Dev tooling

- **CLI:** `pwsh Tools/wsm3d.ps1 help` — 13 subcommands (build, install, launch, relaunch, log, toggle, journey capture, etc.).
- **Slash commands:** `/wsm-status`, `/wsm-validate-all`, `/wsm-build`, `/wsm-install`, `/wsm-relaunch`, `/wsm-log`, `/wsm-toggle`, `/wsm-screenshot`, `/wsm-journey-run`, `/wsm-doctor`.
- **MCP:** `Tools/wsm3d-mcp/` — Python FastMCP with 18 tools, auto-registered via `.claude/mcp-servers.json`.
- **Journey gate:** `.github/workflows/journeys-gate.yml` — OCR-assertion DSL; verify with `phenotype-journey verify <manifest> --mode mock`.

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
