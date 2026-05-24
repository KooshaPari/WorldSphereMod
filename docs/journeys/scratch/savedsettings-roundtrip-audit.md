# SavedSettings Roundtrip Audit

## Verdict
`SavedSettings` roundtrips through Json.NET correctly. `SaveSettings()` writes the whole object, and `LoadSettings()` deserializes the same type back without a custom field map or `[JsonIgnore]` layer in between, so public fields serialize by name and missing JSON members fall back to the field initializers on the new instance. [Core.cs:30](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L30) [Core.cs:35](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L35) [SavedSettings.cs:5](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L5)

## Missing-Field Compatibility
Old `mods_config` JSON is forward-compatible here: `LoadSettings()` only rewrites disk on parse failure or when `Version` mismatches, and otherwise keeps the deserialized object as-is. Because the missing members are public fields with inline defaults, an older file that predates new phase flags will load with sensible in-memory defaults immediately. [Core.cs:43](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L43) [Core.cs:53](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L53) [SavedSettings.cs:7](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L7)

## Fields Added In The Last 30 Commits
Every field added in that window has an explicit default:
`VoxelEntities=false` [SavedSettings.cs:27](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L27),
`VoxelMeshSmoothing=false` [SavedSettings.cs:29](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L29),
`SmoothingIterations=1` [SavedSettings.cs:30](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L30),
`VoxelScaleMultiplier=8.0f` [SavedSettings.cs:31](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L31),
`DebugVoxelOutline=true` [SavedSettings.cs:32](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L32),
`DebugSanityCube=false` [SavedSettings.cs:33](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L33),
`DebugSpawnBuildings=false` [SavedSettings.cs:34](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L34),
`AutoTest=false` [SavedSettings.cs:35](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L35),
`ProceduralBuildings=false` [SavedSettings.cs:37](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L37),
`CrossedQuadFoliage=true` [SavedSettings.cs:39](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L39),
`MeshWater=false` [SavedSettings.cs:41](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L41),
`HighShadows=false` [SavedSettings.cs:43](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L43),
`SkeletalAnimation=false` [SavedSettings.cs:45](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L45),
`WorldspaceUI=true` [SavedSettings.cs:47](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L47),
`DayNightCycle=false` [SavedSettings.cs:49](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L49),
`FogDensity=0.0f` [SavedSettings.cs:50](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L50),
`PostFX=false` [SavedSettings.cs:52](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L52),
`ParticleEffects=true` [SavedSettings.cs:53](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L53),
`LODScale=1.0f` [SavedSettings.cs:55](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L55),
`WaterDetail=1.0f` [SavedSettings.cs:56](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L56),
`FoliageDensity=1.0f` [SavedSettings.cs:57](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L57),
`ProfilerDump=false` [SavedSettings.cs:59](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L59).

## Notes
The only potentially noisy default is `DebugVoxelOutline=true`; it is not a serialization bug, but it does mean an older config will immediately draw debug outlines when the voxel path is active. [SavedSettings.cs:32](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L32) [MeshInstanceBatcher.cs:226](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs#L226)

