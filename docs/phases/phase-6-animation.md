# Phase 6 — Voxel Mesh Animation Kickoff

**Branch:** `feat/phase-6-animation-kickoff`
**Depends on:** Phase 5 (sun + cascaded shadows) — landed
**Status:** Kickoff / planning

---

## Goal

Replace sprite-flip animation with **Vertex Animation Texture (VAT)** driven
voxel mesh animation. The existing `SkeletalAnimation` / `RigDriver` skeletal
path (see `docs/phase6-architecture.md`) handles the rig-eval and compute-
skinning layers. This document scopes the **animation-curve layer** on top:
four motion cycles sampled from baked VAT strips, played back entirely in the
vertex shader with zero CPU work per frame.

**Chosen approach — GPU Vertex Animation Texture.**
Rather than CPU per-frame mesh deformation (which requires a Mesh.SetVertices
upload per actor per frame) or full bone-matrix compute dispatch (Phase 6
Step 8, budget-limited), the VAT approach pre-bakes each animation cycle into
an RGBA32F texture (one texel per vertex per frame). The vertex shader samples
the texture each frame using `unity_DeltaTime` + actor time-offset, applies
the positional delta, and outputs the deformed position. No CPU upload, no
per-frame allocations, compatible with `MeshInstanceBatcher.Flush` because the
deformation lives entirely in the vertex stage.

---

## Functional Requirements

| ID | Requirement |
|----|-------------|
| FR-WSM3D-ANIM-001 | An idle bob animation plays continuously on any visible voxel actor whose `AnimState == Idle` or whose world velocity is below the walk threshold. |
| FR-WSM3D-ANIM-002 | A walk cycle animation plays when an actor is moving; stride frequency scales linearly with world-space speed. |
| FR-WSM3D-ANIM-003 | An attack-swing animation fires as a one-shot when `Actor.attack` is signalled; blends back to idle at the end of the clip. |
| FR-WSM3D-ANIM-004 | A death keel-over animation plays as a one-shot when an actor dies; mesh holds the final frame until the actor is removed. |
| FR-WSM3D-ANIM-005 | All four cycles must be playable simultaneously across at least 500 unique actors with a per-frame GPU cost under 2 ms on RTX 3090 Ti at 1080p. |

---

## Architecture

### 1. VAT Layout

One `Texture2D` per rig family (Humanoid, Quadruped). Layout:

```
cols = max vertex count for the rig (e.g., 256)
rows = total frames across all cycles, concatenated:
    [idle: F_idle rows] [walk: F_walk rows] [attack: F_atk rows] [death: F_die rows]
```

Each texel `(vertexIdx, frameRow)` stores `(dx, dy, dz, 0)` as a positional
delta from the bind pose, encoded as `RGBA32Float`. No alpha channel needed;
pad to 0.

Constants stored as material properties on `OpaqueVertexColor.shader`
(new `_AnimVAT` property block):

| Property | Type | Description |
|---|---|---|
| `_AnimTex` | `Texture2D` | The VAT atlas |
| `_AnimFrameCounts` | `Vector4` | `(F_idle, F_walk, F_atk, F_die)` |
| `_AnimFrameOffsets` | `Vector4` | Row offsets into the atlas per cycle |
| `_AnimFPS` | `float` | Playback FPS (default 12) |
| `_AnimVertexCount` | `int` | Columns in the atlas |

### 2. Per-Actor Instance Data

`MeshInstanceBatcher` already passes a per-instance `Matrix4x4` + `Color32`
tint via `MaterialPropertyBlock`. Phase 6 extends the per-instance block with:

| Property | Type | Source |
|---|---|---|
| `_AnimState` | `int` | 0=idle 1=walk 2=attack 3=death |
| `_AnimTimeOffset` | `float` | `actor.spawnTime % clipDuration` — phases actors so they don't sync |
| `_AnimSpeed` | `float` | walk: world velocity magnitude; attack/death: 1.0 |

These are written in `VoxelRender.cs` during `EmitVoxels` when
`SkeletalAnimation && savedSettings.VoxelEntities`.

### 3. Vertex Shader Sampling

Inside `OpaqueVertexColor.shader`'s `vert()` stage (new block, guarded by
`#ifdef ANIM_ENABLED`):

```hlsl
#ifdef ANIM_ENABLED
float cycleOffset = _AnimFrameOffsets[_AnimState];
float cycleLen    = _AnimFrameCounts[_AnimState];
float t           = fmod(_Time.y * _AnimFPS * _AnimSpeed + _AnimTimeOffset,
                         cycleLen);
float row         = cycleOffset + t;
float u           = (vertexID + 0.5) / _AnimVertexCount;
float v           = (row + 0.5) / _AnimTotalRows;   // _AnimTotalRows = sum of F_*
float3 delta      = tex2Dlod(_AnimTex, float4(u, v, 0, 0)).xyz;
v.vertex.xyz     += delta;
#endif
```

