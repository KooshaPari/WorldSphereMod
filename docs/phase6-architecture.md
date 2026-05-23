# Phase 6 — Skeletal Animation Driver

Source: design pass by `feature-dev:code-architect` (agent run 2026-05-17).
Historical design-state snapshot; use `docs/HANDOFF.md` for current defaults.

---

## 1. Module Layout

Five new files under `WorldSphereMod/Code/Rig/` plus one compute shader, layered as data → computation → integration.

- **`Rig/BoneDefinition.cs`** — data layer. `BoneId` enum, `BoneDefinition` struct (parent index, bind-pose offset, pixel-space region AABB). Shared by all rig types. No Unity or WorldBox deps.
- **`Rig/HumanoidRig.cs`** — 12-bone skeleton: root, hips, spine, head, L/R upper-arm, L/R forearm, L/R upper-leg, L/R lower-leg. Owns static `SegmentVoxels` and `Evaluate`.
- **`Rig/QuadrupedRig.cs`** — 9-bone skeleton: root, spine, neck, head, L/R front-upper, L/R front-lower, L/R rear-upper, L/R rear-lower.
- **`Rig/RigDriver.cs`** — per-frame integration shim. Harmony Postfix on `ActorManager.precalculateRenderDataParallel`. Reads `AnimationFrameData`, calls rig `Evaluate`, pushes `Matrix4x4[]` to skinning compute shader (or CPU fallback), submits skinned mesh.
- **`Rig/RigCache.cs`** — caches `SkinnedVoxelMesh` (base mesh + per-vertex bone index) keyed by `Sprite.GetInstanceID()`. LRU + deferred destroy.

Compute shader: `WorldSphereMod/Resources/Shaders/VoxelSkin.compute`. Loaded via `Resources.Load<ComputeShader>` in `RigDriver.Init`.

Namespace `WorldSphereMod.Rig`.

---

## 2. Public Type Signatures

```csharp
enum BoneId : byte
{
    Root=0, Hips, Spine, Head,
    LArmUpper, LArmLower, RArmUpper, RArmLower,
    LLegUpper, LLegLower, RLegUpper, RLegLower,
    Neck=12, LFrontUpper, LFrontLower, RFrontUpper, RFrontLower,
    LRearUpper, LRearLower, RRearUpper, RRearLower,
}

readonly struct BoneDefinition
{
    int ParentIndex;          // -1 for root
    Vector3 BindPoseOffset;
    RectInt PixelRegion;
}

static class HumanoidRig
{
    static readonly BoneDefinition[] Bones;   // length 12, indexed by BoneId
    static BoneId[] SegmentVoxels(int spriteW, int spriteH, Color32[] pixels);
    static Matrix4x4[] Evaluate(AnimationFrameData fd, float scale);
}

static class RigCache
{
    static int Capacity = 2048;
    static SkinnedVoxelMesh GetOrBuild(Sprite sprite, RigType rigType);
    static void Clear();
    static void DrainPendingDestroy();
}

struct SkinnedVoxelMesh
{
    Mesh BaseMesh;
    byte[] BoneIndices;   // rigid skinning: 1 bone per vertex
}

enum RigType { None, Humanoid, Quadruped }

static class RigDriver
{
    static ComputeShader? _skinCS;
    static bool _gpuSkinning;
    static void Init();
    static void SubmitSkinnedActor(Actor a, ActorRenderData rd, int i,
                                   SkinnedVoxelMesh svm, Matrix4x4[] boneMatrices);
}
```

---

## 3. Voxel Segmentation Heuristic

Deterministic. Same `Color32[]` + dims → same `BoneId[]`. Operates in pixel space, Y=0 at bottom (matches `SpriteVoxelizer.cs:63-75`). Thresholds are fractions of solid-pixel bbox — scale-invariant.

Humanoid steps:

1. **Bbox.** Alpha-threshold `a > 16` → `[x0..x1] × [y0..y1]`.
2. **Head.** Top 20% of rows, plus low-saturation pixels (sat < 0.25, skin-tone proxy) in upper 40% of rows. → `BoneId.Head`.
3. **Arm columns.** Left 15% of cols = left arm band; right 15% = right arm band. Within each, upper half (non-head rows) → `LArmUpper`/`RArmUpper`; lower half → `LArmLower`/`RArmLower`.
4. **Leg columns.** Bottom 30% of rows, split at column midpoint. Per side: upper 50% → `LLegUpper`/`RLegUpper`, lower 50% → `LLegLower`/`RLegLower`.
5. **Torso residual.** Remaining solid pixels above leg band and inside arm cols: upper → `Spine`, lower → `Hips`.
6. **Root.** No pixel assignment; world-space anchor driven by `rd.positions[i]`.

