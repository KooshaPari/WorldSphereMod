# Settings Audit

| Flag Name | Group (Rendering Phase / Quality / Debug / Deprecated) | What it does (1 line) | Suggested clearer name | Action (keep/rename/remove/merge) |
|---|---|---|---|---|
| `AutoScreenshotEnabled` | Debug | Automatically captures screenshots on a timer. | `EnableAutoScreenshots` | rename |
| `AutoScreenshotIntervalSeconds` | Debug | Sets the delay between automatic screenshots. | `AutoScreenshotPeriodSeconds` | keep |
| `AutoScreenshotPath` | Debug | Directory where automatic screenshots are written. | `AutoScreenshotDirectory` | rename |
| `Version` | Deprecated | Stores the saved-settings schema version string. | `SettingsVersion` | keep |
| `Is3D` | Rendering Phase | Toggles the 3D presentation mode. | `Enable3DMode` | rename |
| `InvertedCameraMovement` | Rendering Phase | Inverts camera movement input direction. | `InvertCameraMovement` | keep |
| `PerlinNoise` | Rendering Phase | Enables Perlin noise-based world variation. | `EnablePerlinNoise` | keep |
| `UpsideDownMovement` | Rendering Phase | Reverses movement to match upside-down world behavior. | `InvertWorldMovement` | rename |
| `RotateStuffToCamera` | Rendering Phase | Rotates objects to face the camera. | `BillboardToCamera` | rename |
| `CameraRotatesWithWorld` | Rendering Phase | Makes the camera rotate with world motion. | `RotateCameraWithWorld` | rename |
| `FirstPerson` | Rendering Phase | Uses a first-person camera mode. | `EnableFirstPersonView` | keep |
| `RenderRange` | Quality | Controls how far the world is rendered. | `RenderDistanceScale` | rename |
| `TileHeight` | Rendering Phase | Sets the world tile height scale. | `TileVerticalScale` | rename |
| `BuildingSize` | Rendering Phase | Sets the base scale used for buildings. | `BuildingScale` | keep |
| `CurrentShape` | Rendering Phase | Stores the active world-shape mode. | `WorldShape` | rename |
| `VoxelEntities` | Rendering Phase | Renders actors, items, and projectiles as voxel meshes. | `EnableVoxelEntities` | keep |
| `UseBRG` | Quality | Uses Unity BatchRendererGroup for batching. | `EnableBatchRendererGroup` | rename |
| `VoxelMeshSmoothing` | Quality | Applies Laplacian smoothing to generated voxel meshes. | `EnableVoxelMeshSmoothing` | keep |
| `EnableMcPackTextures` | Quality | Enables Minecraft-pack texture support. | `EnableMcPackTextures` | keep |
| `ForceFallbackDrawPath` | Debug | Forces the fallback per-instance draw path instead of the fast path. | `ForceFallbackDrawPath` | keep |
| `SmoothingIterations` | Quality | Sets the number of smoothing passes for voxel output. | `VoxelSmoothingIterations` | rename |
| `VoxelSpriteDepth` | Quality | Sets the extrusion depth for sprite-to-voxel conversion. | `SpriteVoxelDepth` | rename |
| `VoxelColorTonemap` | Quality | Tonemaps voxel colors for more natural output. | `ApplyVoxelColorTonemap` | keep |
| `VoxelInflationStyle` | Quality | Chooses how sprite voxels are inflated into 3D volume. | `VoxelExtrusionStyle` | rename |
| `VoxelScaleMultiplier` | Quality | Scales the generated voxel geometry. | `VoxelScale` | rename |
| `DebugVoxelOutline` | Debug | Draws debug outlines around voxel geometry. | `ShowVoxelOutlineDebug` | keep |
| `DebugSanityCube` | Debug | Spawns a sanity-check cube for debugging. | `ShowSanityCubeDebug` | keep |
| `DebugSpawnBuildings` | Debug | Spawns buildings for debugging the generation path. | `DebugSpawnBuildings` | keep |
| `AutoTest` | Debug | Runs automated phase validation at startup. | `EnableAutoTest` | keep |
| `ProceduralBuildings` | Rendering Phase | Renders buildings as procedural meshes instead of sprites. | `EnableProceduralBuildings` | keep |
| `BuildingStyleProcgen` | Rendering Phase | Keeps the older stylized procgen building path. | `UseProcgenBuildingStyle` | rename |
| `CrossedQuadFoliage` | Rendering Phase | Uses crossed-quads for foliage instead of flat billboards. | `EnableCrossedQuadFoliage` | keep |
| `BiomeBlending` | Quality | Blends biome colors across tile boundaries. | `EnableBiomeColorBlending` | rename |
| `MeshWater` | Rendering Phase | Renders water as a mesh surface instead of a flat tile. | `EnableMeshWater` | keep |
| `WorldspaceHealth3D` | Rendering Phase | Uses 3D worldspace health bars instead of billboard quads. | `Enable3DHealthBars` | rename |
| `MountainSlopeSmoothing` | Quality | Smooths mountain slopes and cliff transitions. | `EnableMountainSlopeSmoothing` | keep |
| `HighShadows` | Quality | Enables higher-quality directional shadows. | `EnableHighQualityShadows` | rename |
| `HdrSkybox` | Quality | Enables HDR skybox reflections. | `EnableHdrSkybox` | keep |
| `ColorGradingLut` | Quality | Enables LUT-based color grading. | `EnableColorGradingLut` | keep |
| `SkeletalAnimation` | Rendering Phase | Drives voxel actors with skeletal animation. | `EnableSkeletalAnimation` | keep |
| `WorldspaceUI` | Rendering Phase | Enables worldspace UI elements such as nameplates and rings. | `EnableWorldspaceUI` | keep |
| `WorldspaceLabel3D` | Rendering Phase | Uses 3D worldspace labels instead of legacy nameplate text. | `Enable3DWorldspaceLabels` | rename |
| `DayNightCycle` | Rendering Phase | Enables the day/night cycle, procedural sky, and fog. | `EnableDayNightCycle` | keep |
| `FogDensity` | Quality | Sets the density of world fog. | `WorldFogDensity` | keep |
| `PostFX` | Quality | Enables post-processing effects. | `EnablePostProcessing` | rename |
| `SSAOEnabled` | Quality | Enables screen-space ambient occlusion. | `EnableSSAO` | rename |
| `SSAOQuality` | Quality | Selects the quality level for SSAO. | `ScreenSpaceAocQuality` | keep |
| `SSGIEnabled` | Quality | Enables screen-space global illumination. | `EnableSSGI` | rename |
| `ParticleEffects` | Rendering Phase | Enables particle effects. | `EnableParticleEffects` | keep |
| `WeatherRain` | Rendering Phase | Enables rain weather effects. | `EnableRainWeather` | keep |
| `WeatherSnow` | Rendering Phase | Enables snow weather effects. | `EnableSnowWeather` | keep |
| `WeatherLightning` | Rendering Phase | Enables lightning weather effects. | `EnableLightningWeather` | keep |
| `LODScale` | Quality | Scales the LOD transition distance. | `LodDistanceScale` | rename |
| `WaterDetail` | Quality | Adjusts the amount of water surface detail. | `WaterDetailScale` | keep |
| `FoliageDensity` | Quality | Adjusts the density of rendered foliage. | `FoliageDensityScale` | keep |
| `ProfilerDump` | Debug | Shows the runtime performance overlay. | `EnableProfilerOverlay` | rename |
