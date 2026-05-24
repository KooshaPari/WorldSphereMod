# Settings flag gap — pale-blue / black-water / no-cubemap diagnosis 2026-05-22

User reported all visual regressions persisted across code fixes. Investigation
revealed: 8 of 15 phase flags in `mods_config/WorldSphereMod.json` were `false`
despite defaults being `true` in `SavedSettings.cs`.

## Flags that were FALSE in JSON (overriding True defaults)

- `MountainSlopeSmoothing` → no terrain smoothing
- `HdrSkybox` → pale blue Unity default skybox
- `MeshWater` → no mesh water (sprite water still shows)
- `SkeletalAnimation` → no limbed actors
- `BiomeBlending` → no biome transitions
- `HighShadows` → no high-quality shadows
- `SSGIEnabled` → no global illumination

## Flag legitimately FALSE (do NOT flip)

- `VoxelMeshSmoothing` → triangle-soup regression, default False per c714604

## Root cause

NML loads `WorldSphereMod.json` from mods_config/ AFTER `SavedSettings.cs`
sets defaults. Any field present in the JSON overrides the default, even if
the JSON was written by an older mod version that hadn't yet flipped those
flags to True-by-default. The JSON file becomes stale across mod versions.

## Fix

Re-flipped flags via PowerShell. Future-proof option: settings versioning
+ migration logic in `SavedSettings.cs` that bumps stale-flag defaults.
