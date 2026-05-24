# Sky Replacement Research

Current baseline: the mod already uses a URP-tagged custom skybox shader loaded at runtime and driven by `ProceduralSky.cs` + `SunRig`/`TimeOfDay`. It is also tied into the water cubemap bake path, so a replacement has to preserve the skybox material contract, not just render pretty clouds.

## Comparison

- **Time of Day (Unity Asset Store, URP Time Of Day - Skybox, Weather & Time)**  
  URP only, 205.8 MB, latest release Jun 20 2025. Integration cost: **medium-high**. Good turnkey time/weather package, but it is pipeline-specific and still means adopting a vendor-controlled weather/time stack inside the mod. Source: `assetstore.unity.com/packages/tools/particles-effects/urp-time-of-day-skybox-weather-time-257011`.

- **Enviro 3 - Sky and Weather**  
  Built-in/URP/HDRP compatible, 235.7 MB, latest release Jun 20 2025. Integration cost: **high**. It is the most complete system here, but also the heaviest and most package-dependent. Best for a full environment overhaul, not for a narrow mod replacement. Source: `assetstore.unity.com/packages/tools/particles-effects/enviro-3-sky-and-weather-236601`.

- **Azure[Sky] Dynamic Skybox**  
  Mature Asset Store sky system with strong community adoption and day/night/weather features. Integration cost: **low-medium** if using the real asset; **not recommended from an unverified GitHub mirror** because I could not confirm an official free GitHub source. It is the closest architectural match to this mod’s current “swap a skybox material and drive it from code” pattern. Source: `assetstore.unity.com/packages/tools/particles-effects/azure-sky-dynamic-skybox-36050/reviews`.

- **Unity built-in Skybox/Procedural shader**  
  Official Unity procedural skybox for the built-in render pipeline. Integration cost: **low in built-in projects, high here**. WorldBox/this mod is already using URP-tagged shaders and runtime skybox control, so this is the wrong pipeline target unless you add compatibility glue. Source: https://docs.unity3d.com/Manual/shader-skybox-procedural.html.

- **GitHub atmosphere shader libraries**  
  Example 1: `keijiro/CloudSkybox` (MIT) extends Unity’s default procedural skybox with volumetric clouds. Example 2: `sinnwrig/URP-Atmosphere` (MIT) is a URP render feature with baked optical depth and compute-shader requirements. Integration cost: **medium-high to high**. They are technically strong, but they are renderer features or low-level shader libs, not drop-in mod systems. Sources: `github.com/keijiro/CloudSkybox`, `github.com/sinnwrig/URP-Atmosphere`.

## Recommendation

Keep the current hand-rolled sky/sun stack unless you need a full weather system. If you must replace it, **Azure Sky** is the best practical fit for this mod because it is the closest to a drop-in skybox-driven architecture. **Enviro 3** is the richest option, but the highest integration burden. **Unity Procedural** and most GitHub atmosphere libraries are a poor fit for WorldBox’s current URP-based mod path.
