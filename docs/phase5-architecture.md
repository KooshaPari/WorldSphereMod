# Phase 5 — Lighting + Cascaded Shadows

Source: design pass by `feature-dev:code-architect` (agent run 2026-05-17).
Historical design-state snapshot; use `docs/HANDOFF.md` for current defaults. Complements `docs/phase5-prep.md`.

---

## 1. Module Layout

Three new files under `WorldSphereMod/Code/Lighting/` + one shader asset + targeted modifications to four existing files.

- **`Lighting/SunDriver.cs`** — `MonoBehaviour` on `Mod.Object`. Owns the `Sun` `DirectionalLight`, applies a fixed `TimeOfDay` euler in `LateUpdate`. Exposes a static `Sun` handle for Phase 8 to drive.
- **`Lighting/ShadowCascadeConfig.cs`** — URP pipeline-asset configurator. Reads `SavedSettings.HighShadows`, writes `shadowCascadeCount` / `shadowDistance` / cascade splits onto the active `UniversalRenderPipelineAsset`. Stashes originals for `Reset()`.
- **`Lighting/SpriteShadowPatch.cs`** — Harmony Prefix on `SpriteShadow.LateUpdate`. Suppresses flat quads when `IsWorld3D && SunDriver.Active`. Preserves the flat path for crossed-quad foliage and vanilla-2D fallback.
- **`Resources/Shaders/VoxelLit.shader`** — URP forward-lit, instanced, vertex-color. Three passes: ForwardLit + ShadowCaster + DepthOnly. Phase 5b only (requires Unity 2022.3 + AssetBundle rebuild).

Namespace `WorldSphereMod.Lighting`.

---

## 2. Public Type Signatures

```csharp
namespace WorldSphereMod.Lighting;

static class SunDriver
{
    static Light? Sun;
    static Transform? _rig;             // sibling to MainCamera under LightingRoot
    static bool Active => Sun != null && Sun.shadows != LightShadows.None;
    static float TimeOfDay;             // 0..24h; default 11.0
    static void Init();
    static void Teardown();
}

static class ShadowCascadeConfig
{
    static void Apply(bool highShadows);
    static void Reset();
}

[HarmonyPatch(typeof(SpriteShadow), nameof(SpriteShadow.LateUpdate))]
static class SpriteShadowPatch
{
    static bool Prefix(SpriteShadow __instance);
    static bool ShouldKeepFlatQuad(SpriteShadow __instance);
}
```

---

## 3. Sun Rig Parenting

`CameraManager.Begin` (`3DCamera.cs:105`) creates `MainCamera` at world root. Sun must follow world position but rotate independently of camera pitch/yaw.

Rig: a `LightingRoot` GameObject at world origin, with `Sun` as its child. `SunDriver.LateUpdate` mirrors `MainCamera.position` to `LightingRoot.position` each frame, then writes its own euler:

```csharp
_rig.position = CameraManager.MainCamera.transform.position;
_rig.rotation = Quaternion.Euler(
    TimeOfDayToAltitude(TimeOfDay),   // X: 11am ≈ 65°
    _rig.rotation.eulerAngles.y,      // Y: fixed azimuth
    0f);
```

`TimeOfDayToAltitude(t) = (t / 24f) * 360f - 90f`, clamped `[-90, 90]`. Phase 8 drives `TimeOfDay`; Phase 5 ships frozen at 11.0.

Shadow type defaults `LightShadows.Soft` when `HighShadows`, else `LightShadows.Hard` (no cascades, cheaper).

---

## 4. URP Shadow Cascade Configuration

`ShadowCascadeConfig.Apply(bool highShadows)` retrieves the active pipeline asset via `(UniversalRenderPipelineAsset)GraphicsSettings.renderPipelineAsset` and writes:

| Property | HighShadows=false | HighShadows=true |
|---|---|---|
| `shadowCascadeCount` | 2 | 4 |
| `shadowDistance` | 50 m | 50 m |
| cascade splits | (0.1, 0.25) | (0.067, 0.2, 0.467) |
| depth/normal bias | 1.0 / 1.0 | 1.0 / 1.0 |

