# Phase Visual Audit — WorldSphereMod3D

**Date:** 2025-05-25
**Build:** `dotnet build WorldSphereMod.csproj -c Release` — **0 warnings, 0 errors**
**Baseline:** Terrain visible, cameraToSurface=17, 3D mode active.

## Summary Table

| Phase | Setting Flag | Default | Harmony OK? | Shader OK? | Visual Output | Broken? | Fix Needed |
|---|---|---|---|---|---|---|---|
| VoxelEntities | `VoxelEntities` | **ON** | Yes (4 patches) | Partial | Yes — actors, buildings, drops, projectiles render as voxel cubes | Minor | Bundle shader `OpaqueVertexColor` resolves; fallback is `Standard` + emission=1.5 (washes out per-vertex color). Fix: ensure `wsm3d-shaders` bundle loads on target platform so inline shader is preferred over Standard. |
| ProceduralBuildings | `ProceduralBuildings` | OFF | Yes (1 patch) | Reuses VoxelRender material | Conditional | Minor | Renders if both `ProceduralBuildings` AND `VoxelEntities` material resolves. `BuildingStyleProcgen=false` (default) falls back to voxelized sprite, not true procgen mesh. CrossedQuad/Single shapes delegate to FoliageMaterial. True procgen (`BuildingStyleProcgen=true`) uses `ProcGenCache.GetOrGenerate` which works. No unique shader needed. |
| CrossedQuadFoliage | `CrossedQuadFoliage` | OFF | Yes (2 patches: FoliageTileRender, WallTileRender) | Partial | Yes — trees/bushes as voxel blobs, walls as prisms, roads as flat decals | Minor | `FoliageWind` shader NOT in the 3-shader bundle (`OpaqueVertexColor`, `GerstnerWater`, `ColorGradingLUT`). Falls back to `Sprites/Default` + emission=1.5 belt-suspenders. Works but no wind animation. Fix: bake `FoliageWind` into `wsm3d-shaders` bundle. |
| MeshWater | `MeshWater` | OFF | Yes (5 patches) | Partial | Yes — water surface mesh replaces flat tile coloring | Minor | `GerstnerWater` IS in the bundle (1 of 3 baked shaders). If bundle loads, Gerstner wave displacement works. Fallback chain: `Standard` / URP Lit/Unlit with transparent blue tint. Cubemap reflections only if ProceduralSky is active. Bobbing disabled (BobAmplitude=0) due to sphere geometry issues. |
| MountainSlopeSmoothing | `MountainSlopeSmoothing` | OFF | Yes (1 patch) | Partial | Yes — bilinear height-interpolated overlay mesh at cliff transitions | Minor | Uses `OpaqueVertexColor` from bundle (same as voxels). Fallback to `Standard` works but may appear dark. Emission set to 0.15 (subtle). Mesh is a single `MeshRenderer` object, not instanced. Only covers tiles adjacent to height transitions (>1 unit delta). |
| HighShadows | `HighShadows` | OFF | Yes (SunDriver class-level `[Phase]`) | N/A (uses Unity Light) | Yes — directional sun light + soft shadow cascades | No | Creates a `Light` component directly (no custom shader needed). Shadow cascade config applied via `QualitySettings`. Sun tracks camera position. Works correctly. Requires `SunDriver.Init()` to have been called (happens in `Core.Init` flow). |
| HdrSkybox | `HdrSkybox` | OFF | Yes (CubemapLighting class-level `[Phase]`) | N/A (uses RenderSettings) | Partial | Minor | Loads cubemap from `Resources/Cubemap/sky-default`. If missing (likely — no `.cubemap` asset shipped in Resources), falls back to Skybox-derived ambient+reflection mode. No custom shader. The visual change is ambient lighting mode switch (`AmbientMode.Skybox` + reflection intensity=1). Likely invisible without a shipped cubemap asset — just adjusts Unity's existing skybox reflection. |
| SkeletalAnimation | `SkeletalAnimation` | OFF | Yes (RigDriver class-level `[Phase]`) | Partial | Conditional | Moderate | Creates per-actor `SkinnedMeshRenderer` hierarchies with bone transforms. Uses `OpaqueVertexColor` from bundle with instancing=false (required for skinning). ONLY works for `RigType.Humanoid` actors (resolved via `Constants.ResolveActorRig`). Non-humanoid rigs fall back to static voxel. Walk animation driven by `actor.current_position` vs `actor.next_step_position` delta. Material falls back to `Standard` + emission=1.5 if bundle missing. Bone weights are 100% single-bone (no blending). **Potential issue:** bone definitions in `HumanoidRig.Bones` are hardcoded proportions — may not match all actor sprite proportions, causing limb misalignment. |
| WorldspaceUI | `WorldspaceUI` | OFF | Yes (SelectionHooks: 4 patches on SelectedUnit) | N/A (mesh-based) | Yes — per-actor rig graph with nameplates, HP bars, selection rings, damage popups | Minor | Nameplate requires sub-flag `WorldspaceLabel3D=false` (default OFF). Health bar has two modes: legacy (Sprites/Default quad) or 3D (WorldspaceHealth3D flag, uses voxel material). Selection ring and damage popup are mesh-based. `TextMesh3D` type resolved via reflection — if WorldBox doesn't ship it, falls back to Unity UI `Text` on a WorldSpace Canvas. **Issue:** WorldSpace Canvas labels can be very small or invisible at strategy zoom without careful sizing. |
| DayNightCycle | `DayNightCycle` | OFF | Yes (TimeOfDay class-level `[Phase]`) | Partial | Yes — time-of-day driven sun rotation, fog color, sky colors | Moderate | `TimeOfDay` MonoBehaviour drives `SunDriver.TimeOfDay` and applies fog. ProceduralSky component is created when DayNightCycle is ON (see `ProceduralSky.EnsureCreated` guard). **ProceduralSky shader:** NOT in the 3-shader bundle. Checks `LoadedShaders["ProceduralSky"]` (miss), then `Shader.Find("WSM3D/ProceduralSky")` (miss), then `Resources.Load`. If all fail, falls back to vanilla skybox (no procedural gradient). **Fix needed:** bake `ProceduralSky.shader` into `wsm3d-shaders` bundle (source exists at `AssetBundles/Shaders/ProceduralSky.shader`). Without it, sky doesn't change color — only fog and sun direction/color animate. |

