# ADR-0013: Post-Processing Effects Pipeline

**Status:** Accepted

**Date:** 2025-05-25

**Author:** Claude / KooshaPari

**Stakeholders:** WorldSphereMod3D (Phase 9), phenotype-postfx (shared BRP library)

---

## Context

Phase 9 of WorldSphereMod3D adds post-processing effects: screen-space
ambient occlusion (SSAO), screen-space global illumination (SSGI), bloom,
ACES filmic tonemapping, and LUT color grading. WorldBox ships Unity's
Built-in Render Pipeline (BRP), not URP. None of the standard
post-processing stacks (URP Volume, PostProcessing v2, Unity 2023 Volume)
are available at runtime.

### Problem Statement

Enabling any PostFX toggle caused a **black camera** -- the entire viewport
rendered solid black. The root causes were threefold: (1) the shader
AssetBundle failed to compile or load for some shaders, leaving materials
null; (2) the legacy per-effect MonoBehaviours (`ScreenSpaceAO`,
`ScreenSpaceGI`, `ColorGradingLUT`) each had their own `OnRenderImage`
callback, creating a multi-component blit chain where any one null material
broke the chain and swallowed the frame; and (3) an earlier URP-based
approach (`PostFxController`) used reflection to instantiate URP Volume
types that do not exist in WorldBox's BRP runtime, silently failing and
leaving the camera in a half-configured state.

### Forces

- WorldBox ships BRP (Unity 2022.3 LTS). URP runtime assemblies are not
  present. `Shader.Find("Universal Render Pipeline/*")` resolves names but
  instancing/post-processing variants are stripped.
- `OnRenderImage(src, dst)` is the only reliable BRP image-effect hook.
  If a MonoBehaviour with `OnRenderImage` does not blit src to dst on every
  code path, the camera output is black.
- Shaders must be delivered via an AssetBundle baked in Unity 2022.3 to
  match the WorldBox player's shader compiler version. Shaders compiled
  against a mismatched Unity version produce silent load failures --
  `Shader.Find` returns null, `Resources.Load<Shader>` returns null, and
  the bundle's `LoadAsset` returns a shader object whose `isSupported` is
  false.
- The mod runs as a NeoModLoader plugin with no control over the rendering
  pipeline configuration or shader stripping settings.
- Multiple `OnRenderImage` MonoBehaviours on the same camera execute in
  undefined component order. A single unified stack is required for
  deterministic pass ordering.

### Alternatives Considered

| Alternative | Pros | Cons | Why not chosen |
|---|---|---|---|
| URP Volume + VolumeProfile (PostFxController) | Clean API, future-proof | URP types absent at runtime; pure-reflection approach fragile and produced silent failures | WorldBox ships BRP, not URP |
| Per-effect MonoBehaviours (legacy ScreenSpaceAO, ScreenSpaceGI, ColorGradingLUT) | Simple, one effect per file | Undefined execution order; any null material breaks the blit chain causing black camera; no shared ping-pong RT management | Root cause of the black-camera bug |
| CommandBuffer-based injection | Explicit insert point in camera event | Heavier weight, harder to debug, overkill for 5 passes | Reserved for Forward+ renderer (Phase 10+) |
| Compute shader post-processing | GPU-efficient, no blit overhead | Requires compute support gate already present but adds complexity; BRP compute dispatch from OnRenderImage is awkward | Deferred to URP migration |

## Decision

Use a **single `WSM3DPostStack` MonoBehaviour** attached to the main
camera that owns the entire post-processing chain through one
`OnRenderImage(src, dst)` callback. The stack uses a ping-pong
`RenderTexture` pair for intermediate passes and writes the final result
to `dst`. Every code path -- including disabled passes, null materials,
and missing shaders -- guarantees a `Graphics.Blit(src, dst)` so the
camera never goes black.

### Architecture