Quadruped: same pattern rotated for horizontal body. Head = rightmost 20% of cols; spine = central horizontal band; four limb groups split by X position + Y midpoint.

---

## 4. AnimationFrameData → Bone Transform Mapping

`AnimationFrameData` is a WorldBox engine type. Only confirmed field in mod source: `size_unit` (Vector2) at `QuantumSprites.cs:541-543`. PLAN references arm-swing, head offset, leg stride as additional signals.

All FrameData values are 2D sprite-scale signals. Mapping is one-way projection:

| Signal | Bone | 3D interpretation |
|---|---|---|
| `size_unit.y/size_unit.x < 0.6` | `Root` | Lying down: tilt root −90° around X |
| Left arm sprite-column delta | `LArmUpper` local Y rot | Col offset → ±45° shoulder |
| Right arm column delta | `RArmUpper` local Y rot | Mirrored |
| Head sprite row offset | `Head` local X rot | Row delta → ±20° pitch |
| Leg stride offset | `LLegUpper` / `RLegUpper` local Y rot | Stride → ±35° hip; lower leg half amplitude |
| No offset (idle) | All | Bind pose — `Matrix4x4.identity` per bone |

Angles clamped to anatomical ranges in `Evaluate`. Unmapped bones return identity. Projection is intentionally lossy: target is silhouette tracking within ~1 voxel at matched frames, not mocap fidelity.

---

## 5. Compute Skinning vs CPU Skinning

`Mod.cs:21` already gates on `SystemInfo.supportsComputeShaders`. Every session has compute available — no new gate.

**Compute (`VoxelSkin.compute`):**
- Single kernel `CSMain`. Inputs: `StructuredBuffer<float3> _Vertices`, `StructuredBuffer<uint> _BoneIndices`, `StructuredBuffer<float4x4> _BoneMatrices`. Output: `RWStructuredBuffer<float3> _SkinnedVertices`.
- `[numthreads(64,1,1)]`, dispatch `(vertexCount + 63) / 64`.
- Output in `GraphicsBuffer(Target.Structured)`, passed to `MeshInstanceBatcher.Submit` as pre-skinned source — no CPU readback.
- 1000 actors × ~200 verts = 200k transforms ≈ 1.5–2 ms on mid-range GPU. Inside PLAN's ≤4 ms budget.

**CPU fallback** (only if `_skinCS` fails to load):
- Plain loop over vertices, per-vertex bone matrix mul.
- 200k float3 transforms ≈ 3–4 ms single-threaded.
- Upload via `SetVertices` + `UploadMeshData(false)`.
- Log `Debug.LogWarning` when fallback activates.

`ProfilerDump` (`SavedSettings.cs:50`) reports GPU skin dispatch time as a named sample.

---

## 6. Crabzilla Migration

`General.cs:313-377` (`FixCrabzilla`) keeps a parallel `List<SpriteRenderer>` and copies transforms each frame. Phase 6 replaces this without breaking the old hack until verified.

Coexistence via the `SkeletalAnimation` flag (already at `SavedSettings.cs:37`, default false):

1. Add early-return guard to both `PrepareCrabzilla` and `UpdateCrabzilla`: `if (Core.savedSettings.SkeletalAnimation && Core.IsWorld3D) return;`. Patches stay registered; bodies become no-ops when new system active.
2. `Rig/CrabzillaRig.cs` — hand-authored multi-mesh hierarchy. One `SkinnedVoxelMesh` per body segment at original `SpriteRenderer` child offsets. `RigDriver` detects `actor.asset.avatar_prefab == "p_crabzilla"`.
3. `DestroyCrabzilla` unchanged — still cleans up `Manager` GameObject.
4. Dragon: identical treatment via `Rig/DragonRig.cs` replacing the `Dragon.create` Postfix that disables the sprite renderer.
5. When `SkeletalAnimation = true` ships, remove the guards in a cleanup commit.

Old hack and new rig share no mutable state. Rollback = one settings flip.

---

## 7. Asset Coverage and Fallback

| Rig | Assets | Est. % |
|---|---|---|
| Humanoid (12 bones) | Swordsmen, archers, mages, civilians, orcs, elves | ~60% |
| Quadruped (9 bones) | Wolves, horses, boars, deer, bears | ~20% |
| Hand-rigged | Crabzilla, Dragon | ~2 assets |
| Static fallback | Birds, fish, snakes, insects | ~18% |

