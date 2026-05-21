# PLAN vs Actual Gap Audit

Scope: `PLAN.md` root plan vs current `WorldSphereMod` code. I treated “landed”
as “visible in the default install without extra manual toggles or missing
runtime assets.” Result: several plan claims are present in code but still
hidden by defaults, while two headline visuals are still missing outright:
terrain refinement and real cloud meshes.

## Top 10 “plan said landed, but actually broken”

1. Terrain smoothing / biome blending / mountain slope smoothing. `MeshSmoother`
   only runs on voxelized sprites via `VoxelMeshCache.Get()` (`WorldSphereMod/Code/Voxel/VoxelMeshCache.cs:201-206`);
   there is no terrain-mesh smoothing or biome-blend pass anywhere in `WorldSphereMod/Code`.
   `Tools.TrueHeight()` still just maps tile IDs to heights (`WorldSphereMod/Code/Tools.cs:336-419`).
   Code present: no. Flag: no. Gate: no.

2. Clouds as crossed-quads. The plan’s Phase 3 cloud claim is not implemented.
   `Effects.cs` still routes `Cloud.update` through sprite bookkeeping
   (`WorldSphereMod/Code/Effects.cs:258-260`), and `fx_cloud` only has a burst
   profile in `VoxelParticleBurst` (`WorldSphereMod/Code/Fx/VoxelParticleBurst.cs:43-47`).
   Code present: no cloud mesh path. Flag: no. Gate: only a sprite postfix.

3. Water waves. `WaterSurface` and `WaterGerstner.shader` are real
   (`WorldSphereMod/Code/Water/WaterSurface.cs:130-195`, `WorldSphereMod/Resources/Shaders/WaterGerstner.shader:1-101`),
   but `MeshWater` defaults off (`WorldSphereMod/Code/SavedSettings.cs:47`).
   The Harmony hooks do fire when enabled (`WorldSphereMod/Code/Water/WaterRender.cs:16-98`),
   but if the shader resource is missing at runtime, `EnsureMaterial()` falls
   back to stock lit/unlit materials and the waves disappear.

4. Fire/smoke visuals. Particle bursts exist, but only for a fixed ID set:
   `fx_meteorite`, `fx_explosion_wave`, `fx_fire_smoke`, `fx_antimatter_effect`,
   `fx_napalm_flash`, plus `fx_cloud` (`WorldSphereMod/Code/Fx/VoxelParticleBurst.cs:43-47`).
   `BaseEffectController.GetObject` does call `TryStart()` when `ParticleEffects`
   is on (`WorldSphereMod/Code/Effects.cs:187-209`), but there is no general
   smoke/fire system beyond those IDs. Code present: partial. Flag: `ParticleEffects=true`.
   Gate: yes, via the `BaseEffectController.GetObject` postfix.

5. Fog / day-night sky. `TimeOfDay` and `ProceduralSky` are driver-based, not
   Harmony-postfixed. They only create when `DayNightCycle` is on or `FogDensity`
   is nonzero (`WorldSphereMod/Code/Lighting/TimeOfDay.cs:19-83`,
   `WorldSphereMod/Code/Lighting/ProceduralSky.cs:26-39`).
   Defaults are off for `DayNightCycle` and `FogDensity=0`. Code present: yes.
   Flag: off. Gate: no Harmony postfix; it is an `EnsureCreated` driver path.

6. Real sun / cascaded shadows. `SunDriver` and `ShadowCascadeConfig` exist
   (`WorldSphereMod/Code/Lighting/SunDriver.cs:18-53`,
   `WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs:18-104`), but
   `HighShadows` defaults off (`WorldSphereMod/Code/SavedSettings.cs:49`).
   There is no phase patch to fire here; `Core.ApplyPhaseToggle()` just pushes
   the setting into the driver (`WorldSphereMod/Code/Core.cs:81-86`).
   Code present: yes. Flag: off. Gate: driver callback, not a phase postfix.

7. PostFX. `PostFxController` is present and wired from the frame driver
   (`WorldSphereMod/Code/Fx/PostFxController.cs:134-287`,
   `WorldSphereMod/Code/Voxel/VoxelRender.cs:599-670`), but `PostFX` defaults
   off (`WorldSphereMod/Code/SavedSettings.cs:58`). If URP types are absent, it
   logs and no-ops. Code present: yes. Flag: off. Gate: driver-based, not a patch.

8. Procedural buildings. The phase patch exists and does fire when enabled
   (`WorldSphereMod/Code/ProcGen/BuildingProcRender.cs:14-146`), but
   `ProceduralBuildings` defaults off (`WorldSphereMod/Code/SavedSettings.cs:43`).
   So “code-complete” in docs still means “invisible unless toggled.” Code present:
   yes. Flag: off. Gate: yes, the phase postfix fires on `BuildingManager.precalculateRenderDataParallel`.

9. Skeletal animation. `RigDriver` is implemented and reachable, but
   `SkeletalAnimation` defaults off (`WorldSphereMod/Code/SavedSettings.cs:51`).
   It is inline-gated inside `VoxelRender` rather than via a phase postfix, and
   non-humanoid rigs fall back to the static voxel mesh path
   (`WorldSphereMod/Code/Rig/RigDriver.cs:1-144`, `WorldSphereMod/Code/Voxel/VoxelRender.cs:340-430`).
   Code present: yes. Flag: off. Gate: inline branch, not a phase postfix.

10. Building style procgen hidden branch. `BuildingProcRender` has a second
    style gate (`BuildingStyleProcgen`) that defaults off
    (`WorldSphereMod/Code/SavedSettings.cs:43`, `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs:124-146`).
    Users can enable `ProceduralBuildings` and still stay on voxelized fallback
    unless this extra toggle is also flipped. Code present: yes. Flag: off.
    Gate: yes, but only after the extra style toggle is set.

## Bottom line

- Working-by-default: crossed-quad foliage, worldspace UI, particle bursts.
- Implemented but hidden: water, shadows, fog/day-night, post-FX, procedural
  buildings, skeletal animation.
- Still missing: terrain smoothing / biome blending / mountain smoothing, and
  real crossed-quad cloud rendering.
