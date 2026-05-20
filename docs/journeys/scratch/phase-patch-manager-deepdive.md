# PhasePatchManager Deep Dive

Scope: `WorldSphereMod/Code/PhasePatchManager.cs` and the current `WorldSphereMod/Code/*.cs` tree.

## 1) Scan coverage this session

`PhasePatchManager.GetPhaseTypes()` scans `typeof(PhaseAttribute).Assembly.GetTypes()` and yields only types that have both `[Phase(...)]` and `[HarmonyPatch]` (`Core.cs:125-139`, `PhasePatchManager.cs:84-91`).

Current phase-tagged Harmony candidates found in the tree:

- `VoxelEntities`: `VoxelRender.ActorVoxelEmit` (`WorldSphereMod/Code/Voxel/VoxelRender.cs:284`) and `VoxelRender.BuildingVoxelEmit` (`WorldSphereMod/Code/Voxel/VoxelRender.cs:440`)
- `ProceduralBuildings`: `BuildingProcRender.ProcMeshEmit` (`WorldSphereMod/Code/ProcGen/BuildingProcRender.cs:14`)
- `CrossedQuadFoliage`: `FoliageTileRender` (`WorldSphereMod/Code/Foliage/FoliageTileRender.cs:28`) and `WallTileRender` (`WorldSphereMod/Code/Foliage/WallTileRender.cs:25`)
- `MeshWater`: `WaterRender.BeginPostfix`, `FinishPrefix`, `ColorSuppression`, `UpdateBaseLayerPostfix`, `UpdateScalePostfix` (`WorldSphereMod/Code/Water/WaterRender.cs:35, 52, 65, 83, 96`)
- `WorldspaceUI`: `SelectionHooks` (`WorldSphereMod/Code/Worldspace/SelectionHooks.cs:20`)

That is 11 phase-tagged Harmony types total. I did not find any extra `[Phase]` candidates outside those five flags.

## 2) Empty candidate phases

No `[Phase]`-tagged Harmony classes exist for `HighShadows`, `SkeletalAnimation`, `DayNightCycle`, `PostFX`, or `ParticleEffects` in the current tree. The empty `DayNightCycle` / `PostFX` / `ParticleEffects` lists are therefore by design, not a missing Harmony patch class.

## 3) Do driver-based phases consume their flag?

Yes, but not through `PhasePatchManager`.

- `HighShadows` is consumed directly by lighting startup: `SunDriver.Init()` calls `ShadowCascadeConfig.Apply(Core.savedSettings.HighShadows)` (`WorldSphereMod/Code/Lighting/SunDriver.cs:18, 41`).
- `SkeletalAnimation` is consumed inline inside the always-on voxel actor path: `VoxelRender.ActorVoxelEmit` checks `Core.savedSettings.SkeletalAnimation` before routing to `RigDriver` (`WorldSphereMod/Code/Voxel/VoxelRender.cs:320`).
- `DayNightCycle` is consumed by `Mod.Init()` and the lighting drivers: `TimeOfDay.EnsureCreated()` and `ProceduralSky.EnsureCreated()` are only created when the flag is on, and `TimeOfDay.Update()` keeps reading/writing fog/time when it is enabled (`WorldSphereMod/Code/Mod.cs:75-79`, `WorldSphereMod/Code/Lighting/TimeOfDay.cs:19, 48-50`, `WorldSphereMod/Code/Lighting/ProceduralSky.cs:19`).
- `PostFX` is consumed by `PostFxController.Create()` / `ApplySetting()` and the voxel flush path (`WorldSphereMod/Code/Fx/PostFxController.cs:134-136, 283`, `WorldSphereMod/Code/Voxel/VoxelRender.cs:598`).
- `ParticleEffects` is consumed in `Effects.cs` after `BaseEffectController.GetObject` creates the effect (`WorldSphereMod/Code/Effects.cs:202-207`).

Net: the empty candidate logs are expected for the driver/controller phases. The only actual PhasePatchManager candidates in this session are the five Harmony-backed phase families listed above.