`vertexID` is passed as a custom vertex stream (added in the mesh-build step;
`MeshInstanceBatcher` already supports arbitrary vertex attributes).

### 4. VAT Bake Pipeline (offline, not at runtime)

Baking runs once per rig family change via
`Tools/Bake-AnimVAT.ps1` (to be created in a follow-on commit):

1. For each animation cycle, advance a `VoxelAnimCurve` (stub struct in
   `Code/Animation/VoxelAnimCurve.cs`) frame by frame.
2. For each frame, call `HumanoidRig.Evaluate` (Phase 6 architecture rig) or
   equivalent procedural curve to get per-vertex world deltas.
3. Write deltas into the texture row.
4. Save as `WorldSphereMod/Resources/Textures/HumanoidAnimVAT.asset` and
   `QuadrupedAnimVAT.asset` (Unity `Texture2D`, RGBA32F, non-mipmapped,
   clamp wrap).

For this kickoff phase: stub textures (1×1 black) are sufficient to prove the
shader plumbing compiles and the per-instance properties reach the GPU.

---

## Sub-Tasks

### T-001 — Idle Bob
- Procedural: `dy = A * sin(2π * t / T)` where `A = 0.05 world units`,
  `T = 1.2 s`, applied to `Root` bone delta, written into VAT rows 0..F_idle.
- Root-bone-only deformation — no limb offsets. Cheap to bake.
- Verification: visible bob on all actors in a paused world (no AI movement).

### T-002 — Walk Cycle
- 8-frame walk cycle baked from `HumanoidRig.Evaluate` driven by a synthetic
  `stride` signal in `[−1, 1]` ramping sinusoidally.
- `_AnimSpeed` scales playback; threshold below which idle plays:
  `walkThreshold = 0.3 world units / s` (exposed as `SavedSettings`).
- Verification: actors walking to combat visibly stride; speed scaling holds
  at 0.3x and 3x world velocity.

### T-003 — Attack Swing
- One-shot 6-frame clip: wind-up (frames 0–2), strike (frame 3), recovery
  (frames 4–5). Right arm upper/lower bones only for humanoids.
- Triggered via new `VoxelAnimState` field on `ActorRenderData` written by a
  Postfix on `ActorManager.attack` (confirm method name against decompile
  before implementing; see `CLAUDE.md` NML Publicizer trap note).
- Clamps at final frame after one play; resets to idle on next cycle.

### T-004 — Death Keel-Over
- One-shot 8-frame clip: actor tilts −90° around X over 4 frames (root delta
  only), then holds flat.
- Triggered by actor `IsAlive == false` check in `EmitVoxels`.
- Mesh holds frame 7 permanently (final `_AnimTimeOffset` frozen at
  `F_idle + F_walk + F_atk + F_die - 1`).

---

## Files to Touch

| File | Change |
|------|--------|
| `WorldSphereMod/Code/Render/VoxelRender.cs` | Add `_AnimState`, `_AnimTimeOffset`, `_AnimSpeed` writes in `EmitVoxels`; add `VoxelAnimState` enum; add `walkThreshold` gate. |
| `WorldSphereMod/Resources/Shaders/OpaqueVertexColor.shader` | Add `_AnimTex`, `_AnimFrameCounts`, `_AnimFrameOffsets`, `_AnimFPS`, `_AnimVertexCount`, `_AnimTotalRows` properties + `ANIM_ENABLED` keyword + `tex2Dlod` sampling block in `vert()`. |
| `WorldSphereMod/Code/SavedSettings.cs` | Add `AnimationEnabled` (default `false`, Phase 6 gate) + `WalkThreshold` (`0.3f`). |
| `WorldSphereMod/Code/Animation/VoxelAnimCurve.cs` | New stub: `VoxelAnimState` enum + `VoxelAnimCurve` struct with frame-delta API used by the offline bake script. |
| `Tools/Bake-AnimVAT.ps1` | Offline bake script (follow-on commit — stub OK at kickoff). |
| `WorldSphereMod/Resources/Textures/HumanoidAnimVAT.asset` | 1×1 black stub texture (follow-on commit). |
| `WorldSphereMod/Resources/Textures/QuadrupedAnimVAT.asset` | 1×1 black stub texture (follow-on commit). |
| `WorldSphereMod/Code/Constants.cs` | Add `AnimVATFrameCounts`, `AnimVATFrameOffsets` constants mirrored to shader properties. |
| `docs/smoke-test-phase6.md` | Add VAT animation smoke tests for each cycle. |

