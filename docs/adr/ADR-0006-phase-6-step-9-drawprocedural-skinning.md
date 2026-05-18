# ADR 0006 — Phase 6 Step 9: DrawProcedural skinning for multi-actor GPU compute path

**Status:** Proposed

**Date:** 2026-05-18

**Author:** KooshaPari

**Stakeholders:** Phase 6 (skeletal animation), Phase 10 (perf budget)

---

## Context

Phase 6 Step 7 introduced GPU compute skinning via `VoxelSkin.compute`: a kernel that runs per (sprite, rig) base mesh once per frame and writes deformed vertices to a proxy mesh, which `MeshInstanceBatcher` then renders. This works for single actors per (sprite, rig) key, but under Phase 10's multi-actor batching goal, when N actors share the same voxelized sprite + humanoid rig, all N instances write to the SAME proxy mesh. At `MeshInstanceBatcher.Flush` time, the vertex buffer holds whichever actor was processed last; earlier actors in the batch read stale vertices. The current workaround is to force `_gpuOK = false` in `RigDriver.cs` line 90, falling back to CPU bind-pose per-actor every frame — this defeats the entire GPU path and breaks the Phase 10 perf budget (target: 1000 skinned actors).

The root cause: the proxy-mesh path conflates per-(sprite, rig) *geometry* with per-actor *output* locality. When multiple actors need the same skinned mesh, they can't share a single writable target.

### Problem Statement

Re-enable GPU compute skinning while preserving per-actor instance uniqueness, so multi-actor batches render without vertex contention or CPU readback.

### Forces

- **Uniqueness requirement.** Each of N actors sharing a (sprite, rig) key must have its own deformed vertex slice, indexed at render time.
- **GPU-resident data.** CPU readback from the proxy mesh every frame kills performance. Vertices must stay on the GPU.
- **Indirect rendering.** `MeshInstanceBatcher` uses instancing; the vertex shader must map instance ID to the correct per-actor vertex data without a second dispatch or a per-instance mesh.
- **Custom shader coupling.** The placeholder material (`Sprites/Default`) can't sample a StructuredBuffer; a custom shader is required, tying this decision to Phase 5b (shader bake).
- **Culling loss.** `Graphics.DrawProcedural` doesn't participate in Unity's frustum or LOD culling; the mod must supply its own.
- **Buffer scaling.** A StructuredBuffer per (sprite, rig) grows with active actor count; unbounded allocation will OOM. Need a high-water cap + LRU eviction like `RigCache`.

### Alternatives Considered

| Alternative | Pros | Cons | Why not chosen |
|---|---|---|---|
| Proxy mesh per actor | Each actor has exclusive target; no contention. | 10× memory cost (StructuredBuffer per actor instead of per rig). Scales with actor count, defeats perf budget. Per-actor compute dispatch overhead. | Violates Phase 10 target. |
| CPU fallback (current) | Simple, no GPU work. | Slow (200–500 µs per actor), kills 1000-actor goal. Defeats entire Phase 6 GPU investment. | Unacceptable for Phase 10. |
| DrawMeshInstanced with per-actor matrices | Keep proxy mesh, pass actor matrices to vertex shader. | Proxy mesh still single-target; contention persists. Instance data doesn't help if all instances read the same verts. | Doesn't solve the core problem. |
| DrawProceduralIndirect + StructuredBuffer per (sprite, rig) | GPU-resident; zero contention; one StructuredBuffer shared (sliced per instance); indirect args buffer handles dispatch. | Requires custom shader (Phase 5b coupling). No built-in culling (need FrustumCuller integration). Buffer scaling + eviction overhead. But these are solvable. | **Chosen.** Solves multi-actor uniqueness + perf. |

## Decision

Replace the proxy-mesh write path with `Graphics.DrawProceduralIndirect`. Allocate one `ComputeBuffer` (StructuredBuffer) per (sprite, rig) pair, sized `vertexCount × maxInstancesPerRig` floats (storing float3 positions for all vertices of all actors). The vertex shader reads positions via:

```hlsl
float3 instanceSliceOffset = vertexCount * instanceID;
float3 skinnedPos = positionBuffer[instanceSliceOffset + vertexIndex];
```

`VoxelSkin.compute` kernel is rewritten to take `instanceID` as a parameter and writes actor N's deformed verts to offset `vertexCount × instanceID`. An indirect-args buffer (`uint[] {indexCount, instanceCount, 0, 0, 0}`) replaces the per-frame `SetVertices` call; the GPU reads these args to dispatch the draw.

Per-(sprite, rig) StructuredBuffer lifetime follows `RigCache.Evict` semantics: high-water cap (e.g., 8 actors max per rig, configurable), LRU eviction when exceeded, with a hook in `RigDriver.DispatchSkin` to trigger cleanup.

---

## Consequences

### Positive

