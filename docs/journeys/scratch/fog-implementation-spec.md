# Fog Implementation Spec

Goal: turn `SavedSettings.FogDensity` into real atmospheric fog without changing the current startup gating or sky behavior.

## Current read sites

- `WorldSphereMod/Code/Lighting/TimeOfDay.cs:15-21` gates creation of `TimeOfDay` on `FogDensity == 0` only when `DayNightCycle` is also off.
- `WorldSphereMod/Code/Lighting/TimeOfDay.cs:48-52` is the only runtime write path today: it flips `RenderSettings.fog`, sets `RenderSettings.fogMode`, and assigns `RenderSettings.fogDensity`.
- `WorldSphereMod/Code/SavedSettings.cs:48-50` declares the setting and defaults it to `0.0f`.

## Shader hook

- The canonical fog consumer should be the opaque forward-lit world shader path, starting with `WorldSphereMod/Resources/Shaders/VoxelLit.shader:32-130` (`Pass "ForwardLit"`).
- `Standard` is only a fallback shader name in the material resolution chains, not the intended primary hook:
  - `WorldSphereMod/Code/Voxel/VoxelRender.cs:75-103`
  - `WorldSphereMod/Code/LOD/ImpostorBillboard.cs:44-67`
  - `WorldSphereMod/Code/Water/WaterSurface.cs:170-190`
- Spec choice: implement fog in the forward fragment path using world-space distance / depth, and mirror the same logic into any fallback material path only if that surface is still visible in the target build.

## ProceduralSky integration

- `WorldSphereMod/Code/Lighting/ProceduralSky.cs:39-57` already computes the day-phase sky colors from `TimeOfDay.Current` and `SunRig`.
- `WorldSphereMod/Resources/Shaders/ProceduralSky.shader:62-73` uses the same zenith / horizon / ground / sun inputs for the skybox.
- `WorldSphereMod/Code/Lighting/SunRig.cs:30-34` exposes `AmbientColor(t)`, and `WorldSphereMod/Code/Lighting/TimeOfDay.cs:48-52` already uses that ambient proxy for fog color.
- Spec choice: do not invent a separate fog palette. Derive fog tint from the same time-of-day sky model, ideally from the horizon / ambient blend, so the fog line matches the skybox at low altitude and dawn/dusk.

## Cost model

- Full-screen post-process: simplest way to get screen-space atmospheric fade, but it costs every pixel every frame and needs a depth texture. Use only if the design later needs strong volumetric layering across all transparent / screen-space content.
- Depth-bound forward fog: cheaper fit for this repo. It runs where geometry already shades, benefits from early-Z, and avoids spending work on empty sky pixels. This is the preferred implementation path for Phase 8.

## Spec decision

1. Keep `FogDensity` as the single user-facing scalar.
2. Keep `TimeOfDay` as the CPU-side driver for `RenderSettings` and sky color sync.
3. Add fog evaluation to the `VoxelLit` forward pass first.
4. Use the Phase 8 sky color curve as the fog tint source.
5. Treat a fullscreen fog pass as a later fallback, not the default design.