---

## Interaction with Phase 6 Architecture (Skeletal Rig)

`docs/phase6-architecture.md` describes the `RigDriver` / `VoxelSkin.compute`
compute-skinning path. The VAT approach is **layered on top**, not a
replacement:

- When `AnimationEnabled && !GpuProceduralSkinning`: VAT vertex shader
  deformation handles visible motion. No compute dispatch.
- When `AnimationEnabled && GpuProceduralSkinning` (ADR-0006 path): compute
  skinning writes the full deformed mesh; VAT is skipped to avoid double-
  deformation. The `#ifdef ANIM_ENABLED` guard in the shader is set per-
  material instance to prevent overlap.
- The `SkeletalAnimation` flag gates the rig-segmentation + bone-eval path.
  `AnimationEnabled` gates the VAT path. Both can be false (no animation),
  one true (hybrid), or both true only under the compute path where VAT is
  suppressed.

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `tex2Dlod` in `vert()` not available on NML's Unity version | Low | High | Gate `#pragma require 2darray` / test with `#pragma target 3.5`; fallback: pass pre-sampled delta as per-vertex stream from CPU |
| VAT texture RGBA32F not supported on all platforms | Low | Medium | Check `SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat)` at init; degrade to RGBA16 with 0.01-unit precision loss |
| `ActorManager.attack` method name changes across WorldBox versions | Medium | Medium | Pin against decompiled name in `Constants.cs` with `// verified against worldbox 0.22.6`; add a unit test that asserts the patch target resolves |
| Texture memory at 500 actors | Very Low | Low | Single VAT texture shared across all actors of same rig family; memory = `256 cols × (F_idle+F_walk+F_atk+F_die) rows × 16 bytes ≈ 256 KB` — negligible |
| VAT + Compute skinning double-deformation | Low | High | `#ifdef ANIM_ENABLED` guard; set via `material.DisableKeyword("ANIM_ENABLED")` when `GpuProceduralSkinning` is active |

---

## Acceptance Criteria (Phase 6 Ship Gate)

- [ ] FR-WSM3D-ANIM-001 through FR-WSM3D-ANIM-005 pass in-game on a 256×256 map.
- [ ] `AnimationEnabled = true` default committed; `WalkThreshold` tuned.
- [ ] `docs/smoke-test-phase6.md` updated with passing screenshots for all 4 cycles.
- [ ] `docs/HANDOFF.md` Phase 6 row updated from `scaffolding` to `landed`.
- [ ] No regression on Phase 1–5 smoke tests.
- [ ] `dotnet test` green (`tests/WorldSphereMod.Tests.Unit/ + Integration/`).

---

## Build Sequence (proposed commits)

1. `anim: add VoxelAnimCurve stub + AnimationEnabled + WalkThreshold flags`
2. `anim: add VAT property block to OpaqueVertexColor.shader (ANIM_ENABLED keyword, no sampling yet)`
3. `anim: stub 1×1 HumanoidAnimVAT + QuadrupedAnimVAT textures; wire _AnimTex to material`
4. `anim: add _AnimState / _AnimTimeOffset / _AnimSpeed writes in VoxelRender.EmitVoxels`
5. `anim: enable tex2Dlod sampling in vert(); idle bob VAT rows`
6. `anim: bake walk-cycle rows into HumanoidAnimVAT; wire walkThreshold`
7. `anim: bake attack-swing rows; wire ActorManager.attack Postfix`
8. `anim: bake death keel-over rows; freeze final frame on !IsAlive`
9. `anim: QuadrupedAnimVAT idle + walk (attack/death = static for P6)`
10. `anim: flip AnimationEnabled=true; update phase table + HANDOFF + smoke tests`

---

## Key Cross-References

- `docs/phase6-architecture.md` — skeletal rig architecture (RigDriver, RigCache, BoneDefinition)
- `docs/adr/ADR-0006-phase-6-step-9-drawprocedural-skinning.md` — GPU compute path (Phase 6 Step 9)
- `docs/journeys/scratch/phase6-rig-variety-spec.md` — confirmed actor asset IDs + rig families
- `WorldSphereMod/Code/Render/VoxelRender.cs` — `EmitVoxels` (animation state write site)
- `WorldSphereMod/Resources/Shaders/OpaqueVertexColor.shader` — vertex shader (sampling site)
- `WorldSphereMod/Code/Rig/HumanoidRig.cs` — `Evaluate` (bake-time input)
- `CLAUDE.md` — `EmitVoxels` gate pattern, NML Publicizer trap, actor manager method caveats
