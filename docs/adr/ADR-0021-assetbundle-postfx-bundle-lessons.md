# ADR-0021: AssetBundle PostFX Shader Bake — Version-Mismatch Root Cause, Variant-Stripping, and Crash-Safe Gating Design

**Status:** Accepted (crash-safe gating active). PostFX resolution: Deferred.

**Status updates:**
- 2026-06-06: L1 re-investigation re-confirmed `PostFxShaderBundleAvailable = false` as the long-term stable state. Prior L1 logs claimed the bake was on Unity 2022.3.62f3 vs runtime 60f1; actual `Tools/Unity-Bake-Project/ProjectSettings/ProjectVersion.txt` and `Tools/bake-shaders.log` show the bake now runs on **2022.3.60f1** (same revision as the player). Version-mismatch is **not** the root cause. Despite the match, all four attempts still produce 80-byte stubs at player runtime:
  1. b724b40e (SafeShaders expand + 14/14 validate) → crash
  2. 9c1c4488 (SVC -> m_PreloadedShaders + 12/0 subshaderCount) → editor-side false positive, player-side crash
  3. b206c1d3 (WSM3D_POSTFX_KEEP pragma on BrpACES/BrpBloom/ScreenSpaceGI + SVC +2 variants) → player still reads 80 bytes for ColorGradingLUT/ProceduralSky/ScreenSpaceAO and 8 bytes for CompoundSphereCompute; reverted in 36d57d9a
  4. 36d57d9a (revert) → current state
  Decision stands: the mod cannot modify the WorldBox player or trigger a rebuild, so the in-tree fix surface is exhausted. Re-enable criteria in "Future Resolution Path" § must all hold before any flip. Inline Core.cs:2318 comment now reads as documented long-term state, not "PLAYER-TEST REVERT".
- 2026-06-01: ADR accepted.

**Supersedes:** ADR-0012 (AssetBundle shader bake plan — that ADR described the initial plan; this ADR records what actually happened and why)

**Date:** 2026-06-01

**Author:** quality-mgr / WSM3D team

**Stakeholders:** WorldSphereMod/Code/Core.cs, any future shader bake work

---

## Context

WSM3D ships custom shaders (OpaqueVertexColor, postFX pipeline, compute) in an
`AssetBundle` named `wsm3d-shaders`. At runtime the bundle is loaded via
`AssetBundleUtils.GetAssetBundle` and shaders are resolved with `GetObject<Shader>`.

During the session that produced issues #204 and #208, the postFX shaders loaded
as empty stubs that caused a native crash inside Unity's `GetObject` path. Three
separate fix attempts were made; all failed. This ADR records the root cause,
the three failed approaches, and the final crash-safe gating design adopted as
the stable interim state.

### Problem Statement

PostFX shaders loaded from the `wsm3d-shaders` AssetBundle appeared valid in
the Unity editor (non-zero `subshaderCount`) but were empty stubs at player
runtime. Any attempt to call `GetObject<Shader>` on these stubs caused a
native crash, making the entire mod unloadable if postFX shaders were included
in the safe-load list.

### Forces

- WSM3D cannot modify the WorldBox player binary or Unity project settings.
- The mod is delivered as NML-loaded assemblies; it cannot trigger a player rebuild.
- Shaders must load without the Unity editor present.
- OpaqueVertexColor (OVC) loads correctly from the bundle; only postFX shaders crash.
- PostFX rendering (SSAO, bloom, tonemapping, sky) is a desired feature (FR-WSM-010).

---

## Root Cause: AssetBundle Version Mismatch

The `wsm3d-shaders` bundle was baked in a Unity editor installation whose
version did not match the Unity runtime version inside the WorldBox player.

Unity's AssetBundle format embeds a serialized object graph that is
version-specific. When the player's Unity runtime reads a bundle baked by a
different version, scalar fields deserialize but shader `SubShader` passes are
empty — the runtime produces a `Shader` object with `subshaderCount > 0` but
zero valid render passes. Calling `GetObject<Shader>` on these stubs triggers
an internal Unity assertion that surfaces as a native crash.

**Key finding:** `subshaderCount > 0` in the Unity editor is a FALSE POSITIVE.
It does not guarantee that the player-runtime shader has valid passes. Editor
validation of subshader counts cannot be used to certify runtime safety.

---

## Three Failed Approaches (#208)

### Approach 1 — SVC Preload (`GraphicsSettings.m_PreloadedShaders`)

Register a `ShaderVariantCollection` (SVC) in `GraphicsSettings.m_PreloadedShaders`
to pin variants and prevent Unity's build-time stripping.

**Why it failed:** SVC preload only takes effect during a player build. It pins
variants so the build pipeline does not strip them. It has no effect on an already-
baked bundle whose serialization is mismatched. The runtime still deserializes
stubs from the bundle regardless of what the SVC says.

### Approach 2 — `POSTFX_KEEP` keyword on shaders

Add a custom `#pragma multi_compile POSTFX_KEEP` keyword to postFX shaders so
the build pipeline's keyword-keeplist mechanism retains the variant.

