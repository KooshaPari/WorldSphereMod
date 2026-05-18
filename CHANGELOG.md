# Changelog

All notable changes to **WorldSphereMod3D** (the `claude/research-ultraplan-fork-DdgI5` branch on `KooshaPari/WorldSphereMod`) are tracked here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This branch's PR is [#1](https://github.com/KooshaPari/WorldSphereMod/pull/1).

## [Unreleased]

### Added — Phase 0 (fork plumbing)
- Rebrand to `WorldSphereMod3D` with GUID `worldsphere3d.fork` (co-installable with upstream)
- `Directory.Build.props` for cross-OS WorldBox path resolution (`$(WorldBoxPath)` / `WORLDBOX_PATH` env)
- CI workflow building the Unity-free `WorldSphereAPI` project on every push
- `SavedSettings` extended with 12 new phase-gating flags (all defaulted OFF until each phase ships)
- `WorldSphereAPI` v2: `IsModel3D`, `RegisterCustomMesh`, `RegisterBuildingRules`, `OnTimeOfDayChanged` (backwards-compatible with v1 external mods)

### Added — Phase 1 (voxel actors + buildings)
- `Voxel/SpriteVoxelizer.cs` with greedy meshing (~5-10× vertex reduction over naive per-texel emit)
- `Voxel/VoxelMeshCache.cs` — LRU cache with lock + deferred destroy
- `Voxel/MeshInstanceBatcher.cs` — `Graphics.DrawMeshInstanced` wrapper with properly-sized per-batch color array
- `Voxel/VoxelRender.cs` — Harmony Postfix on `ActorManager.precalculateRenderDataParallel` + `BuildingManager.precalculateRenderDataParallel`; `VoxelFrameDriver` MonoBehaviour
- `Voxel/SpriteVoxelizer.BuildPerTexel(...)` — non-greedy variant for per-voxel bone-index mapping (Phase 6 consumer)

### Added — Phase 2 (procedural building meshes)
- `ProcGen/BuildingMeshGen.cs` — footprint extrusion + multi-story inference + door/window detection + flat/gable/hipped roof inference
- `ProcGen/ProcGenCache.cs` — same LRU+deferred-destroy shape as `VoxelMeshCache`
- `ProcGen/BuildingRules.cs` — data types, `BuildingRulesLoader`, `BuildingRulesRegistry`, `BuildingShape` enum
- `ProcGen/BuildingProcRender.cs` — Postfix on `BuildingManager.precalculateRenderDataParallel`
- Public API: `WorldSphereModAPI.RegisterBuildingRules(string assetId, object rules)` (internal + external delegate binding)

### Added — Phase 3 (foliage)
- 3a: `Foliage/CrossedQuadMesher.cs` + `CrossedQuadMeshCache.cs` + `FoliageMaterial.cs` + `WindSwayDriver.cs` — tree/bush/rock heuristic routing
- 3b: `Foliage/FoliageTileRender.cs` — Prefix on `WorldTilemap.renderTile` for grass/life/road surface overlays
- 3b: `Foliage/WallTileRender.cs` — Prefix on `QuantumSpriteLibrary.drawWallType` for wall prisms
- `Resources/Shaders/FoliageWind.shader` source (placeholder; lit variant pending Phase 5b)

### Added — Phase 4-lite (mesh water)
- `Water/WaterMaskBuffer.cs` — per-tile depth `float[]`, CPU height-ID threshold
- `Water/WaterSurface.cs` — `MonoBehaviour` with X-wrap seam vertex dedup, per-renderer instance material
- `Water/WaterRender.cs` — `Sphere.Begin/Finish` lifecycle + alpha suppression on `SphereTileColor` + tile-change invalidation on `UpdateBaseLayer`/`UpdateScale` + runtime toggle support
- `Resources/Shaders/WaterGerstner.shader` source (URP forward, Gerstner waves, Fresnel cubemap, screen-space foam)

