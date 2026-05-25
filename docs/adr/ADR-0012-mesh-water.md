# ADR-0012 — Mesh Water Rendering

**Status:** Accepted (Phase 4, landed alpha.8+)
**Date:** 2026-05-25
**Author:** KooshaPari
**Supersedes:** n/a
**Related:** ADR-0012-assetbundle-shader-bake-plan (shader bake pipeline that compiles GerstnerWater into the bundle)

---

## 1. Problem

Water in the 3D sphere view has exhibited several failure modes across the
Phase 4 development arc:

1. **Flat billboard above the surface.** The original approach translated the
   water GameObject in local-Y to simulate wave bob. On a sphere, local-Y is
   tangential on the top face and radial on the sides, so the mesh floated
   visibly above the terrain from most camera angles and was only edge-visible
   from others.

2. **Invisible geometry.** The Unity Standard shader's `_Mode=3` (Transparent)
   activates shader passes that differ from the opaque path. Even when blend
   state and render queue were manually overridden to opaque values, the
   transparent-mode passes produced invisible geometry because the Standard
   shader's internal keyword gating (`_ALPHABLEND_ON`, `_ALPHAPREMULTIPLY_ON`)
   changed which fragment output was active.

3. **Black water ("blackworld").** When the Standard fallback was configured
   with alpha-blended transparent queue (`renderQueue = 3000`) and
   `waterTint.a = 0.55`, the surface blended toward black in WorldBox's
   near-zero ambient lighting. Without self-illumination, the alpha blend
   `src * 0.55 + dst * 0.45` over an unlit dark framebuffer produced a solid
   dark rectangle.

4. **`Sprites/Default` rendering black.** Early iterations fell through to the
   sprite shader as a last-resort fallback. `Sprites/Default` expects sprite
   atlas UVs and vertex colors; a procedural mesh with no UVs and default-white
   vertex color rendered as a black silhouette.

5. **`kGerstnerKnownBroken` flag blocking the custom shader.** During
   development, the bundled GerstnerWater shader was temporarily marked broken
   (`const bool kGerstnerKnownBroken = true`), forcing the material resolution
   chain to skip it entirely and fall through to the Standard/URP cascade. This
   masked the fact that the custom shader had been fixed, and kept the broken
   Standard fallback active longer than necessary.

---

## 2. Root Causes

| Symptom | Root cause | Fix |
|---------|-----------|-----|
| Float above surface | `BobAmplitude > 0` translated GO in local-Y; on a sphere this is tangential, not radial | Set `BobAmplitude = 0`; wave displacement belongs in the vertex shader |
| Invisible geometry | Standard shader `_Mode=3` activates transparent passes internally even when blend/queue are overridden | Use `_Mode=0` (Opaque) with emission self-illumination; see `SetStandardTransparentMode()` |
| Black water | Alpha blend in zero-light scene blends toward black | Opaque mode + `_EMISSION` keyword + bright emission color `(0.30, 0.60, 1.20)` |
| Sprites/Default black | Sprite shader needs UVs and sprite atlas; procedural mesh has neither | Eliminate Sprites/Default from fallback chain; use Standard or URP Lit |
| Custom shader skipped | `kGerstnerKnownBroken = true` constant | Flip to `false` once GerstnerWater compiles in the built-in pipeline |

---

## 3. Current Architecture

### 3.1 Module Layout

```
WorldSphereMod/Code/Water/
    WaterMaskBuffer.cs   -- per-tile depth float[], sea level derivation
    WaterSurface.cs      -- MonoBehaviour: mesh generation, material, wave params
    WaterRender.cs       -- Harmony patches: lifecycle, tile suppression, invalidation

Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/
    GerstnerWater.shader -- AssetBundle variant (built-in RP, CG/HLSL)

WorldSphereMod/Resources/Shaders/
    WaterGerstner.shader -- Resources.Load fallback (URP HLSL)

WorldSphereMod/AssetBundles/Shaders/
    GerstnerWater.shader -- Pre-baked copy loaded via Core.Sphere.LoadedShaders
```

### 3.2 Material Resolution Chain

`WaterSurface.EnsureMaterial()` attempts shaders in priority order:

1. **`Core.Sphere.LoadedShaders["GerstnerWater"]`** -- cached from AssetBundle
   at world load. Preferred path; compiles against the built-in render pipeline.
2. **`Shader.Find("WSM3D/GerstnerWater")`** -- runtime probe if the bundle
   cache missed.
3. **`Resources.Load<Shader>("Shaders/ContinuumWaterGerstner")`** -- reserved
   for a future continuum-water variant.
4. **`Resources.Load<Shader>("Shaders/WaterGerstner")`** -- the URP source
   shader shipped under `Resources/`.
5. **`Shader.Find("Standard")`** -- built-in RP fallback, configured opaque
   with emission.
6. **`Shader.Find("Universal Render Pipeline/Lit")`** -- URP Lit fallback.
7. **`Shader.Find("Universal Render Pipeline/Unlit")`** -- URP Unlit fallback.
8. **None found** -- water disabled; `EnsureMaterial()` returns false.

The entire chain is gated by `kGerstnerKnownBroken` (currently `false`). When
`true`, steps 1-4 are skipped and the chain begins at step 5.

After material creation, `enableInstancing` is set then read back. If the GPU
rejects instancing, the material is destroyed and the next candidate is tried.

### 3.3 Water Mask

`WaterMaskBuffer` maintains a `float[]` indexed by `SphereTile.Index()`:

- Sea level = `Tools.TrueHeight(17)` = 2.0f (the ice boundary height ID).
- Per-tile depth = `max(0, SeaLevel - TrueHeight(tile))`.
- `IsWater(idx)` = depth > 0.

Rebuilt on `Sphere.Begin`, `UpdateBaseLayer` (tile edits), and `UpdateScale`.

### 3.4 Mesh Generation

`WaterSurface.RebuildMesh()` emits two triangles per water tile at sea-level
height via `Core.Sphere.SpherePos(x, y, SeaLevel)`. Vertex deduplication at
grid corners uses modular X-wrap to collapse the cylindrical seam, eliminating
a lighting stripe from `RecalculateNormals`. Uses `IndexFormat.UInt32` for
worlds exceeding 65k water vertices.

### 3.5 Wave Animation

`WaterSurface.LateUpdate()` advances `_WaveTime` and writes per-instance
material properties (`_WaveAmp`, `_WaveFreq`, `_WaveSpeed`) scaled by
`SavedSettings.WaterDetail` (range 0-2, default 1.0). The GO's
`localPosition` is pinned to `_baseLocalPosition` -- no transform-level bob.

Actual vertex displacement happens in the shader:

- **AssetBundle GerstnerWater** (`Tools/Unity-Bake-Project`): single-direction
  Gerstner wave with foam crest factor. Built-in RP (`UnityCG.cginc`).
- **Resources WaterGerstner** (`Resources/Shaders`): three-direction Gerstner
  sum with cylindrical coordinate mapping (`atan2` theta for seam-free wrap),
  Fresnel cubemap reflection, and screen-space shoreline foam via
  `_CameraDepthTexture`. URP (`Core.hlsl` + `DeclareDepthTexture.hlsl`).

### 3.6 Tile Color Suppression

`WaterRender.ColorSuppression` is a Harmony Postfix on
`CompoundSphereScripts.SphereTileColor`. When `MeshWater` is active and
`WaterSurface.Instance` exists, water tiles have their `Color32.a` set to 0,
hiding the vanilla flat tile tint under the 3D mesh. The null-check prevents
a one-frame flash during the create/destroy transition.

### 3.7 Runtime Toggle

`WaterRender.UpdateLifecycle()` is called from `VoxelFrameDriver.LateUpdate`
every frame. It compares the current `MeshWater` setting against the last-known
state and creates/destroys the `WaterSurface` without requiring a world reload.
Settings changes via the MCP server or in-game UI take effect immediately.

---

## 4. What Works vs. What Does Not

### Works

- Mesh water surface at correct sea level on sphere and cylinder world shapes.
- Three-direction Gerstner vertex displacement (URP shader path) with
  cylindrical coordinate mapping that avoids seam artifacts.