`Reset()` restores stashed originals on world unload.

---

## 5. VoxelLit Shader + EnsureMaterial Replacement

`VoxelRender.EnsureMaterial` (`VoxelRender.cs:46`) gets an AssetBundle-first lookup:

```csharp
var ab = AssetBundleUtils.GetAssetBundle("worldsphere");
Shader? s = ab?.GetObject<Shader>("assets/worldspheremod/VoxelLit.shader");
if (s == null) {
    // existing built-in fallback chain
}
_material = new Material(s) { name = "WSM3D.Voxel.Lit", enableInstancing = true };
```

`VoxelLit.shader` (URP forward, 3 passes):
- **ForwardLit** — `multi_compile_instancing`, `UNITY_INSTANCED_PROP(_InstanceColor)`, MainLight Lambert + half-lambert wrap, `_NormalStrength` slider.
- **ShadowCaster** — `ShadowPassVertex` / `ShadowPassFragment` from URP `ShadowCasterPass.hlsl`. `UNITY_SETUP_INSTANCE_ID` in vertex stage. **This is what makes `MeshInstanceBatcher.Flush(shadows: On)` actually write the shadow map.**
- **DepthOnly** — required by URP depth prepass.

No UVs / no texture sampler — vertex colors are the only color input. Matches `MeshInstanceBatcher`'s existing data model.

---

## 6. SpriteShadow Suppression

`Effects.cs:272-288` currently has a `Shadow3D` Prefix forwarding to `EffectManager.UpdateShadow`. Phase 5 replaces with:

```csharp
[HarmonyPatch(typeof(SpriteShadow), nameof(SpriteShadow.LateUpdate))]
static class SpriteShadowPatch
{
    static bool Prefix(SpriteShadow __instance)
    {
        if (!Core.IsWorld3D) return true;
        if (SunDriver.Active && !ShouldKeepFlatQuad(__instance)) return false;
        EffectManager.UpdateShadow(__instance);
        return false;
    }
    static bool ShouldKeepFlatQuad(SpriteShadow s)
    {
        if (!Core.savedSettings.VoxelEntities) return true;
        Actor? a = s.sprRndCaster?.GetComponentInParent<Actor>();
        return a != null && Constants.PerpActors.ContainsKey(a.asset?.id ?? "");
    }
}
```

Keep-path retains flat quad for: foliage (crossed-quad), vanilla-2D (`VoxelEntities = false`), and per-asset perp opt-outs. When `HighShadows` toggles off at runtime, flat-quad resumes without world reload.

---

## 7. Per-Vertex Normals — Two-Phase Plan

**Phase 5-lite** — screen-space derivatives in the shader: `N = normalize(cross(ddx(worldPos), ddy(worldPos)))`. Zero backend changes. Faceted look (per-face normals); acceptable for Phase 5 ship.

**Phase 5b** — Compound-Spheres-3D fork adds `CustomBufferData<Vector3>("VertexNormals", …)` computed from the height-field per tile at `CreateSphereManager` time. `VoxelLit.shader` reads the buffer as vertex input. Requires Unity 2022.3 + `External/AssetBundleBuilder/` + AssetBundle rebuild.

`_NormalStrength` material slider gates the blend (0 = flat unlit, 1 = full normal). Default 0.6.

---

## 8. Risks