### Added — Phase 5-lite (sun + cascaded shadows)
- `Lighting/SunDriver.cs` — `LightingRoot` GameObject + `Sun` directional light at `TimeOfDay=11`
- `Lighting/ShadowCascadeConfig.cs` — fully reflective URP pipeline-asset configurator (WorldBox ships no URP runtime DLLs in `Managed/`)

### Added — Phase 6 (skeletal animation)
- `Rig/BoneDefinition.cs` — `BoneId` byte enum (12 humanoid + 9 quadruped slots), `BoneDefinition` struct, `SkinnedVoxelMesh` struct
- `Rig/RigCache.cs` — LRU cache mirroring `VoxelMeshCache`
- `Rig/HumanoidRig.cs` — 12-bone bind-pose + deterministic `SegmentVoxels` pixel-region heuristic + `Evaluate` 2D→3D bone-rotation projection from `AnimationFrameData` (prone-posture branch live; arm-swing/leg-stride dormant pending WorldBox field exposure)
- `Rig/RigDriver.cs` — CPU bind-pose path + full GPU compute-skinning path with per-(sprite,rig) buffers, shared matrices buffer, try/catch fallback, `ReleaseGpuMesh(key)` eviction hook
- `Resources/Shaders/VoxelSkin.compute` — single-kernel rigid-skinning compute shader

### Added — Phase 7 (worldspace UI)
- `Worldspace/WorldUIRenderer.cs` — actor-follow-rig `MonoBehaviour`
- `Worldspace/NameplateWorld.cs` — UI.Text world-canvas with distance fade
- `Worldspace/HealthBar.cs` — quad with shared static mesh + cached `Actor.getHealthRatio()` MethodInfo
- `Worldspace/SelectionRing.cs` — procedural torus mesh + quantised-radius cache
- `Worldspace/DamagePopup.cs` — pool of 64 world-canvas TMPs with lazy camera assignment
- `Worldspace/SelectionHooks.cs` — Postfixes on static `SelectedUnit.select`/`unselect`/`removeSelected`/`clear`

### Added — Phase 8 (day/night)
- `Lighting/TimeOfDay.cs` — autonomous driver with `MapBox.world_time` reflection probe + fallback
- `Lighting/SunRig.cs` — 4-anchor color-temperature gradient (night/dawn/noon/dusk)
- `Lighting/ProceduralSky.cs` — runtime `Material` swap on `RenderSettings.skybox`
- `Resources/Shaders/ProceduralSky.shader` — 3-color gradient + sun disc (Hosek-Wilkie keyword reserved)

### Added — Phase 9 (particles + decals + post-FX)
- `Fx/ParticleEffectLibrary.cs` — pool of 16 `ParticleSystem`s + 5-effect burst table + VFX Graph capability probe
- `Fx/DecalPool.cs` — 3 sub-pools (Footprint 32, Scorch 16, Blood 32) of flat quads with TTL expiry
- `Fx/PostFxController.cs` — fully reflective URP `Volume` + `VolumeProfile` (Bloom + ColorAdjustments + Vignette)
- `Fx/EffectPatches9.cs` — `Sphere.Begin/Finish` lifecycle
- `Effects.cs` integration: particle-burst suppression on sprite renderer + recycle re-enable

### Added — Phase 10 (LOD + impostor + profiler)
- `LOD/LodSelector.cs` — 3-tier `Voxel`/`Proxy`/`Impostor` with 3-frame hysteresis + `ImpostorOnlyMode` flag + per-instance `Remove(int)` cleanup
- `LOD/FrustumCuller.cs` — frame-cached `GeometryUtility.CalculateFrustumPlanes` wrapper
- `LOD/ImpostorBillboard.cs` — sprite-keyed quad atlas
- `Perf/FrameProfiler.cs` — per-system stopwatch accumulator + 1s window flush
- `Perf/ProfilerFrameDriver.cs` — `MonoBehaviour` driving the flush
- `Mod.cs` graduated hardware gate: missing compute/indirect-args sets `ImpostorOnlyMode = true` instead of throwing