- Fresnel-based view-dependent tint (shallow teal to deep navy).
- Screen-space shoreline foam from `_CameraDepthTexture` depth comparison.
- Per-tile average depth passed as `_WaterDepth` uniform for depth tinting.
- Sky cubemap reflection via `_SkyCubemap` sampler (if skybox is a Cubemap).
- Dynamic tile-change invalidation (mask + mesh rebuild on `UpdateBaseLayer`
  and `UpdateScale`).
- Runtime toggle without world reload.
- Emission self-illumination on Standard/URP fallback so water is visible in
  WorldBox's low-light scenes.
- Vertex dedup at cylindrical X-wrap seam eliminates normal-recalculation
  artifacts.

### Does Not Work / Limitations

- **URP WaterGerstner shader requires URP pipeline.** WorldBox ships built-in
  RP; the `Resources/Shaders/WaterGerstner.shader` includes URP packages
  (`com.unity.render-pipelines.universal`) that are absent at runtime. It will
  only compile if the game is modded to include URP, or if loaded through an
  AssetBundle baked in a URP project. In practice, the AssetBundle
  `GerstnerWater` (built-in RP) is the only path that compiles at runtime.
- **No per-vertex depth.** Depth tinting uses a single averaged `_WaterDepth`
  float per mesh, not per-vertex. Deep ocean and shallow coast share the same
  tint. Per-vertex SSBO depth is deferred to Phase 5 (`Core.cs:433`
  `IBufferData` integration).
- **No transparency.** Both the AssetBundle shader and the Standard fallback
  render opaque (`Queue=Geometry`, `ZWrite On`) to avoid the blackworld
  regression. True alpha transparency requires either HDR lighting or a
  dedicated water-transparent pass with correct depth sorting.
- **No caustics or underwater effects.** No projective caustic texture, no
  underwater fog, no refraction. Planned for Phase 9 (PostFX).
- **No sub-tile foam gradient.** Screen-space foam works when
  `_CameraDepthTexture` is available; if the depth prepass is off, foam
  degrades to a hard edge. The per-tile neighbor-edge R8 texture fallback
  described in the architecture doc is not yet implemented.
- **Full mesh rebuild on tile edit.** `RebuildMesh` regenerates the entire
  water mesh on every `UpdateBaseLayer` call. Acceptable for current world
  sizes (max ~256x256 = 131k triangles, ~500 us) but per-tile dirty tracking
  is deferred to Phase 4 polish.
- **Wave parameter mismatch between shaders.** The AssetBundle shader uses
  `_WaveAmplitude`/`_WaveSteepness`/`_WaveDirX`/`_WaveDirZ`/`_WaveLength`
  while `WaterSurface.ApplyWaveProfile` writes `_WaveAmp`/`_WaveFreq`/
  `_WaveSpeed` (Vector4). If the AssetBundle shader is active, the C# wave
  parameters are silently ignored (the `HasProperty` guard skips them). The
  AssetBundle shader animates via its own `_WaveTime` only.

---

## 5. Future Work

### 5.1 Vertex Displacement Waves (Phase 5)

Per-vertex SSBO depth buffer (`WaterDepths` at `Core.cs:433`) enables the
shader to read per-tile depth and modulate wave amplitude: shallower water
gets smaller waves, deeper water gets larger swells. Requires the
`Compound-Spheres-3D` + Unity 2022.3 SSBO path.

### 5.2 Foam (Phase 4 polish / Phase 5)

- Implement the per-tile neighbor-edge R8 fallback texture in
  `WaterMaskBuffer.RebuildMask` for when `_CameraDepthTexture` is unavailable.
- Add foam noise texture scrolling along wave direction for visual breakup.

### 5.3 Transparency (Phase 5+)

True alpha transparency requires solving the blackworld regression. Options:

- **HDR + tonemapping**: if scene lighting is brought to HDR values (Phase 5
  sun/shadow), alpha blend no longer collapses to black.
- **Grab pass refraction**: sample the behind-water framebuffer and tint it.
  Expensive but visually correct.
- **Separate transparent pass**: render water after all opaque geometry with
  `ZWrite Off`, `Queue=Transparent`. Requires depth-sorted submission or OIT.

### 5.4 Caustics (Phase 9)

Projective caustic texture on the sea floor, animated by `_WaveTime`. Requires
access to the terrain depth buffer and a second pass or decal projector. Fits
naturally into the Phase 9 PostFX pass.