```
Camera.OnRenderImage(src, dst)
    |
    v
WSM3DPostStack (singleton MonoBehaviour)
    |
    |-- PreCheck: if !_initialized || !PostFX -> Blit(src, dst), return
    |-- EnsurePingPong(src)
    |
    |-- Pass 1: SSAO          [if SSAOEnabled && _ssaoMat != null]
    |     Blit(cur, next, _ssaoMat) -> swap
    |
    |-- Pass 2: SSGI          [if SSGIEnabled && _ssgiMat != null]
    |     Blit(cur, next, _ssgiMat) -> swap
    |
    |-- Pass 3: Bloom          [if BloomEnabled && _bloomMat != null]
    |     Pass 3a: Threshold extract (1/4 res)
    |     Pass 3b: Gaussian blur horizontal
    |     Pass 3c: Gaussian blur vertical
    |     Pass 3d: Composite (additive blend onto source)
    |     swap
    |
    |-- Pass 4: ACES tonemap   [if ACESTonemapping && _acesMat != null]
    |     Blit(cur, next, _acesMat) -> swap
    |
    |-- Pass 5: LUT grade      [if ColorGradingLut && _lutMat != null]
    |     Blit(cur, dst, _lutMat)   [terminal -- writes to dst]
    |
    |-- Fallback: Blit(cur, dst) if no LUT pass
    |
    v
  ReleasePingPong()
```

The ping-pong scheme allocates one `RenderTexture.GetTemporary` at frame
resolution. Bloom additionally allocates two quarter-resolution temporaries
for the blur passes, released in a `finally` block. The single ping RT is
released after every frame to avoid holding VRAM across resolution changes.

### Shader Resolution Chain

Each material is resolved through a 5-step fallback chain in
`TryLoadMaterial`:

1. **Bundle cache** -- `Core.Sphere.LoadedShaders[cacheKey]` populated by
   the wsm3d-shaders AssetBundle at mod load time.
2. **Shader.Find with WSM3D/ prefix** -- catches shaders registered under
   their AssetBundle-declared name (e.g. `WSM3D/ScreenSpaceAO`).
3. **Resources.Load** -- only works inside a Unity project
   `Assets/Resources`; never resolves at WorldBox runtime but kept for
   editor testing.
4. **Shader.Find with Hidden/ fallback** -- catches shaders registered
   under their source-file declared name (e.g. `Hidden/ScreenSpaceAO`).
5. **Shader.Find with Hidden/ + cacheKey** -- catches naming mismatches
   between the fallback name and the cache key.

If all five steps return null, the material is left null and that pass is
skipped at render time. If **all** materials are null, the stack sets a
static `_shadersUnavailable` flag, logs a warning, and self-destructs to
prevent a black camera. Subsequent `EnsureCreated` / `ApplySetting` calls
are no-ops until the bundle is rebaked.

### Self-Destruct and Re-Creation Guard

The legacy per-effect approach had a self-destruct/re-creation loop:
`VoxelRender.TickPerFrame` would detect the component was missing (because
it self-destructed on shader failure), re-add it, which would fail again,
creating a per-frame allocate-destroy cycle. The unified stack breaks this
loop with the static `_shadersUnavailable` sentinel -- once set, no new
`WSM3DPostStack` instances are created until the mod is reloaded with a
working shader bundle.

### Legacy Component Cleanup

On `Awake`, `WSM3DPostStack.RemoveLegacyPasses()` finds and destroys any
existing `ScreenSpaceAO`, `ScreenSpaceGI`, or `ColorGradingLUT`
MonoBehaviours on the camera. This prevents duplicate `OnRenderImage`
callbacks and undefined pass ordering. The legacy classes remain in the
codebase for backward compatibility and static kernel generation
(`BuildKernelStatic`) but are no longer attached to the camera when the
unified stack is active.

### Shader Compilation: Bake Project vs Runtime Resources

Two copies of each shader exist:

| Shader | Resources/Shaders/ (runtime source) | Tools/Unity-Bake-Project/ (bake source) | Differences |
|---|---|---|---|
| ScreenSpaceAO | `Hidden/ScreenSpaceAO` -- 16-tap dynamic kernel via uniform `_Samples[16]` array, `[loop]` directive, `_SampleCount` early-out | `WSM3D/ScreenSpaceAO` -- 8-tap hardcoded rotated kernel via `GetKernelSample()`, `[unroll]`, `vert_img` built-in | **Different algorithms.** Bake version uses `vert_img` and `_MainTex_TexelSize`; runtime version uses custom vertex struct and `_ScreenParams`. Bake version compiles cleanly in 2022.3; runtime version also compiles but uses a different (configurable) kernel. |
| ScreenSpaceGI | `Hidden/ScreenSpaceGI` -- 12-tap dynamic kernel, same pattern as SSAO runtime | `Hidden/ScreenSpaceGI` -- identical to runtime | **Identical.** Both compile in 2022.3. |
| BrpBloom | `Hidden/WSM3D/BrpBloom` -- 4-pass (threshold, blur H, blur V, composite) | `Hidden/WSM3D/BrpBloom` -- identical | **Identical.** Compiles in 2022.3. |
| BrpACES | `Hidden/WSM3D/BrpACES` -- single-pass Narkowicz ACES fit | `Hidden/WSM3D/BrpACES` -- identical | **Identical.** Compiles in 2022.3. |
| ColorGradingLUT | `Hidden/ColorGradingLUT` -- 32-slice strip LUT, `_LutTex`/`_LookupTex` dual property, `_LutParams.z` guard | `WSM3D/ColorGradingLUT` -- `_LUT_Tex2D` property, `_LUT_Strength`/`_Exposure`/`_Saturation` controls, `vert_img` | **Different interfaces.** Bake version has exposure/saturation pre-grading and a strength lerp. Runtime version is simpler but the property names differ (`_LutTex` vs `_LUT_Tex2D`), causing the PostStack's `SetTexture` calls to miss on the bake shader. The PostStack mitigates this with a three-way property probe (`_LutTex`, `_LookupTex`, `_LUT_Tex2D`). |