**Why it failed:** keyword-keep guards apply at shader compilation / build time,
not at AssetBundle load time. The bundle was already baked with a version-mismatched
editor; no amount of keyword annotation in source fixes the binary serialization
mismatch already stored in the `.bundle` file.

### Approach 3 — Worldsphere-fold (split postFX into a separate bundle)

Move postFX shaders into a new `worldsphere` bundle, keeping OVC in `wsm3d-shaders`.

**Why it failed:** the split does not change the bake toolchain. Both bundles would
still be baked in the same mismatched editor environment. The postFX shaders
remain stubs in the new bundle.

---

## Decision: Crash-Safe Triple-Flag Gating

Adopt a two-constant, one-array design in `Core.Sphere` that separates OVC
availability from postFX availability:

```csharp
// In Core.Sphere (WorldSphereMod/Code/Core.cs ~line 1903)
public const bool PostFxShaderBundleAvailable = false; // postFX stubs — crash-safe
public const bool ShaderBundleAvailable = false;       // current crash-safe default
                                                        // (set true when OVC loads cleanly)
public static readonly string[] SafeShaders = { ... }; // runtime-confirmed safe set
```

| Flag | Meaning | Current value | Re-enable when |
|------|---------|---------------|----------------|
| `ShaderBundleAvailable` | The non-postFX portion of the bundle (OVC, etc.) loads without crashing | `false` (full crash-safe) | OVC confirmed stub-free at runtime |
| `PostFxShaderBundleAvailable` | The postFX shader portion of the bundle is safe to load | `false` | Version-mismatch resolved + runtime-validated |
| `SafeShaders` | Enumeration of shader names confirmed safe for `GetObject<Shader>` | OVC + non-postFX confirmed set | Expand as shaders are validated |

The `PostFxShaderBundleAvailable` flag is a `const` (not a runtime variable) so
it cannot be flipped accidentally; it encodes a build-time decision about the
bake state.

### Implementation Notes

- `Core.cs:LoadAssets` gates the entire bundle load on `ShaderBundleAvailable`.
- Inside the load loop, each shader name is checked against `PostFxShaderNames`;
  if `PostFxShaderBundleAvailable == false` the shader is skipped with a warning.
- `SafeShaders` is a named `string[]` constant (not an inline literal) so tests
  can assert its exact contents.
- All three symbols are tested by `VoxelPipelineRegressionTests` (invariants 6a, 6b,
  7, 10, 11) and cannot drift without a test failure.

---

## Consequences

### Positive

- Crash eliminated — the mod loads cleanly with postFX disabled.
- OVC and non-postFX shaders can be re-enabled independently without risk.
- The design is self-documenting: the two `const bool` flags make the bake state
  explicit and searchable.
- Tests enforce the contract so future editors cannot accidentally enable postFX
  without resolving the bake issue.

### Negative

- PostFX features (FR-WSM-010: bloom, SSAO, sky, tonemapping) are unavailable
  until the bake is fixed.
- Requires tracking the Unity editor version used to bake the bundle going forward.

### Neutral

- `SafeShaders` may need updating if new non-postFX shaders are added to the bundle.

---

## Future Resolution Path

To re-enable postFX shaders:

1. Identify the exact Unity editor patch version that matches the WorldBox player's
   embedded runtime (check `Application.unityVersion` at runtime; compare to the
   editor version used to bake the bundle).
2. Re-bake `wsm3d-shaders` using that exact editor version.
3. Add a **runtime** `subshaderCount` validation immediately after `GetObject<Shader>`
   (not editor-side): any shader with zero passes at runtime must be rejected and
   logged before the load proceeds.
4. If runtime validation passes for all postFX shaders, set
   `PostFxShaderBundleAvailable = true` and `ShaderBundleAvailable = true`.
5. Consider shipping shaders as embedded HLSL text + runtime-compiled `ComputeShader`
   as a long-term alternative to AssetBundle delivery, eliminating the version-mismatch
   class of problem entirely.

---

## References

- Issues: #204 (postFX stub crash), #208 (three fix attempts)
- Related ADRs: ADR-0012 (original bake plan, superseded), ADR-0013 (postFX pipeline)
- Code anchors:
  - `WorldSphereMod/Code/Core.cs:1903` — triple-flag declarations
  - `WorldSphereMod/Code/Core.cs:1591` — `ShaderBundleAvailable` gate in `LoadAssets`
  - `WorldSphereMod/Code/Core.cs:1652` — `PostFxShaderBundleAvailable` inner gate
- Tests:
  - `tests/WorldSphereMod.Tests.E2E/VoxelPipelineRegressionTests.cs` — invariants 10, 11
  - `tests/WorldSphereMod.Tests.E2E/VoxelPipelineRegressionTests.cs:Core_shader_load_list_matches_SafeShaders_exactly`

---

> Phenotype ADR conventions: keep ADRs short (1–3 screens), one decision per ADR,
> link out to architecture / journey docs rather than restating them.