### 5.5 Underwater Effects (Phase 9)

- Underwater fog (exponential depth fog tinted to `_DeepColor`).
- Screen-space distortion (refraction post-effect when camera is below sea
  level).
- God rays via radial blur from sun direction.

---

## 6. Shader Dependency Matrix

For water to render correctly, the following shader compilation requirements
must be met:

| Shader | Source Location | Render Pipeline | Compiles at Runtime? | Notes |
|--------|----------------|-----------------|---------------------|-------|
| `WSM3D/GerstnerWater` | `Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/GerstnerWater.shader` | Built-in (UnityCG.cginc) | Yes, via AssetBundle | Primary path. Single Gerstner direction, Fresnel tint, foam crest. Opaque, `Queue=Geometry`. |
| `WorldSphereMod3D/WaterGerstner` | `WorldSphereMod/Resources/Shaders/WaterGerstner.shader` | URP (Core.hlsl) | No (missing URP packages) | Three-direction Gerstner, Fresnel cubemap, screen-space foam. Would be superior but cannot compile without URP runtime. |
| `Standard` | Unity built-in | Built-in | Yes | Fallback. Configured opaque `_Mode=0` with `_EMISSION` keyword. No wave displacement. |
| `Universal Render Pipeline/Lit` | Unity URP | URP | Only if URP present | Secondary fallback. Transparent surface type with emission. |
| `Universal Render Pipeline/Unlit` | Unity URP | URP | Only if URP present | Tertiary fallback. Opaque with emission. |

**Critical constraint:** The AssetBundle GerstnerWater shader must be baked in
the Unity bake project (`Tools/Unity-Bake-Project`) targeting the built-in
render pipeline. If the bake project switches to URP, the shader must be
updated to remove `UnityCG.cginc` includes and use URP equivalents, or both
variants must be baked. See ADR-0012-assetbundle-shader-bake-plan for the
bake pipeline details.

**Wave parameter contract:** `WaterSurface.ApplyWaveProfile` writes
`_WaveTime`, `_WaveAmp` (Vector4), `_WaveFreq` (Vector4), `_WaveSpeed`
(Vector4). Only shaders declaring these exact property names receive the C#
wave profile. The AssetBundle GerstnerWater uses `_WaveAmplitude` (float),
`_WaveSteepness`, `_WaveDirX/Z`, `_WaveLength` instead -- it only shares
`_WaveTime`. A future unification pass should align property names across both
shader variants.

---

## 7. Settings

| Setting | Type | Default | Phase | Purpose |
|---------|------|---------|-------|---------|
| `MeshWater` | bool | `false` | 4 | Master toggle. `false` = vanilla flat tile color; `true` = 3D mesh water. |
| `WaterDetail` | float | `1.0` | 10 | Scales wave amplitude, frequency, and speed. Range 0 (flat) to 2 (heavy chop). |

`MeshWater` defaults `false` for new installs. The "Ultra" preset sets it
`true`; the "Minimal" preset sets it `false`.

---

## 8. Test Coverage

`tests/WorldSphereMod.Tests.E2E/MeshWaterInvariantsTests.cs` enforces
source-level invariants:

- `MeshWater` defaults false in `SavedSettings.cs`.
- `WaterGerstner.shader` exists under `Resources/` with expected shader name
  and `_WaveTime` uniform.
- `EnsureMaterial` references the full fallback chain (LoadedShaders cache,
  `Shader.Find`, `Resources.Load`, Standard, URP Lit, URP Unlit).
- Standard fallback avoids the blackworld regression (`_Mode=0`, `_EMISSION`,
  `renderQueue=2000`).
- `enableInstancing` is validated after being set.
- `WaterRender` wires lifecycle, tile suppression, and mask rebuild correctly.

---

## 9. Decision

Use opaque rendering with emission self-illumination as the safe default for
all fallback shaders. Reserve true transparency for when the lighting pipeline
supports it (Phase 5+). Keep the GerstnerWater AssetBundle shader as the
primary path and maintain the Standard/URP fallback chain for compatibility.
Wave displacement lives exclusively in the vertex shader; the CPU side only
advances time and scales wave parameters.
