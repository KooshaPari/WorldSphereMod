# Handoff — pick up cold

Canonical "next session starts here" doc for WorldSphereMod3D.

## TL;DR

Hard fork of `MelvinShwuaner/WorldSphereMod` that finishes the 3D conversion
of WorldBox: actors, buildings, foliage, water, lighting, animation, UI, sky,
particles and LOD now have real 3D code paths sitting behind per-phase flags
in `SavedSettings`. All 10 phases are code-complete; **Phases 1, 2 and 5
still need an in-game smoke test** before their flags flip to default-on.
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
| 1  Voxel actors + buildings            | 🔄 | Code-complete; all 5 review issues fixed; **awaits in-game smoke test**, flag still OFF |
| 2  Procedural building meshes          | 🔄 | Heuristic + roof inference shipped; **awaits smoke test**, flag still OFF |
| 3a Crossed-quad foliage                | ✅ | Trees/bushes/rocks ship as crossed quads; flag default ON |
| 3b Surface overlays + walls            | ✅ | `WorldTilemap.renderTile` Prefix + `drawWallType` Prefix wired |
| 4  Mesh water                          | ✅ | Gerstner surface + mask buffer + lifecycle Postfix; flag default ON |
| 5  Sun + cascaded shadows              | 🔄 | `SunDriver`/`SunRig`/`ShadowCascadeConfig` landed; **lit shader (`VoxelLit.shader`) requires Unity 2022.3 AssetBundle bake** — flag OFF |
| 6  Skeletal animation                  | ✅ | Humanoid/quadruped rigs + `RigDriver` + GPU compute skin (`VoxelSkin.compute`) + CPU fallback; flag default OFF (cost gate) |
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

- `VoxelEntities` — Phase 1 (blocked on smoke test)
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

## In-game smoke test (gates Phases 1, 2, 5, 8)

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

## What's left after this session

### User-driven

- Run the Phase 1 + Phase 2 smoke tests above. Flip the two flags to ON in
  `SavedSettings.cs` once clean.
- Capture screenshots into `docs/screenshots/` (placeholder folder exists).

### Environment-driven

- **Unity 2022.3 install** + clone `Compound-Spheres-3D` submodule into
  `External/Compound-Spheres-3D/` per `docs/phase5-prep.md`. Required to
  bake `Resources/Shaders/VoxelLit.shader`,
  `Resources/Shaders/WaterGerstner.shader`,
  `Resources/Shaders/FoliageWind.shader` and
  `Resources/Shaders/ProceduralSky.shader` into the platform AssetBundles
  under `WorldSphereMod/AssetBundles/{win,linux,osx}/worldsphere`. Until
  baked, those phases run with placeholder unlit fallback materials.
- A bake unlocks Phase 5 (`HighShadows`), the real Phase 4 water look, the
  real Phase 8 sky look, and proper per-vertex normals on voxel meshes
  (currently flat-shaded via the placeholder material).

### Code-driven (TODO list)

- **Phase 7 selection ring** — `SelectionRing.cs` is in tree but no Harmony
  Prefix hooks `SelectionManager`. Needs a decompile pass to find the right
  call site, then a Prefix/Postfix to keep the world-space ring in sync.
- **Phase 10 Proxy tier** — `LodSelector.Select` returns `LodTier.Proxy`
  for the middle distance band, but no proxy mesh exists; `LodDispatch`
  currently falls through to the Voxel path. Either kill the tier or ship
  a 32-voxel-cap proxy mesh.
- **Phase 1 review issue #5** (`VoxelRender` material leak across world
  reload) — `VoxelRender.Reset()` exists *and* is now wired into
  `WorldUnloadPatch` (Phase 10 step 5). Mark this closed.
- **Per-instance color shader read** — `MeshInstanceBatcher` uploads
  `_InstanceColor` to the property block, but the placeholder unlit
  material doesn't sample it. The Phase 5 `VoxelLit.shader` must read it.
- **External `RegisterBuildingRules` ergonomics** — API takes `object` at
  the delegate boundary. Consider a `RegisterBuildingRules(string assetId,
  string rulesJson)` overload so external mod authors don't have to
  reference the internal `BuildingRules` struct.
- **Skeletal cache key** — `RigCache` keys on
  `(Sprite.GetInstanceID, RigType)`. Once per-creature rigs diverge add
  `rigId` to the key.

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
