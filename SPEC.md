# SPEC — WorldSphereMod3D

**Document ID:** SPEC-WSM-001
**Version:** 2.0 (matches `SavedSettings.Version`)
**Status:** Living
**Author:** @KooshaPari
**Created:** 2026-04-10
**Last updated:** 2026-05-18
**Target release:** v2.0 (per `VERSION`)

> Router document. Per-phase architectural detail lives under
> `docs/phase[1..10]-architecture.md` and `docs/phase5-prep.md`. ADRs that
> back individual decisions are indexed in [`ADR.md`](./ADR.md).

---

## 1. System overview

WorldSphereMod3D is a Harmony-based rendering mod loaded by NeoModLoader
into WorldBox. It composes ten independent rendering systems on top of the
existing `CompoundSpheres` terrain backend; each system is gated by a
`SavedSettings` boolean and registered into one of three render queues
(`VoxelRenderQueue`, `BuildingRenderQueue`, foliage/water/UI/Fx queues)
that flush through `MeshInstanceBatcher.Submit` + `Flush` once per frame.

```
   Sprite path (upstream, fallback)            Mesh path (this fork)
   ─────────────────────────────────           ──────────────────────────────
   QuantumSpriteLibrary.draw*                  Voxel / ProcGen / Foliage /
        ↓                                       Water / Rig / Worldspace / Fx
   SpriteRenderer billboards                          ↓
                                              MeshInstanceBatcher (buckets per
                                              mesh+material) → Graphics.
                                              DrawMeshInstanced
                                                     ↓
                                              SunRig + ShadowCascadeConfig
                                              + URP PostFx volume (reflective)
```

## 2. Phase composition

Each phase is a self-contained module under `WorldSphereMod/Code/<area>/`
with a `SavedSettings` flag, a Harmony patch class, and (where needed) a
`MonoBehaviour` driver registered into `Mod.Object`.

| # | Module dir | Flag | Patches |
|---|---|---|---|
| 0 | (project-wide) | n/a | `Directory.Build.props`, GUID, settings v2 |
| 1 | `Voxel/` | `VoxelEntities` | `ActorManager.precalculateRenderDataParallel`, `BuildingManager.precalculateRenderDataParallel` |
| 2 | `ProcGen/` | `ProceduralBuildings` | `BuildingManager.precalculateRenderDataParallel` (Postfix) |
| 3a | `Foliage/` | `CrossedQuadFoliage` | TopTile path + `WorldTilemap.renderTile` |
| 3b | `Foliage/` | `CrossedQuadFoliage` | `QuantumSpriteLibrary.drawWallType` (Prefix) |
| 4 | `Water/` | `MeshWater` | `Sphere.Begin/Finish`, `UpdateBaseLayer`, `UpdateScale`, `SphereTileColor` |
| 5 | `Lighting/` | `HighShadows` | URP pipeline asset (reflective) |
| 6 | `Rig/` | `SkeletalAnimation` | Voxel pipeline consumer (no direct WorldBox patch) |
| 7 | `Worldspace/` | `WorldspaceUI` | `SelectedUnit.select`/`unselect`/`removeSelected`/`clear` |
| 8 | `Lighting/` | `DayNightCycle` | `MapBox.world_time` reflection probe, fallback autonomous |
| 9 | `Fx/` | `ParticleEffects`, `PostFX` | `Effects.cs` particle-burst suppression |
| 10 | `LOD/`, `Perf/` | `LODScale`, `ProfilerDump` | `Mod.cs:21` hardware gate softened |

Phase isolation invariant: flipping any one flag OFF must not break
another phase. Verified per phase via `docs/smoke-test-phase*.md`.

## 3. Public API surface — `WorldSphereAPI` v2

External binding lives in `WorldSphereAPI/WorldSphereAPI.cs` (netstandard2.0,
Unity-free, reflection-loaded). Internal implementation in
`WorldSphereMod/Code/WorldSphereAPI.cs`. Both must stay in sync.

**v1 (preserved, identical signatures):**

- `bool IsWorld3D { get; }`
- `void MakeActorNonUpright(string id)`
- `void MakeBuildingNonUpright(string id)`
- `void MakeProjectileNonUpright(string id)`
- `void EditEffect(string id, bool upright, bool separateSprite=false, float extraHeight=0, bool onGround=true)`
- `T GetSetting<T>(string name)`
- `static bool Connect(out WorldSphereAPI api)` — probes both `WorldSphereMod3D` and upstream `THE_3D_WORLDBOX_MOD` assemblies