The bake project SSAO uses `WSM3D/ScreenSpaceAO` as its shader name,
which the runtime `TryLoadMaterial` finds via step 2 of the resolution
chain. The runtime Resources copy uses `Hidden/ScreenSpaceAO`, found via
step 4. When the AssetBundle loads successfully, the bake version wins
because step 1 (bundle cache) is checked first.

**Shaders that fail to compile in the bake project** produce a null entry
in the AssetBundle. The runtime falls through to `Shader.Find` with the
Hidden/ name, which only resolves if the shader source was also registered
via `Resources.Load` -- which it is not at WorldBox runtime. This is why
a bake failure for any shader results in a null material rather than a
graceful fallback.

### SavedSettings Flags

| Flag | Default | Effect |
|---|---|---|
| `PostFX` | `false` | Master gate: entire WSM3DPostStack |
| `SSAOEnabled` | `false` | Pass 1 |
| `SSGIEnabled` | `false` | Pass 2 |
| `BloomEnabled` | `false` | Pass 3 |
| `ACESTonemapping` | `true` | Pass 4 (on by default when PostFX is on) |
| `ColorGradingLut` | `false` | Pass 5 |
| `SSAOQuality` | `Medium` | Low/Medium/High: 8/12/16 samples, radius 1.6/2.0/2.4 |

All flags ship default-OFF (except ACES) per ADR-0005 phase-ship-gate
policy. The `SavedSettings.SafeDefaults()` preset disables all; the
`HighDefaults()` preset enables all.

### Implementation Notes

- **File:** `WorldSphereMod/Code/PostFx/WSM3DPostStack.cs` -- unified
  stack, singleton, 380 lines.
- **File:** `WorldSphereMod/Code/PostFx/ScreenSpaceAO.cs` -- legacy
  component, still used for `BuildKernelStatic()` and `Kernel[]` array.
- **File:** `WorldSphereMod/Code/PostFx/ScreenSpaceGI.cs` -- legacy
  component, still used for `BuildKernelStatic()` and `Kernel[]` array.
- **File:** `WorldSphereMod/Code/Fx/PostFxController.cs` -- URP Volume
  reflection path, retained for potential URP-capable builds but not the
  primary runtime.
- **Shaders (runtime):** `WorldSphereMod/Resources/Shaders/ScreenSpaceAO.shader`,
  `ScreenSpaceGI.shader`, `BrpBloom.shader`, `BrpACES.shader`,
  `ColorGradingLUT.shader`.
- **Shaders (bake):** `Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/`
  -- same set plus `StratumVoxelPBR.shader`, `Impostor.shader`.
- **Spec:** `docs/specs/onrenderimage-postfx-spec.md`.
- **E2E tests:** `SsaoPostFxInvariantsTests.cs`,
  `OnRenderImagePostFxSpecInvariantsTests.cs`,
  `VoxelFrameDriverPostFxInvariantsTests.cs`,
  `WSM3DPostStackInvariantsTests.cs`.
- **Wiring:** `Core.ApplyPhaseToggle` -> `WSM3DPostStack.ApplySetting`;
  `VoxelRender.TickPerFrame` reconciles via `WSM3DPostStack`; `Mod`
  world-init calls `WSM3DPostStack.EnsureCreated()`.

## Consequences

### Positive

- The black-camera bug is eliminated. Every `OnRenderImage` code path
  ends with a blit to `dst`.
- Pass ordering is deterministic and documented: SSAO -> SSGI -> Bloom ->
  ACES -> LUT.
- The self-destruct/re-creation loop is broken by the static
  `_shadersUnavailable` sentinel.
