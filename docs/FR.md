# WSM3D Functional Requirements (FR)

> **Status:** draft bootstrapped from `WorldSphereMod/Code/SavedSettings.cs` public
> fields and `WorldSphereMod/Code/WorldSphereAPI.cs` public surface on
> branch `wip/208-height-fix` (HEAD `08aa17f8`).
> Cross-referenced by `docs/requirements-traceability.md`.
> Convention: `FR-WSM-NNN` = one user-visible capability. Numbering preserved
> from the prior traceability table where possible; new IDs are appended.

## FR-WSM-001 — Voxel actor / item / projectile rendering
Voxelize sprites (actors, items, drops, projectiles, talk bubbles) into
instanced meshes. Controlled by `SavedSettings.VoxelEntities`,
`VoxelScaleMultiplier`, `ActorVoxelScaleFactor`, `VoxelSpriteDepth`,
`VoxelLuminanceDepth`, `VoxelInflationStyle`, `VoxelColorTonemap`,
`VoxelMeshSmoothing`, `UseBRG`, `ForceFallbackDrawPath`,
`ImpostorEmissionMultiplier`.

## FR-WSM-002 — Procedural building meshes
Replace billboarded building sprites with procgen prism + roof + door/window
meshes. Controlled by `SavedSettings.ProceduralBuildings`,
`BuildingStyleProcgen`, `BuildingRenderBudget`, `BuildingVoxelScaleFactor`.

## FR-WSM-003 — Per-sprite shape-hint routing
`AssetShapeRegistry` chooses voxelization style ("auto" / "pertexel" /
"greedy" / "balloon" / "organicblob" / "lathe") per sprite name. Surface:
`WorldSphereMod/Code/Import/AssetShapeRegistry.cs`,
`WorldSphereMod/Code/PhaseAttribute.cs`, `WorldSphereMod/Code/PhasePatchGate.cs`.

## FR-WSM-004 — LOD ladder + impostor fallback
LOD ladder: voxel-mesh near → low-poly proxy mid → impostor billboard far.
Hardware-gate fallback for compute-shader-less GPUs. Controlled by
`SavedSettings.LODScale`, `ImpostorEmissionMultiplier`.

## FR-WSM-005 — Mesh water surface
Animated mesh water (Gerstner waves) replacing tile-color water.
Controlled by `SavedSettings.MeshWater`, `WaterDetail`.

## FR-WSM-006 — Crossed-quad foliage + clouds
Top-tile foliage and effect clouds render as two perpendicular quads.
Controlled by `SavedSettings.CrossedQuadFoliage`, `FoliageDensity`,
`FoliageVoxelScaleFactor`.

## FR-WSM-007 — Cascaded shadow maps + directional sun
Real-time directional sun + 4-cascade URP shadow maps. Controlled by
`SavedSettings.HighShadows`. `HdrSkybox` / `ColorGradingLut` for
env reflections and LUT grading.

## FR-WSM-008 — Skeletal animation
Bone-driven voxel actor animation (humanoid 12-bone, quadruped 9-bone,
hand-rigged bosses). Controlled by `SavedSettings.SkeletalAnimation`,
`GpuProceduralSkinning`.

## FR-WSM-009 — Day/night cycle + procedural sky
TimeOfDay driver broadcasts phase 0..1 to subscribers. Procedural sky
(3-color gradient + sun disc). Exponential height fog. Controlled by
`SavedSettings.DayNightCycle`, `FogDensity`. Public event:
`WorldSphereModAPI.OnTimeOfDayChanged(float phase)`.

## FR-WSM-010 — Post-processing pipeline
URP post-FX volume: bloom, color grading, vignette, ACES tonemapping,
SSAO, SSGI. Controlled by `SavedSettings.PostFX`, `SSAOEnabled`,
`SSAOQuality`, `SSGIEnabled`, `BloomEnabled`, `ACESTonemapping`.

## FR-WSM-011 — Worldspace UI
Worldspace nameplates, health bars, selection rings, damage numbers.
Controlled by `SavedSettings.WorldspaceUI`, `WorldspaceLabel3D`,
`WorldspaceHealth3D`, `NameplateFadeNear`, `NameplateFadeFar`,
`NameplateReferenceDistance`, `NameplateMinScale`, `NameplateMaxScale`,
`NameplateBaseScale`.

## FR-WSM-012 — Voxel-mesh particle bursts
Meteorite, explosion, fire, antimatter, napalm effects as small voxel
mesh particles. Controlled by `SavedSettings.ParticleEffects`,
`WeatherRain`, `WeatherSnow`, `WeatherLightning`.

## FR-WSM-013 — Settings persistence across launches
`SavedSettings` JSON serialize/deserialize with phase default migration
(`ApplyLightweightPreset`, `ApplyPhaseDefaults`, `ApplyFullPreset`),
settings-version bump triggers migration on load.

## FR-WSM-014 — Bridge HTTP API
`WorldSphereMod/Code/Bridge/*` exposes a local HTTP server (Kestrel
or `HttpListener`) with phase health endpoints (`/phase/<name>`),
`/memory`, save/load hooks, settings POST, screenshot capture, action
endpoints. Public surface: `WorldSphereMod/Code/Bridge/BridgeServer.cs`,
`BridgeActions.cs`, `BridgeLoadSaveHooks.cs`, `BridgeSettingParser.cs`,
`BridgePerFrameTick.cs`.

## FR-WSM-015 — Clean mod init + public API surface
`Mod.cs` init/PostInit hooks, public API surface in
`WorldSphereMod/Code/WorldSphereAPI.cs`:
- `IsWorld3D()`, `GetVersion()`, `GetCapabilities()`, `HasFeature(name)`
- `MakeActorPerp / MakeBuildingPerp / MakeProjectilePerp`
- `EditEffect(id, isUpright, separate, extraHeight, onGround)`
- `GetSetting(name)` (reflection over `SavedSettings`)
- v2: `IsModel3D()`, `RegisterCustomMesh(assetId, mesh, albedo)`,
  `RegisterBuildingRules(assetId, rules)`, `OnTimeOfDayChanged` event

## FR-WSM-016 — Diagnostics / render-error observability
In-world error props when `SavedSettings.RenderErrorProps = true` (default),
per-object diagnostic overlay when `SavedSettings.RenderDiagOverlay = true`,
in-game IMGUI HUD toggled by F8 when `SavedSettings.DebugHUDVisible = true`.
Telemetry feeds `RenderErrorRegistry` and `/diag/errors` bridge endpoint.

## FR-WSM-017 — AutoTest + automated screenshot capture
`SavedSettings.AutoTest` flag, `AutoScreenshotEnabled`,
`AutoScreenshotIntervalSeconds`, `AutoScreenshotPath` drive in-game
`AutoScreenshotDriver` and `AutoTest` harnesses for CI regression.

## FR-WSM-018 — Input-capture substrate
`SavedSettings.InputCaptureEnabled` (default on) records user action
stream (clicks, tool, tile, camera moves, world create/load, speed) to
an append-only JSONL session log for headless replay via the bridge.

## FR-WSM-019 — Texture-pack (.mcpack) import
`SavedSettings.EnableMcPackTextures` enables runtime .mcpack texture
pack discovery/registration. Surface: `Import/TexturePackImporter.cs`.

## FR-WSM-020 — Continuous height-field terrain mesh
ADR-0017 M0 corner-averaged + analytic-normal + Perlin-displaced
heightfield mesh terrain. Controlled by `SavedSettings.UseHeightFieldTerrain`
(default on), `BiomeBlending`, `MountainSlopeSmoothing`.