1. **Unity 2022.3 not installed.** Required for `VoxelLit.shader` compile + AssetBundle rebuild. **Mitigation:** split into 5-lite (Sun + cascades + screen-space normals; ships with built-in URP shaders) and 5b (lit shader + real normals). `EnsureMaterial`'s bundle-first lookup falls through gracefully when the bundle is absent — no flag at the boundary.
2. **Submodule swap breaks build.** Vendored `CompoundSpheres.dll` → `ProjectReference`. **Mitigation:** gate with `$(UseSubmoduleBackend)` MSBuild property. Binary-diff submodule build output against vendored DLL before flip. Reverting the property restores vendored path.
3. **Shadow acne on cylindrical terrain at high latitudes.** Default URP bias is tuned for planar terrain. **Mitigation:** expose `_ShadowBias` / `_ShadowNormalBias` as per-material properties on `VoxelLit.shader` so they're tweakable independently of the URP asset. Verify at 60° latitude before ship.
4. **Procedural skybox sun vs Phase 5 sun.** Phase 4 water Fresnel samples `unity_SpecCube0` (static skybox); Phase 5 sun is runtime light. They diverge until Phase 8 procedural sky. **Decision:** Static skybox stays for Phase 5; procedural sky lands in Phase 8 alongside `DayNightCycle`. Document as known visual artifact, not blocker.
5. **Shadow casting with placeholder materials.** `MeshInstanceBatcher.Flush` passes `ShadowCastingMode.On` today (`MeshInstanceBatcher.cs:66`), but the placeholder shaders have no `ShadowCaster` pass — so Unity silently skips shadow-map writes. **Not a regression** (Phase 1 never had shadows), but the end-to-end shadow pipeline needs `VoxelLit.shader` to test. For Phase 5-lite verification, use the existing terrain material (which has its own shadow pass) to confirm cascade configuration; validate voxel-actor shadows once the lit shader lands in 5b.

---

## 9. Build Sequence

**Phase 5-lite** (one PR, 4 commits):

1. `lighting: SunDriver + LightingRoot hierarchy; freeze TimeOfDay=11`
2. `lighting: ShadowCascadeConfig; gate behind HighShadows`
3. `lighting: SpriteShadowPatch replaces Effects Shadow3D body`
4. `lighting: wire SunDriver.Init + ShadowCascadeConfig.Apply in Core.Init after CameraManager.Begin`

**Phase 5b** (separate PR, 5 commits):

5. `lighting: install Unity 2022.3, set up External/AssetBundleBuilder, no-op bundle round-trip parity test`
6. `lighting: VoxelLit.shader (ForwardLit + ShadowCaster + DepthOnly); rebuild worldsphere bundle`
7. `lighting: EnsureMaterial prefers VoxelLit from bundle; fallback chain preserved`
8. `lighting: per-vertex normal buffer in Compound-Spheres-3D submodule; binary-diff parity test`
9. `lighting: swap csproj reference to submodule ProjectReference; flip HighShadows=true; update phase table + HANDOFF`

---

## Files

**New:**
- `WorldSphereMod/Code/Lighting/SunDriver.cs`
- `WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs`
- `WorldSphereMod/Code/Lighting/SpriteShadowPatch.cs`
- `WorldSphereMod/Resources/Shaders/VoxelLit.shader` (Phase 5b only)

**Modify:**
- `WorldSphereMod/Code/Core.cs:412` — `SunDriver.Init()` after `CameraManager.Begin`; teardown on unload.
- `WorldSphereMod/Code/Effects.cs:272-288` — replace `Shadow3D` Prefix body.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:46-71` — `EnsureMaterial` bundle-first lookup.
- `WorldSphereMod/Code/SavedSettings.cs:35` — `HighShadows = true` final commit.
- `WorldSphereMod.csproj` — `$(UseSubmoduleBackend)` property + conditional refs.
- `External/Compound-Spheres-3D/` — submodule (5b only).

---

## Key file references

- `docs/PLAN.md:122-132`, `docs/phase5-prep.md` — scope + research.
- `WorldSphereMod/Code/3DCamera.cs:105-118` — `CameraManager.Begin`; Sun attach point.
- `WorldSphereMod/Code/Core.cs:407-413` — `LoadAssets`; insert `SunDriver.Init` after line 412.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:46-71` — `EnsureMaterial` replaced in 5b.
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:66` — `Flush(shadows: On)` already correct.
- `WorldSphereMod/Code/Effects.cs:121-135,272-288` — `UpdateShadow` + `Shadow3D` Prefix.
- `WorldSphereMod/Code/SavedSettings.cs:35` — `HighShadows` flag.