`RigCache.GetOrBuild` returns `RigType.None` for unregistered assets. `RigDriver` skips bone eval + compute dispatch, routes through `VoxelRender.Submit` unchanged — no visual regression vs Phase 1.

Rig lookup: new `Dictionary<string, RigType> ActorRigTypes` in `Constants.cs`. Unregistered IDs → `RigType.None`. External: `RegisterActorRig(string assetId, RigType rig)`.

---

## 8. Build Sequence (one PR, atomic commits)

1. `rig: add BoneDefinition + BoneId` — data only, no Unity deps.
2. `rig: add RigCache with SkinnedVoxelMesh stub` — returns static mesh.
3. `rig: implement HumanoidRig.SegmentVoxels` — determinism test.
4. `rig: implement QuadrupedRig.SegmentVoxels`.
5. `rig: HumanoidRig.Evaluate + QuadrupedRig.Evaluate` — bind-pose first, then animation mapping.
6. `rig: add VoxelSkin.compute shader` — single-actor verification.
7. `rig: wire RigDriver Postfix into ActorVoxelEmit` — CPU fallback path.
8. `rig: compute dispatch + bench 1000 actors via ProfilerDump`.
9. `rig: gate FixCrabzilla behind !SkeletalAnimation; add CrabzillaRig`.
10. `rig: add DragonRig; remove Dragon.create sprite-disable Postfix`.
11. `rig: expose RegisterActorRig (internal + external API)`.
12. `rig: flip SkeletalAnimation=true; update phase table + HANDOFF`.

---

## 9. Files to Create or Modify

**New:**
- `WorldSphereMod/Code/Rig/BoneDefinition.cs`
- `WorldSphereMod/Code/Rig/HumanoidRig.cs`
- `WorldSphereMod/Code/Rig/QuadrupedRig.cs`
- `WorldSphereMod/Code/Rig/RigDriver.cs`
- `WorldSphereMod/Code/Rig/RigCache.cs`
- `WorldSphereMod/Code/Rig/CrabzillaRig.cs`
- `WorldSphereMod/Code/Rig/DragonRig.cs`
- `WorldSphereMod/Resources/Shaders/VoxelSkin.compute`

**Modify:**
- `WorldSphereMod/Code/General.cs:319,358` — flag guard in `PrepareCrabzilla` + `UpdateCrabzilla`.
- `WorldSphereMod/Code/Constants.cs` — `Dictionary<string, RigType> ActorRigTypes`.
- `WorldSphereMod/Code/Core.cs` — call `RigDriver.Init()` from `Core.Init()`.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:106` — route to `RigDriver.SubmitSkinnedActor` when `SkeletalAnimation`.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs` — `VoxelFrameDriver.LateUpdate` drains `RigCache.DrainPendingDestroy()`.
- `WorldSphereMod/Code/WorldSphereAPI.cs` — `RegisterActorRig` internal.
- `WorldSphereAPI/WorldSphereAPI.cs` — `RegisterActorRig` external delegate + binding.

---

## Architectural decisions

- **Rigid skinning over blended.** One bone per vertex. Halves GPU buffer size, sufficient for pixel art where bone regions don't overlap at sub-voxel scale.
- **Segmentation at cache-build time, not voxelization.** `SpriteVoxelizer.Build` stays unchanged. `RigCache.GetOrBuild` runs segmentation independently against the same pixels, stores bone indices alongside mesh. No cascade through `VoxelMeshCache`.
- **`RigDriver` as Postfix overlay, not replacement.** Insert a branch into existing `ActorVoxelEmit.EmitVoxels` Postfix rather than register a competing patch. One Postfix on one method is cleaner.
- **Crabzilla migration via flag guard, not patch removal.** Old `FixCrabzilla` stays registered; body becomes no-op behind a boolean. Lets QA toggle old vs new mid-session.

---

## Key references

- `docs/PLAN.md:136-150` — Phase 6 scope.
- `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs:32-103` — `Build`; segmentation runs against same pixel data.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:93-132` — `ActorVoxelEmit.EmitVoxels`; primary integration point.
- `WorldSphereMod/Code/QuantumSprites.cs:500,541-543` — `AnimationFrameData` read site.
- `WorldSphereMod/Code/General.cs:313-377` — `FixCrabzilla`; migration target.
- `WorldSphereMod/Code/Mod.cs:21` — compute-shader hardware gate.
- `WorldSphereMod/Code/SavedSettings.cs:37` — `SkeletalAnimation` flag.
