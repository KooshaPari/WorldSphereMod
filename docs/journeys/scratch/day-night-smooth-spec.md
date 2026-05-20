# Day/Night Smooth Spec

Goal: keep Phase 8’s day/night motion continuous, not stepped through preview fixtures.

## 1. Time-of-day state

The authoritative clock is `TimeOfDay.Current` in `WorldSphereMod/Code/Lighting/TimeOfDay.cs:8-56`.
`Update()` is already continuous: it reads `MapBox.world_time` when available, otherwise advances `Current` by `Time.deltaTime * DaySpeed` and wraps with `Mathf.Repeat` (`TimeOfDay.cs:35-45`). It then publishes the normalized time to `SunDriver.TimeOfDay` and raises the API event (`TimeOfDay.cs:46-55`).

## 2. Sun + sky interpolation curves

The sun/ambient curve lives in `WorldSphereMod/Code/Lighting/SunRig.cs:11-34`.
`SunColor(t)` is a four-segment piecewise `Color.Lerp` across night → dawn → noon → dusk → night (`SunRig.cs:18-27`), and `AmbientColor(t)` derives from that same sun color (`SunRig.cs:30-33`).

The skybox curve lives in `WorldSphereMod/Code/Lighting/ProceduralSky.cs:39-57`.
`LateUpdate()` samples `TimeOfDay.Current` every frame, then computes:
- `zenith` from a dark-blue to light-blue `Color.Lerp` using `Mathf.Sin(t * Mathf.PI)` (`ProceduralSky.cs:47`)
- `horizon` from scaled sun colors using the same sine blend (`ProceduralSky.cs:48`)
- `_SunDir` from `-SunDriver.Sun.transform.forward` (`ProceduralSky.cs:55-56`)

## 3. How to make it continuous

The runtime path should stay scalar-driven and frame-sampled:
- keep `TimeOfDay.Current` as the only day phase input
- drive sun color, ambient light, and skybox colors from that scalar every frame
- avoid any preview/runtime code that snaps the cycle to fixed after-0/after-1/after-2/after-3 states

If the stepped behavior is in the phase-preview assets, replace the fixed fixture ladder with a sampled run over the same curve so the preview records intermediate times instead of hard cuts.

## 4. HighShadows interaction

`HighShadows` is orthogonal to the day/night curve. `SunDriver.Init()` creates the sun light and immediately calls `ShadowCascadeConfig.Apply(Core.savedSettings.HighShadows)` (`WorldSphereMod/Code/Lighting/SunDriver.cs:18-41`).

`ShadowCascadeConfig.Apply(bool)` only mutates URP shadow cascade count, split, distance, and bias (`WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs:71-110`). It does not touch `TimeOfDay`, `SunRig`, or `ProceduralSky`.

Spec rule: changing the day/night interpolation must not rebind or rebuild HighShadows state; changing HighShadows must not quantize or reset the time-of-day curve.