- **Multi-actor correctness.** Each instance reads its own slice of the per-rig StructuredBuffer. No vertex contention; all N actors in a batch render their correct deformed geometry.
- **Zero CPU readback.** Vertices live entirely on the GPU. No `ComputeBuffer.GetData` + `Mesh.SetVertices` per frame.
- **Shared rig efficiency.** One StructuredBuffer covers all actors with the same base mesh + rig; memory and compute cost scales with vertex count, not actor count.
- **Phase 10 perf goal achievable.** Removes the primary bottleneck for 1000-actor batches.

### Negative

- **Custom shader required.** The placeholder material cannot sample a StructuredBuffer. Phase 5b (Unity 2022.3 shader bake) must land first. Lit skinning features gate on this.
- **No built-in culling.** `Graphics.DrawProceduralIndirect` bypasses Unity's frustum/occlusion logic. `FrustumCuller` (Phase 10) must be hooked to skip draws outside the viewport.
- **Buffer scaling + eviction.** Each (sprite, rig) uses `vertexCount × maxInstancesPerRig × 12` bytes. At 1000 actors across ~50 unique rigs, with 200 verts each and max 8 instances, this is ~960 KB of position buffers — acceptable, but not unlimited. Exceeding the cap triggers LRU eviction, which must be tested for race conditions with in-flight compute dispatches.
- **API complexity.** RigDriver must track per-rig compute buffer lifetimes, rebuild indirect-args buffers when the actor set changes, and integrate eviction logic.

### Neutral

- **Actor tint per-instance.** A parallel `ComputeBuffer<uint>` can store per-actor RGBA tints, passed to the vertex shader the same way. This complicates the architecture slightly but is not load-bearing for Phase 6 — can defer to Phase 6 Step 10 if needed.

---

## Implementation Steps

1. **Allocate the per-rig StructuredBuffer.** In `RigDriver.DispatchSkin`, replace the proxy-mesh alloc with: `ComputeBuffer positionBuffer = new ComputeBuffer(vertexCount * kMaxInstancesPerRig, sizeof(float) * 3, ComputeBufferType.Default)`. Cache by (sprite, rig) key in `RigCache` or a local dictionary.

2. **Build the indirect-args buffer.** Create a separate `ComputeBuffer<uint>` sized 5, initialized with `{meshIndexCount, activeInstanceCount, 0, 0, 0}`. Update `activeInstanceCount` as actors are added/removed each frame; write via `ComputeBuffer.SetData`.

3. **Rewrite VoxelSkin.compute kernel signature.** Current: `[numthreads(8,8,8)] void CSMain(uint3 id)`. New: `[numthreads(8,8,8)] void CSMain(uint3 id, uint instanceID : SV_InstanceID)` or pass `instanceID` as a constant buffer. Read actor matrices and bone data from per-instance input buffers (already exist from Step 7). Output to `positionBuffer[vertexCount * instanceID + vertexIndex]`.

4. **Write the VoxelSkinned.shader vertex shader.** Bind the per-rig `StructuredBuffer<float3> positionBuffer`. In `vert()`, read `float3 skinnedPos = positionBuffer[vertexCount * instanceID + input.vertexID]`. Apply any post-skin effects (e.g., vertex color blending). Output to `positionWS`.

5. **Gate the dispatch.** In `RigDriver.DispatchSkin`, replace `if (!_gpuOK) { CPU fallback; return; }` with `if (!_gpuOK || !FrustumCuller.IsVisible(bounds)) { skip; }`. After all actor skinning is done, call `Graphics.DrawProceduralIndirect(material, bounds, MeshTopology.Triangles, argsBuffer, ...)` once per (sprite, rig) pair.

6. **Integrate FrustumCuller.** Phase 10's `FrustumCuller` is already in the codebase. Before calling `DispatchSkin`, check if the bounding box of this (sprite, rig) + all its actors is visible. If not, skip the compute dispatch and the draw.

7. **Implement per-rig buffer eviction.** Track active actor count per (sprite, rig). When a new actor is added and the count exceeds `kMaxInstancesPerRig`, evict the least-recently-used (sprite, rig) pair: release its StructuredBuffer, clear its cache entry, and mark for rebuild. On next frame, the buffer is reallocated on demand.

8. **Test the indirect-args buffer lifecycle.** Build a unit test that spawns/despawns actors in a multi-actor batch and verifies that the indirect-args `activeInstanceCount` matches the live actor count. Verify no off-by-one errors in draw calls.

9. **Remove the `_gpuOK = false` gate.** In `RigDriver.cs` line 90, change to `_gpuOK = CanDispatchGPU()`, which probes once at init time for compute + indirect + StructuredBuffer support (as before).

10. **Validate compute-to-shader binding.** Ensure the material's shader property IDs match the kernel's buffer bindings. Write a small integration test that submits a single actor, reads back one frame, and checks that verts are deformed (not in bind pose).

11. **Document buffer sizing heuristic.** Add a comment in `RigCache.cs` or `RigDriver.cs` explaining the `kMaxInstancesPerRig` choice: e.g., "Tuned for Phase 10 target: 1000 actors ÷ ~50 unique rigs ≈ 20 actors per rig on average; cap at 8 to leave headroom for popular rigs like humanoids."

