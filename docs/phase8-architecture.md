# Phase 8 — Procedural Sky + Day/Night + Fog

Source: design pass by `feature-dev:code-architect` (agent run 2026-05-17).
Pre-implementation design doc; no code written yet. Builds on Phase 5 (which owns the Sun `DirectionalLight`).

---

## 1. Module Layout

Three files under `WorldSphereMod/Code/Lighting/` + one shader.

- **`ProceduralSky.cs`** — `MonoBehaviour` on `Mod.Object`. Replaces the existing `Skybox.material` (set in `3DCamera.cs:116`) with a runtime `Material(ProceduralSky.shader)`. Re-bakes a 128-res `RenderTextureCube` when `TimeOfDay` advances past `kDirtyThreshold = 0.005f`, then assigns it to both `RenderSettings.skybox` and Phase 4's `WaterSurface._renderer.sharedMaterial._SkyCubemap` (so Fresnel stays in sync).
- **`TimeOfDay.cs`** — `MonoBehaviour`. Owns the `[0..1]` time float (`0=midnight, 0.5=noon`). Two driver modes selected at startup: (a) reflection probe `MapBox.world_time` if exposed; (b) autonomous mode advancing by `Time.deltaTime * _DaySpeed`. Raises `WorldSphereModAPI.RaiseTimeOfDay(t)`.
- **`SunRig.cs`** — static. Holds cached `Light` ref to the Phase 5 sun. `Drive(t)` rotates it + sets `Sun.color` + `RenderSettings.ambientLight` via the dawn→noon→dusk→night color gradient.

Shader: `WorldSphereMod/Resources/Shaders/ProceduralSky.shader` — 3-color gradient (zenith/horizon/ground) + sun disc. Hosek-Wilkie reserved as future shader keyword `_HOSEK_WILKIE`.

Namespace `WorldSphereMod.Lighting`.

---

## 2. Public Type Signatures

```csharp
sealed class ProceduralSky : MonoBehaviour
{
    static ProceduralSky? Instance;
    Material _skyMat;
    RenderTextureCube _cubemap;
    float _lastRenderedT;
    static float kDirtyThreshold = 0.005f;
    static void EnsureCreated();
    void Apply(float t);
    void OnWorldUnload();
}

sealed class TimeOfDay : MonoBehaviour
{
    static TimeOfDay? Instance;
    float _t;
    float _daySpeed = 0.001f;     // full day ≈ 16 min
    bool _useWorldBoxTime;        // set at Init via reflection
    static void EnsureCreated();
    float Current => _t;
    void SetStartTime(float t);
    void SetSpeed(float speed);
    void OnWorldUnload();
    void Update();                 // advances _t; Apply(t); SunRig.Drive(t); RaiseTimeOfDay(t)
}

static class SunRig
{
    static Light? _sun;
    static void Bind(Light sun);   // called from Phase 5 CameraManager.Begin()
    static void Drive(float t);
    static Color SunColor(float t);
    static Color AmbientColor(float t);
}
```

Gradient (sampled at `t`):
- dawn (0.25): warm orange `#FF9B4E`; ambient dim orange
- noon (0.5): white-yellow `#FFF5E0`; ambient sky-blue
- dusk (0.75): red-orange `#FF6B35`; ambient purple
- night (0.0): cold blue `#2B3A67`; ambient near-black

---

## 3. Procedural Sky Shader

`ProceduralSky.shader` — URP custom skybox pass. Properties:
- `_ZenithColor`, `_HorizonColor`, `_GroundColor` — set by `Apply(t)`.
- `_SunDir` — world-space sun direction (matches `_sun.transform.forward`).
- `_SunSize` (default 0.04 rad), `_SunBloom` (default 0.12).

Vertex: outputs `worldDir` from skybox cube verts (standard Unity convention).
Fragment:
```hlsl
float horizon = pow(1 - abs(worldDir.y), _HorizonPower);
color = lerp(lerp(ground, horizon, saturate(worldDir.y + 0.05)), zenith, saturate(worldDir.y));
float sunDot = dot(worldDir, _SunDir);
float disc = smoothstep(cos(_SunSize), cos(_SunSize * 0.9), sunDot);
color += _SunColor * disc * _SunBloom;
```

No Hosek-Wilkie LUT in first ship — keyword `_HOSEK_WILKIE` reserved.

---

## 4. Phase 5 Split

Phase 5 owns: `Sun` `DirectionalLight` creation in `CameraManager.Begin()`, shadow cascades, binding via `SunRig.Bind(sun)`.

Phase 8 owns: `Drive(t)` rotation per frame, color-temperature animation, sky gradient, `TimeOfDay` driver, fog.

If Phase 8 ships before Phase 5 (Phase 5 blocks on Unity 2022.3), `SunRig.Drive` no-ops on null `_sun` — sky still gradient-animates without a real sun light.

---

## 5. `MapBox.world_time` Probe

```csharp
FieldInfo? f = typeof(MapBox).GetField("world_time",
    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
_useWorldBoxTime = f != null && f.FieldType == typeof(float);
```

Grep across the mod's C# files returns zero matches for `world_time`. The probe handles current absence + future presence gracefully. Fallback: fully autonomous; `WorldSphereTab` slider is the player's only control.

---

## 6. Fog