## Detailed Phase Notes

### Phase 1: VoxelEntities (DEFAULT ON)

**Harmony patches (4):**
- `ActorVoxelEmit` — Postfix on `ActorManager.precalculateRenderDataParallel`
- `BuildingVoxelEmit` — Postfix on `BuildingManager.precalculateRenderDataParallel`
- `DropVoxelEmit` — Postfix on `Drop.updatePosition`
- `ProjectileVoxelEmit` — Postfix on `QuantumSpriteLibrary.drawProjectiles`

**Shader chain:** `OpaqueVertexColor` (bundle) > `Particles/Standard Surface` > `Particles/Standard Unlit` > URP variants > `Unlit/Texture` > `Unlit/Color` > `Standard` (last resort). WorldBox uses built-in pipeline, so URP/Particles shaders return MISSING. Effective resolution: `OpaqueVertexColor` if bundle loads, otherwise `Standard` + emission boost.

**Known working:** Actors, buildings, drops, projectiles all render as colored voxel cubes. LOD system routes to Impostor billboard at distance. Frustum culling applied. Y-lift prevents terrain embedding. Sprite suppression (has_normal_render=false / scales=zero) hides 2D sprites after 3D draw.

### Phase 2: ProceduralBuildings (DEFAULT OFF)

**Harmony patches (1):**
- `ProcMeshEmit` — Postfix on `BuildingManager.precalculateRenderDataParallel`

**Logic:** When `ProceduralBuildings` is ON, this patch takes over from `BuildingVoxelEmit` (which early-returns). Routes buildings by `BuildingShape`: CrossedQuad/Single shapes go through `FoliageMaterial`+`CrossedQuadMeshCache`. Box shapes go through either `ProcGenCache` (if `BuildingStyleProcgen=true`) or plain `VoxelMeshCache`. No unique shader.

### Phase 3: CrossedQuadFoliage (DEFAULT OFF)

**Harmony patches (2):**
- `FoliageTileRender` — Prefix on `WorldTilemap.renderTile` (skips vanilla tile flush for foliage)
- `WallTileRender` — Prefix on `QuantumSpriteLibrary.drawWallType` (replaces wall sprites with prisms)

**Shader:** `FoliageWind` not bundled. Falls back to `Sprites/Default` with emission=1.5. Foliage tiles (grass, life, road) get voxel blob meshes. Roads get flat decals. Walls get extruded prisms. Animated walls fall through to vanilla.

### Phase 4: MeshWater (DEFAULT OFF)

**Harmony patches (5):**
- `BeginPostfix` on `Core.Sphere.Begin`
- `FinishPrefix` on `Core.Sphere.Finish`
- `ColorSuppression` on `CompoundSphereScripts.SphereTileColor`
- `UpdateBaseLayerPostfix` on `Core.Sphere.UpdateBaseLayer`
- `UpdateScalePostfix` on `Core.Sphere.UpdateScale`

**Shader:** `GerstnerWater` IS bundled. Transparent blue surface with Gerstner wave params. Full mask rebuild on tile changes. Water tile alpha set to 0 to hide flat tile color.

### Phase 5: MountainSlopeSmoothing (DEFAULT OFF)

**Harmony patches (1):**
- `MountainSlopeRedrawPatch` — Postfix on `WorldTilemap.redrawTiles`

