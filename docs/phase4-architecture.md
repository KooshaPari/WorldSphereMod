# Phase 4 — Mesh Water: Architecture Blueprint

Source: design pass by `feature-dev:code-architect` (agent run 2026-05-17).
Pre-implementation design doc; no code written yet.

---

## 1. Module Layout

Three files under `WorldSphereMod/Code/Water/` plus one shader.

- **`WaterMaskBuffer.cs`** — data layer. Per-tile depth `float[]` indexed by `SphereTile.Index()`. Phase 4-lite path computes from `Tools.TrueHeight` thresholds; full-vision path reads from a Compound-Spheres-3D SSBO. Owns `RebuildMask()`.
- **`WaterSurface.cs`** — mesh + render layer. `MonoBehaviour` owning the water `GameObject` + `Mesh` + material. `RebuildMesh()` invoked on mask change; `LateUpdate()` advances `_WaveTime`.
- **`WaterRender.cs`** — integration shim. Harmony patches: `SphereTileColor` alpha suppression Postfix + `Sphere.Begin`/`Sphere.Finish` lifecycle hooks.
- Shader: `WorldSphereMod/Resources/Shaders/WaterGerstner.shader`. Loaded via `Resources.Load<Shader>`.

Namespace `WorldSphereMod.Water`. Files match types 1:1.

---

## 2. Public Type Signatures

### `WaterMaskBuffer.cs`

```csharp
namespace WorldSphereMod.Water;

static class WaterMaskBuffer
{
    static float[] Depths;     // length = MapBox.width * MapBox.height
    static float SeaLevel;     // = Tools.TrueHeight(17) = 2.0f (ice boundary)

    static void RebuildMask();
    static float DepthAt(int tileIndex);
    static bool IsWater(int tileIndex);
    static void Clear();
}
```

### `WaterSurface.cs`

```csharp
sealed class WaterSurface : MonoBehaviour
{
    static WaterSurface Instance;
    static WaterSurface Create(Transform parent);
    static void Destroy();
    void RebuildMesh();
    void LateUpdate();         // advance _WaveTime; no mesh rebuild
}
```

`RebuildMesh` iterates tiles, emits two triangles per water tile at `Sphere.SpherePos(x, y, SeaLevel)` — same coord function as terrain. GameObject parented to `Sphere.Manager.transform`.

---

## 3. Water Mask Derivation

**Phase 4-lite (primary).** `Tools.TrueHeight` (`Tools.cs:312-320`) ocean IDs 0-6 range 0.01-1.8f. ID 17 = 2.0f (ice boundary, sea level). `Depths[i] = max(0, 2.0f - TrueHeight(tile.type.height_id))`. No backend dependency.

**Full-vision (Phase 5 dependent).** Add `new CustomBufferData<float>("WaterDepths", 4, SphereTileWaterDepth)` at `Core.cs:433`. GPU-side SSBO enables per-vertex depth in the vertex shader. CPU `float[]` continues to be maintained — SSBO is additive.

---

## 4. Wire-Up

- **Create.** `WaterRender` Postfix on `Sphere.Begin` (after `CreateSphereManager`) → `WaterMaskBuffer.RebuildMask()` → `WaterSurface.Create(Sphere.Manager.transform)`. Gated on `MeshWater && Is3D`.
- **Destroy.** Prefix on `Sphere.Finish` → `WaterSurface.Destroy()` + `WaterMaskBuffer.Clear()`.
- **Per-frame.** Add one line to `WindSwayDriver.LateUpdate` (Phase 3 driver): `WaterSurface.Instance?.LateUpdate()`. No second driver.
- **Terrain suppression.** Postfix on `SphereTileColor` (`CompoundSphereScripts.cs:35`): when `IsWater(SphereTile.Index())`, set alpha=0. `Sphere.GetColor` already skips alpha=0 layers at `Core.cs:316`.
- **Tile-change invalidation.** Postfix on `Sphere.UpdateBaseLayer` + `Sphere.UpdateScale`: if tile crosses water boundary, `RebuildMask()` + `RebuildMesh()` (~500 µs on 512×512).

---

## 5. Shader Design (`WaterGerstner.shader`)

URP forward pass. Wave displacement in vertex stage.

- **Gerstner waves (3 directions).** Phase = `dot(_WaveDir[i], float2(theta, y))` where `theta = atan2(worldPos.x, worldPos.z) * radius`. Cylindrical-coord variant avoids seam artifacts on the default cylindrical world shape. Shader keyword `_FLAT_WORLD` (from `savedSettings.CurrentShape == 1`) switches to standard XZ for flat shape.
- **Depth tint.** Fragment: `lerp(shallow, deep, saturate(depth/_MaxDepth))`. Defaults: shallow `(0.26, 0.78, 0.75)` teal, deep `(0.05, 0.12, 0.35)` navy. Per-tile depth via `MaterialPropertyBlock` float (Phase 4-lite) or SSBO (full-vision).
- **Fresnel cubemap reflection.** `fresnel = pow(1 - saturate(dot(N,V)), _FresnelPower)`. Sample `_SkyCubemap` (skybox loaded at `Core.cs:412`). Final: `lerp(depthTint, cubemap, fresnel)`.
- **Shoreline foam.** Phase 4-lite: URP `_CameraDepthTexture` screen-space fade. `foam = 1 - saturate((sceneDepth - waterDepth) / _FoamRange)`. Fallback if depth prepass off: per-tile neighbor-edge bool baked into an R8 texture in `RebuildMask`.
- **`WaterDetail`** (already in `SavedSettings.cs:47`): scales `_WaveFrequency` and `_WaveAmplitude`. 0=flat, 1=default, 2=heavy chop.