`TimeOfDay.Update` writes `RenderSettings` each frame (cheap):
```csharp
bool fogOn = Core.savedSettings.DayNightCycle || Core.savedSettings.FogDensity > 0f;
RenderSettings.fog = fogOn;
RenderSettings.fogMode = FogMode.ExponentialSquared;
RenderSettings.fogColor = SunRig.AmbientColor(_t) * 0.8f;
RenderSettings.fogDensity = Core.savedSettings.FogDensity;
```

`FogDensity = 0` (default) disables. Player controls density via `WorldSphereTab` slider. Color tracks ambient for seam blend with skybox horizon.

---

## 7. Wire-Up

- **Init.** `Mod.Init` after Phase 7's `WorldUIRenderer.EnsureCreated()`: `TimeOfDay.EnsureCreated()` then `ProceduralSky.EnsureCreated()`. Skipped if `!DayNightCycle && FogDensity == 0`.
- **Skybox swap.** `ProceduralSky.EnsureCreated`: `CameraManager.MainCamera.GetComponent<Skybox>().material = _skyMat`. Runs after `CameraManager.Begin()` so camera exists.
- **Cubemap → water.** After each re-bake: `WaterSurface.Instance?._renderer.sharedMaterial.SetTexture("_SkyCubemap", _cubemap)`. Null-safe if Phase 4 inactive.
- **Phase 5 sun bind.** `CameraManager.Begin()` (Phase 5 commit): after sun creation, `SunRig.Bind(Sun)`.
- **World end.** Postfix on `MapBox.addClearWorld` → `TimeOfDay.OnWorldUnload + ProceduralSky.OnWorldUnload`.
- **Tab controls.** `WorldSphereTab.cs`: "Sky Settings" window — `DayNightCycle` toggle, start-time slider (0-1), day-speed slider (0.0001-0.01), fog-density slider (0-0.1).

---

## 8. Risks

1. **Phase 4 Fresnel cubemap sync.** `WaterGerstner.shader` samples `_SkyCubemap`. Phase 8 must `SetTexture` on `WaterSurface._renderer.sharedMaterial` every re-bake (gated `kDirtyThreshold`). Skipped safely if `WaterSurface.Instance == null`.
2. **`MapBox.world_time` not exposed.** Grep returns zero hits in the decompile. Reflection probe falls back to autonomous mode. Decoupled from WorldBox's own clock (plant growth, night spawns still use WorldBox time); acceptable. Deeper integration if Melvin exposes the field later.
3. **Gating without breaking Phase 5 sun.** When `DayNightCycle` is off but Phase 5 is on, sun should stay at 11am default. If `TimeOfDay.Instance == null`, `CameraManager.Begin()` calls `SunRig.Drive(0.5f)` once. Phase 5 lighting is not contingent on Phase 8.

---

## 9. Build Sequence (one PR)

1. `sky: ProceduralSky + gradient shader at static t=0.5` — replace existing skybox material; verify no regression with Phase 4 Fresnel on.
2. `sky: SunRig.Drive + color-temp gradient` — sun rotates with time float; shadow lengthening visible.
3. `sky: TimeOfDay autonomous driver + WorldSphereTab slider`.
4. `sky: MapBox.world_time reflection probe + fallback` — conditional driver selection.
5. `sky: fog integration via RenderSettings + FogDensity slider`.
6. `sky: cubemap re-bake on dirty threshold + WaterSurface._SkyCubemap sync`.
7. `sky: raise OnTimeOfDayChanged via API`.
8. `sky: flip DayNightCycle=true; update phase table + HANDOFF`.

---

## 10. Files

**New:**
- `WorldSphereMod/Code/Lighting/{ProceduralSky,TimeOfDay,SunRig}.cs`
- `WorldSphereMod/Resources/Shaders/ProceduralSky.shader`

**Modify:**
- `WorldSphereMod/Code/Mod.cs` — `EnsureCreated` calls after Phase 7 init.
- `WorldSphereMod/Code/3DCamera.cs:105-119` — `CameraManager.Begin()`: `SunRig.Bind(sun)` after Phase 5 sun creation.
- `WorldSphereMod/Code/WorldSphereTab.cs` — Sky Settings window.
- `WorldSphereMod/Code/Water/WaterSurface.cs` — expose `_renderer` as `internal` for cubemap write.

No changes to `SavedSettings.cs` (`DayNightCycle`/`FogDensity` already declared), `WorldSphereAPI.cs` (`OnTimeOfDayChanged`/`RaiseTimeOfDay` already wired).

---

## Key references

- `WorldSphereMod/Code/3DCamera.cs:105-119` — `CameraManager.Begin()`; skybox material set here.
- `WorldSphereMod/Code/Core.cs:412` — `SkyBox.mat` asset bundle load; Phase 8 bypasses.
- `WorldSphereMod/Code/WorldSphereAPI.cs:79-80` — `OnTimeOfDayChanged` + `RaiseTimeOfDay`.
- `WorldSphereMod/Code/SavedSettings.cs:40-42` — `DayNightCycle = false`, `FogDensity = 0.0f`.
- `WorldSphereMod/Code/Water/WaterSurface.cs` — `_renderer` (Phase 8 writes `_SkyCubemap`).
- `docs/phase4-architecture.md:81` — `_SkyCubemap` name (must match exactly).
- `docs/phase5-architecture.md` — Phase 5 sun creation; Phase 8 drives.
