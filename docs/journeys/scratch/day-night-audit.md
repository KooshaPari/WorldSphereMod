# Day/Night Audit

Scope: `WorldSphereMod/Code/Lighting/SunDriver.cs` and `WorldSphereMod/Code/Lighting/ProceduralSky.cs`.

## Findings

- High: the day/night clock is not self-advancing when `SavedSettings.DayNightCycle` is on. `TimeOfDay.Update()` prefers `MapBox.world_time` whenever the reflection lookup succeeds, and only falls back to `Current += Time.deltaTime * DaySpeed` when that lookup fails (`WorldSphereMod/Code/Lighting/TimeOfDay.cs:35-45`). This repo does not advance `world_time` anywhere else, so the cycle depends on an external game clock rather than ticking from this mod.

- Medium: the phase can be enabled/disabled in settings, but the sky/time components are only created at startup. `Mod.Init()` calls `ProceduralSky.EnsureCreated()` once (`WorldSphereMod/Code/Mod.cs:75-81`), and `EnsureCreated()` returns early unless `DayNightCycle` is already true (`WorldSphereMod/Code/Lighting/ProceduralSky.cs:16-21`). The runtime phase toggle path only repatches Harmony types (`WorldSphereMod/Code/PhasePatchManager.cs:15-50`), so turning `DayNightCycle` on later will not instantiate the skybox/time driver.

- Low: shadow caster setup is mostly correct, but it is static after init. `SunDriver.Init()` creates a directional light and sets `Sun.shadows = LightShadows.Soft` before applying URP cascade settings (`WorldSphereMod/Code/Lighting/SunDriver.cs:27-41`). That is enough for shadow casting, but `ShadowCascadeConfig.Apply()` is only called once here, so later `HighShadows` toggles do not refresh the shadow setup unless the light is torn down and rebuilt (`WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs:71-110`).

- Low: sun direction calculation is internally consistent. `SunDriver.Update()` rotates the lighting root from `TimeOfDayToEuler(TimeOfDay)` (`WorldSphereMod/Code/Lighting/SunDriver.cs:70-79,82-85`), and `ProceduralSky.LateUpdate()` feeds the shader `-Sun.transform.forward` as `_SunDir` (`WorldSphereMod/Code/Lighting/ProceduralSky.cs:39-57`). The shader expects a world-space sun vector and uses it in a dot product (`WorldSphereMod/Resources/Shaders/ProceduralSky.shader:62-73`), so I did not find a sign mismatch here.

- Low: per-frame work is acceptable, but there is one easy cache win. `ProceduralSky.LateUpdate()` recomputes the sun/ambient colors every frame and `AmbientColor()` recomputes `SunColor(t)` again (`WorldSphereMod/Code/Lighting/ProceduralSky.cs:39-57`, `WorldSphereMod/Code/Lighting/SunRig.cs:11-34`). If this shows up in profiling, cache the current time bucket and derived colors before pushing material properties.