- Individual passes degrade gracefully: a missing shader skips that pass
  rather than breaking the chain.
- The shader resolution chain provides five fallback levels, making the
  stack resilient to partial bundle loads.
- Legacy components are cleaned up automatically on stack attach.

### Negative

- Two divergent copies of some shaders (SSAO, ColorGradingLUT) exist
  between the runtime Resources and the bake project. Property name
  mismatches between the two LUT shader versions require a three-way
  property probe in `InitMaterials`.
- The ping-pong RT is allocated and released every frame. This is correct
  for handling resolution changes but adds minor GC pressure from the
  temporary RT pool.
- Bloom allocates two additional quarter-resolution RTs per frame. At
  1080p this is ~2MB transient VRAM; at 4K it is ~8MB.
- The `_shadersUnavailable` flag is static and sticky for the lifetime of
  the mod load. A hot-reload of the shader bundle cannot clear it without
  restarting the game.

### Neutral

- `PostFxController` (URP Volume path) remains in the codebase but is
  effectively dead code at WorldBox runtime. It serves as documentation
  of the intended URP approach and would activate if URP assemblies
  became available.
- The legacy `ScreenSpaceAO` and `ScreenSpaceGI` MonoBehaviours remain
  compilable and functional but are never attached to the camera when the
  unified stack is active. They provide the static kernel arrays consumed
  by `WSM3DPostStack.ApplySSAOParams` and `ApplySSGIParams`.

## Future Work

### Failing Shader Resolution

For shaders where the bake version and runtime version diverge (SSAO,
ColorGradingLUT), the decision is to **fix the bake-project source** to
match the runtime shader interface rather than maintain two divergent
implementations. Specifically:

- **SSAO:** The bake project should adopt the dynamic-kernel approach
  (uniform `_Samples[16]` array with `_SampleCount`) so the C# quality
  tiers work identically regardless of which shader version loads. The
  hardcoded 8-tap rotated kernel in the bake version cannot express the
  Low/Medium/High quality presets.
- **ColorGradingLUT:** The bake project should use `_LutTex` as the
  primary property name (matching the runtime version) and add
  `_LUT_Tex2D` as an alias. The exposure/saturation pre-grading in the
  bake version is useful and should be back-ported to the runtime version.

### URP Migration Path

When WorldBox upgrades to a Unity version with accessible URP assemblies:

1. `PostFxController` becomes the primary path.
2. `WSM3DPostStack` becomes the BRP fallback (detected at runtime via
   `GraphicsSettings.currentRenderPipeline != null`).
3. Custom shaders transition to URP Shader Graph or HLSL with URP
   includes.
4. The `OnRenderImage` hook is replaced by `ScriptableRenderPass` /
   `RendererFeature`.

### Compute-Based PostFX

For effects that benefit from shared memory (bilateral blur for SSAO,
separable bloom with compute dispatch):

1. Gate on `SystemInfo.supportsComputeShaders` (already checked in
   `Mod.OnLoad`).
2. Add compute kernel variants alongside the fragment shaders.
3. `WSM3DPostStack` selects compute vs fragment path per-effect at
   `InitMaterials` time.
4. Compute path uses `CommandBuffer.DispatchCompute` + `Blit` rather than
   pure `Graphics.Blit`.

### Temporal Effects

Motion blur, temporal anti-aliasing (TAA), and temporal upscaling require:

- A velocity buffer (motion vectors from `DepthTextureMode.MotionVectors`).
- Jittered projection matrices (sub-pixel offset per frame).
- History buffer management (previous frame's color/depth).

These are out of scope for the current BRP `OnRenderImage` approach and
are deferred to the Forward+ renderer work (Phase 10+) or URP migration.

## References

- Related ADRs: ADR-0012 (AssetBundle shader bake plan), ADR-0002 (defer
  shader bake to Unity 2022.3), ADR-0003 (reflective URP bindings),
  ADR-0005 (default-on flags per phase ship gate)
- Spec: `docs/specs/onrenderimage-postfx-spec.md`
- Architecture: `docs/phase9-architecture.md`
- Code anchors: `WorldSphereMod/Code/PostFx/WSM3DPostStack.cs`,
  `WorldSphereMod/Code/Fx/PostFxController.cs`

---

> Phenotype ADR conventions: keep ADRs short (1-3 screens), one decision
> per ADR, link out to architecture / journey docs rather than restating
> them. Status changes (`Accepted` -> `Superseded`) are appended at the top
> with a date; don't delete history.