### Fixed
- `MeshInstanceBatcher.Flush` color array tail garbage (last partial batch's tint was wrong)
- `VoxelRender` yaw-only rotation (Z/X lean axes were toppling voxel bodies during walking)
- `VoxelMeshCache` thread-safety + deferred mesh destroy (sync `Object.Destroy` in `Evict` could destroy in-flight meshes)
- `VoxelRender._material` reset on world reload
- All cache `Evict` methods: O(N log N) sort → O(N) two-pass min/max + threshold
- HealthBar shared static mesh + material (was allocating per actor)
- HealthBar reflection: cached `MethodInfo` for `getHealthRatio` (was lookup-per-frame per-actor)
- WaterSurface: per-renderer instance material (was mutating shared template), X-wrap seam vertex dedup, Destroy releases material
- WaterRender: runtime `MeshWater` toggle via `UpdateLifecycle`, tile-change invalidation Postfixes
- SelectionRing.Clear destroys cached meshes + material
- WorldUIRenderer.OnWorldUnload wired into `WorldUnloadPatch`
- `SettingsVersion 1.5 → 2.0` to match `SavedSettings.Version` (was a mismatched-reset loop)
- Settings migration: v1.5 → v2.0 preserves user preferences instead of resetting
- `LodSelector._hyst` reaped per-actor on `WorldUIRenderer.UnregisterActor`
- `ShadowCascadeConfig` `_hasOriginals` set once + never cleared (Reset→Apply→Reset no longer re-stashes mod values as "originals")
- `MeshInstanceBatcher._buckets` static dict cleared on world unload
- ProcGenCache.Clear routes through `_pendingDestroy` queue (was main-thread-only `Object.Destroy` under lock)
- RigCache eviction releases the matching `RigDriver` GpuMesh entry
- Default unimplemented phase flags flipped to `false` (was `true` for Phases 2-8 in initial scaffold)

### Docs
- `docs/phase[1-10]-architecture.md` — 10 architecture docs
- `docs/phase1-review.md`, `docs/phase3-decompile-findings.md`, `docs/render-data-fields.md` — investigation findings
- `docs/phase5-prep.md` — Compound-Spheres-3D research
- `docs/smoke-test-phase1.md` — in-game test checklist
- `docs/CONTRIBUTING.md`, `docs/PR_CHECKLIST.md`, `docs/performance.md`, `docs/HANDOFF.md`
- `docs/phenotype-conventions.md` — org-baseline reference (added during Phenotype retrofit)
- `docs/phenotype-baseline.md` — convention-compliance checklist
- `docs/journeys/*` — 5 user-journey pages
- `docs/adr/*` — 5 backfilled architecture decision records + template

### Internal
- `Tools/install.ps1`, `Tools/uninstall.ps1` — Windows-first dev tooling
- `WorldSphereTester/` — external API regression harness mod
- `WorldUnloadPatch` — centralized Harmony Prefix on `Sphere.Finish` draining 10 fork-side caches
- `tests/{Unit,Integration,E2E}/` — xUnit test projects + StrykerConfig + COVERAGE plan (Phenotype retrofit)
- `Taskfile.yaml`, `Justfile` — go-task + just runners (Phenotype retrofit)
- `.github/workflows/{build,test-gate,lint-gate,docs-build-gate}.yml` — quality gates (Phenotype retrofit)
- `CHANGELOG.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md`, `RELEASING.md`, `VERSION`, `AGENTS.md`, `GEMINI.md`, `LICENSE` — governance baseline (Phenotype retrofit)

[Unreleased]: https://github.com/KooshaPari/WorldSphereMod/compare/main...claude/research-ultraplan-fork-DdgI5