12. **Add telemetry.** Log per-rig buffer allocations and evictions to help diagnose memory pressure. Include frame counters for eviction frequency (if > 2/sec, the cap is too low).

---

## Implementation Notes

### Files touched

- `WorldSphereMod/Code/Rig/RigDriver.cs` — Remove `_gpuOK = false` gate; rewrite `DispatchSkin` to allocate and manage per-rig StructuredBuffer; wire `FrustumCuller` integration; add eviction logic.
- `WorldSphereMod/Code/Rig/RigCache.cs` — Extend to store per-rig `ComputeBuffer` lifetimes and eviction metadata.
- `WorldSphereMod/Resources/Shaders/VoxelSkin.compute` — Rewrite kernel to accept `instanceID` and write to per-instance buffer offset.
- `WorldSphereMod/Resources/Shaders/VoxelSkinned.shader` (new) — Vertex shader that reads from per-rig StructuredBuffer indexed by instanceID. Must pair with Phase 5b (bake in Unity 2022.3).

### Settings / feature flags

No new `SavedSettings` flags. This is an implementation detail of Phase 6 Step 6 (the GPU path). The feature ships gated by the existing Phase 6 flag.

### Roll-out

1. Implement and test locally in the Phase 6 branch.
2. Once Phase 5b lands (shader bake), re-enable via a test flag to verify multi-actor correctness in-game.
3. Ship as Phase 6 Step 9 in PR #1, Phase 6 section.

---

## Open Questions

1. **Max instances per (sprite, rig): power-of-2 cap, dynamic resize, or pooled alloc?** Current proposal is a static cap (`kMaxInstancesPerRig = 8`), simple to reason about. Alternatives: start at 4, double on contention (complicates eviction); or pre-allocate a pool of 16 buffers and hand them out LRU (adds bookkeeping but no per-rig fragmentation). Recommend static cap for Phase 6, revisit in Phase 10 if perf data shows the cap is too tight.

2. **How to pass actor tint per-instance?** A parallel `ComputeBuffer<uint>` mirrors the position buffer and stores RGBA per actor. The vertex shader multiplies the base texture color by this tint. Defer to Phase 6 Step 10 if Phase 6 Step 9 lands without tint support.

3. **Eviction under in-flight dispatches.** If a (sprite, rig) is evicted while a compute dispatch or draw is in-flight, the GPU may read freed memory or crash. The fix: add a frame delay to eviction (mark evict candidate, wait 2 frames, then release) or use a semaphore to block eviction until all in-flight commands complete. Recommend frame-delay approach for simplicity.

4. **Indirect-args buffer rebuild cost.** Currently re-written every frame if the actor set changes. For a stable frame (same N actors), the write is a single `ComputeBuffer.SetData(activeInstanceCount)` — negligible. For churn (actors spawning/dying every frame), N calls per frame could add up. Benchmark and consider batching eviction to every Nth frame if this becomes a bottleneck.

---

## Consequences of Deferral

If this step is deferred beyond Phase 6:

- GPU skinning remains disabled (`_gpuOK = false`); Phase 10's perf target becomes unachievable.
- CPU fallback (bind-pose per actor) costs ~200–500 µs per actor. At 500 actors, this is 100–250 ms/frame, blowing the frame budget.
- The multi-actor batching infrastructure of Phase 10 is ready but unused.

---

## References

- **Phase 6 architecture:** `docs/phase6-architecture.md` § GPU compute path + Phase 6 Step 7 notes
- **Phase 10 architecture:** `docs/phase10-architecture.md` § perf budget + FrustumCuller
- **Code anchors:**
  - `WorldSphereMod/Code/Rig/RigDriver.cs` line 90 (`_gpuOK = false` gate)
  - `WorldSphereMod/Code/Rig/RigDriver.cs` method `DispatchSkin` (current proxy-mesh path)
  - `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs` (LRU pattern for cache eviction)
  - `WorldSphereMod/Code/Voxel/FrustumCuller.cs` (Phase 10 visibility check)
- **External references:**
  - [Unity Graphics.DrawProceduralIndirect](https://docs.unity3d.com/ScriptReference/Graphics.DrawProceduralIndirect.html) — Draws without a mesh; reads args from a ComputeBuffer.
  - [Unity ComputeBuffer](https://docs.unity3d.com/ScriptReference/ComputeBuffer.html) — GPU-resident structured buffer for compute shaders and shaders.
  - [HLSL SV_InstanceID](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/sv-instanceid) — Built-in semantic for instance ID in vertex shader.

---

> **Effort estimate:** 2–3 days of focused work. ~400 LOC across `RigDriver.cs` (200 LOC rewrite + eviction), `VoxelSkin.compute` (100 LOC kernel rewrite), and `VoxelSkinned.shader` (100 LOC new shader).
>
> **Risk:** High coupling to Phase 5b (shader bake). If the bake is delayed, this step cannot ship.
> **Acceptance criteria:** Multi-actor batches (same sprite + rig, different transforms) render without vertex contention. Frame time for 500 skinned actors on Phase 10-scale hardware is < 5 ms (current CPU fallback: ~100+ ms).