---

## 6. Critical Risks

### Risk 1 — Phase 5 dependency
SSBO requires `Compound-Spheres-3D` + Unity 2022.3 (`phase5-prep.md:58-69`). **Phase 4-lite delivers ~95% of the visual:** wave-animated water mesh with Fresnel/tint/screen-space foam. Only per-vertex SSBO depth is blocked. Phase 5 retrofits with one branch in `RebuildMask` + one entry at `Core.cs:433`. Ship Phase 4-lite independently.

### Risk 2 — Coordinate mapping on cylinder
XZ-space Gerstner waves diverge at the wrap seam and shrink at high Y on the cylindrical world (default). Mitigation: cylindrical-coord wave phase (section 5). Flat-world path uses XZ via shader keyword branch. ~2 shader variants — negligible.

### Risk 3 — Shoreline foam without smooth depth gradient
Per-tile float gives no sub-tile gradient. Screen-space `_CameraDepthTexture` sidesteps this entirely — reads rendered geometry depth, produces smooth foam regardless of tile resolution. Per-tile neighbor-edge fallback is a defined backup, not the primary path. Phase 5 SSBO doesn't improve foam beyond screen-space.

---

## 7. Decision Tree Per Tile

```
MeshWater == false
    → vanilla SphereTileColor unchanged; no WaterSurface

MeshWater == true AND Is3D:
    IsWater(tile) == false → terrain renders normally
    IsWater(tile) == true:
        - SphereTileColor Postfix: alpha = 0 (terrain invisible)
        - WaterSurface includes tile in mesh at SeaLevel
        - WaterGerstner.shader: Gerstner + tint + Fresnel + foam
```

---

## 8. Build Sequence (one PR, atomic commits)

1. `water: add WaterMaskBuffer with CPU height-threshold rebuild` — data layer only.
2. `water: add WaterSurface MonoBehaviour + flat unlit mesh` — placement validation.
3. `water: wire WaterRender — Sphere.Begin Postfix + SphereTileColor suppression` — terrain hides under plane.
4. `water: add WaterGerstner.shader — depth tint + Fresnel, static surface` — colored Fresnel surface.
5. `water: add Gerstner vertex displacement, cylindrical-coord variant` — waves animate.
6. `water: shoreline foam via _CameraDepthTexture + per-tile fallback`
7. `water: tile-change invalidation on UpdateBaseLayer + UpdateScale` — dynamic tile edits.
8. `water: flip MeshWater=true; update phase table + HANDOFF` — ship gate.

---

## 9. Files to Create or Modify

**New:**
- `WorldSphereMod/Code/Water/WaterMaskBuffer.cs`
- `WorldSphereMod/Code/Water/WaterSurface.cs`
- `WorldSphereMod/Code/Water/WaterRender.cs`
- `WorldSphereMod/Resources/Shaders/WaterGerstner.shader`

**Modify:**
- `WorldSphereMod/Code/Core.cs:433` — full-vision only: add `"WaterDepths"` buffer, guarded.
- `WorldSphereMod/Code/Foliage/WindSwayDriver.cs` — add `WaterSurface.Instance?.LateUpdate()` (one line).

No changes to `SavedSettings.cs` (`MeshWater` + `WaterDetail` already present), `MeshInstanceBatcher.cs`, or Phase 1-3 files.

---

## Key file references

- `WorldSphereMod/Code/CompoundSphereScripts.cs:35` — `SphereTileColor` (suppression target).
- `WorldSphereMod/Code/Core.cs:294-303` — `Sphere.Begin` (create hook).
- `WorldSphereMod/Code/Core.cs:355-362` — `Sphere.Finish` (destroy hook).
- `WorldSphereMod/Code/Core.cs:383-386` — `Sphere.SpherePos` (vertex placement).
- `WorldSphereMod/Code/Core.cs:316` — `GetColor` alpha=0 skip (enables suppression).
- `WorldSphereMod/Code/Core.cs:412` — skybox cubemap load (reused for Fresnel).
- `WorldSphereMod/Code/Core.cs:433` — `IBufferData` list (full-vision SSBO).
- `WorldSphereMod/Code/Tools.cs:312-320` — `TrueHeight` ocean IDs; ID 17 = 2.0f sea level.
- `WorldSphereMod/Code/SavedSettings.cs:33,47` — `MeshWater` (default false), `WaterDetail` (1.0f).
- `docs/phase5-prep.md:44-45` — water-mask SSBO as Phase 5 deliverable.