**Mechanism:** Not instanced. Single `MeshRenderer` with a generated mesh. Bilinear interpolation of corner heights at cliff transitions. SubDiv=2 (4 sub-quads per tile). Height bias of 0.02 to avoid z-fighting with flat terrain. Rebuilds on `redrawTiles` events + per-frame dirty check.

### Phase 6: HighShadows (DEFAULT OFF)

**No custom shader.** Creates a Unity `Light` (Directional, Soft shadows). Shadow strength/bias adjusted by toggle. Cascade config via `QualitySettings`. Sun position tracks camera. TimeOfDay controls rotation if DayNightCycle is also on.

**Note:** `SunDriver` is `[Phase(HighShadows)]` but `SunDriver.Init()` is called unconditionally in the startup flow — the `[Phase]` attribute gates whether the Harmony auto-patching picks up any patches inside the class. Since SunDriver has no `[HarmonyPatch]` attributes directly, the Phase gate is a no-op defense. The actual guard is `if (!Core.IsWorld3D) return` inside `Init()`.

### Phase 7: HdrSkybox (DEFAULT OFF)

**No custom shader.** Switches `RenderSettings.ambientMode` to Skybox and sets reflection intensity. Tries to load a cubemap from Resources — likely missing (no `.cubemap` asset in the shipped Resources). Falls back gracefully to skybox-derived mode. Visual impact: subtle ambient lighting change.

### Phase 8: SkeletalAnimation (DEFAULT OFF)

**One per-actor SkinnedMeshRenderer.** Bone hierarchy from `HumanoidRig.Bones`. Walk cycle driven by position delta. Single bone weight per vertex (no blending). GPU procedural skinning available behind `GpuProceduralSkinning` sub-flag. Material: `OpaqueVertexColor` with instancing=false (SkinnedMeshRenderer requires non-instanced material).

**Potential issue:** RigCache builds a `SkinnedVoxelMesh` per sprite, but bone indices are assigned by vertex position heuristic — may produce incorrect limb assignments for sprites with unusual proportions.

### Phase 9: WorldspaceUI (DEFAULT OFF)

**Harmony patches (4):** On `SelectedUnit.select/unselect/removeSelected/clear`.

**Sub-systems:**
- `WorldUIRenderer` — per-actor rig graph, LateUpdate positioning
- `NameplateWorld` — name labels (WorldspaceLabel3D sub-flag, default OFF)
- `HealthBar` — HP bars (WorldspaceHealth3D sub-flag for 3D mode)
- `SelectionRing` — ring on selected actors
- `DamagePopup` — floating damage numbers

All mesh-based, no custom shaders. HP bar uses `Sprites/Default` or voxel material depending on mode.

### Phase 10: DayNightCycle (DEFAULT OFF)

**TimeOfDay MonoBehaviour** drives the clock. Syncs with WorldBox `world_time` field via reflection when available. Drives:
- `SunDriver.TimeOfDay` — sun rotation angle
- `SunRig.Drive` — sun color + ambient color
- `RenderSettings.fog` — exponential squared fog with time-varying color
- `ProceduralSky` — sky gradient colors (IF shader resolves)

**Critical gap:** `ProceduralSky.shader` source exists in `AssetBundles/Shaders/` but is NOT in the baked bundle (only `OpaqueVertexColor`, `GerstnerWater`, `ColorGradingLUT` are baked). Without it, the procedural sky gradient is completely absent — only fog color and sun color/direction animate.

## Bundle Shader Status

| Shader | In wsm3d-shaders bundle? | Consumers | Status |
|---|---|---|---|
| OpaqueVertexColor | **YES** | VoxelRender, RigDriver, MountainSlope, ImpostorBillboard | Working |
| GerstnerWater | **YES** | WaterSurface | Working |
| ColorGradingLUT | **YES** | ColorGradingLUT, WSM3DPostStack | Working (PostFX phase) |
| FoliageWind | NO (source exists) | FoliageMaterial | Falls back to Sprites/Default — no wind sway |
| ProceduralSky | NO (source exists) | ProceduralSky | Falls back to vanilla skybox — no gradient |
| Impostor | NO (source exists) | ImpostorBillboard | Falls back to OpaqueVertexColor (works) |
| ScreenSpaceAO | NO (source exists) | PostFx/ScreenSpaceAO | Falls back gracefully |
| StratumVoxelPBR | NO (source exists) | None currently | Future use |

## Recommended Priority Fixes

1. **Bake ProceduralSky + FoliageWind into wsm3d-shaders bundle** — these are the two most impactful missing shaders. ProceduralSky enables the full day/night sky experience; FoliageWind enables wind animation on trees.
2. **Ship a cubemap asset for HdrSkybox** — without it, the HdrSkybox toggle is nearly invisible.
3. **Verify SkeletalAnimation bone mapping** — bone index assignment is positional heuristic; needs in-game visual validation against diverse actor sprites.
4. **WorldspaceUI label sizing** — Canvas-based fallback labels may be invisible at strategy zoom. Needs camera-distance-adaptive scaling.