**v2 additions (no-op on v1 hosts):**

- `bool IsModel3D { get; }` — true when the mesh pipeline (Phase 1+) is active, distinct from `IsWorld3D` (terrain only)
- `void RegisterCustomMesh(string assetId, object mesh, object albedo)` — bypass auto-voxelization
- `void RegisterBuildingRules(string assetId, object rules)` — override Phase 2 heuristics; `rules` is `WorldSphereMod.ProcGen.BuildingRules`
- *(planned)* `void RegisterRig(string assetId, object rig)` — assign a custom skeleton (Phase 6 consumer)
- *(planned)* `event Action<float> OnTimeOfDayChanged` — Phase 8 hook

Compatibility test: `WorldSphereTester/` regression mod calls every public
surface and must build + load against both the v1 upstream host and this
fork.

## 4. Integration contract

**WorldBox.** This mod patches ~80 WorldBox call sites via Harmony. Upstream
files (`Core.cs`, `QuantumSprites.cs`, `Effects.cs`, `Tools.cs`,
`DimensionConverter.cs`, `General.cs`, `TileMapToSphere.cs`,
`CompoundSphereScripts.cs`, `3DCamera.cs`) are inherited and edited
in-place. Pitfalls and invariants are documented in `CLAUDE.md` §"Pitfalls
and surprises" — z-displacement sentinel, cylindrical X-wrap, parallel
render passes, compute-shader gate, AssetBundle paths.

**NeoModLoader.** Mod ships as `Code/*.cs` source plus `Assemblies/`,
`AssetBundles/{win,linux,osx}/worldsphere`, `GameResources/`, `Locales/`,
and `mod.json`. NeoModLoader runtime-compiles the C#. `mod.json` GUID is
`worldsphere3d.fork` (vs upstream's `worldsphere`) so both can be installed
side-by-side; the user enables only one in the loader.

**Compound-Spheres terrain backend.** Phase 0 keeps the vendored
`Assemblies/CompoundSpheres.dll` for compatibility. Phase 5 introduces
`External/Compound-Spheres-3D/` as a submodule fork that emits per-vertex
normals and exposes a water-mask SSBO; the rebuilt DLL is a drop-in
replacement gated by a build-time `<ProjectReference>` swap. See
`docs/phase5-prep.md`.

**AssetBundles.** Built externally in Unity 2022.3 against
`External/AssetBundleBuilder/`. Shaders that ship as `.shader` source
(`Resources/Shaders/{VoxelLit,WaterGerstner,FoliageWind,ProceduralSky,VoxelSkin.compute}`)
must be baked into the platform bundles before the corresponding phase can
flip its flag default-on. Until baked, the phase runs with a placeholder
unlit fallback material.

## 5. Settings model

`SavedSettings.cs` is the single source of truth for runtime behaviour.
Version pinned to `"2.0"`. Migration from v1.5 preserves user preferences
(non-destructive). Settings persist to disk via NeoModLoader's settings
store. New flags added by this fork (12 fields, listed in CHANGELOG
"Phase 0") follow the per-phase ship-gate ADR
([ADR-0005](./docs/adr/0005-default-on-flags-per-phase-ship-gate.md)) —
they ship default-OFF and flip to ON only after in-game smoke test.

## 6. Cache + lifecycle

Every mesh-producing system has an LRU+deferred-destroy cache (avoids
synchronous `Object.Destroy` of in-flight meshes):

`VoxelMeshCache` · `ProcGenCache` · `CrossedQuadMeshCache` · `RigCache` ·
`ImpostorBillboard` quad-atlas · `MeshInstanceBatcher._buckets`.

A single Harmony Prefix on `Core.Sphere.Finish` (`WorldUnloadPatch.cs`)
drains all of the above on world unload. New caches **must** register
here.

## 7. References

- [`PRD.md`](./PRD.md) — product requirements
- [`PLAN.md`](./PLAN.md) — implementation plan
- [`FUNCTIONAL_REQUIREMENTS.md`](./FUNCTIONAL_REQUIREMENTS.md) — FR-WSM-NNN traceability
- [`ADR.md`](./ADR.md) — architectural decisions index
- `docs/phase[1..10]-architecture.md` — per-phase technical detail
- `docs/phenotype-conventions.md` — org-wide repo conventions
- `CLAUDE.md` — agent-facing operating notes
