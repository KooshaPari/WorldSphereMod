# Phase 1 voxel-pipeline code review

Source: `feature-dev:code-reviewer` agent, 2026-05-17. Scope: `WorldSphereMod/Code/Voxel/*.cs` only. All five issues are gated behind `SavedSettings.VoxelEntities` (default `false`) — they only fire when the flag is toggled on for the smoke test.

---

## Critical

### 1. `MeshInstanceBatcher.Flush` uploads full 1023-element color array per draw
`WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:71-93`

```csharp
var cols = new Vector4[kBatch];                  // always 1023
bucket.Colors.CopyTo(offset, cols, 0, n);        // only first n filled
bucket.Block.SetVectorArray(_colorProp, cols);   // uploads all 1023
Graphics.DrawMeshInstanced(..., mats, n, bucket.Block, ...);
```

`DrawMeshInstanced` respects `n` for matrices, but `SetVectorArray` uploads the full array — partial final batches (always the case unless actor count is a multiple of 1023) get garbage color data for tail entries. **Visible result: actor tint fully lost for the final batch.**

**Fix:** copy into a `Vector4[n]` and upload that, or maintain a per-bucket scratch buffer sized to `n`.

---

## High

### 3. `VoxelMeshCache.Get` is unlocked + destroys meshes that may still be in the current frame's batch
`WorldSphereMod/Code/Voxel/VoxelMeshCache.cs:31-44`, `VoxelRender.cs:104`

Plain `Dictionary<>` access; `Evict()` calls `Object.Destroy(mesh)` synchronously inside `Get()`. If the evicted mesh was already queued in `MeshInstanceBatcher` for this frame, the GPU draw will reference a destroyed mesh.

**Fix:** lock around `_cache`, and defer mesh destruction to end-of-frame (drain queue → then destroy) rather than destroying inside `Evict`.

### 4. Voxel meshes inherit `rotations[i]` Z-lean → walking actors physically topple sideways
`WorldSphereMod/Code/Voxel/VoxelRender.cs:111`

```csharp
Vector3 rot = rd.rotations[i];
Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(rot), scl);
```

Upstream (`QuantumSprites.cs:472-493`) populates `rotations[i] = tActor.Get3DRot()`, which encodes Z-axis "lean" (sprite tilt for animation flavor). On a 2D quad it just tilts the sprite; on a 3D voxel body it rotates the whole mesh sideways. **Visible result: walking units lean over and topple as they move.**

**Fix:** apply only yaw — `Quaternion.Euler(0, rot.y, 0)` — for Phase 1. Lean returns in Phase 6 (skeletal) as a spine-bone tilt, not a whole-body rotation.

---

## Medium

### 5. `VoxelRender._material` leaks across world reloads
`WorldSphereMod/Code/Voxel/VoxelRender.cs:23-57`

Static `_material` and `_materialAttempted` aren't reset on world unload. After a reload Unity may have invalidated the `Material` but `EnsureMaterial` returns it anyway.

**Fix:** add `VoxelRender.Reset()` mirroring `MeshInstanceBatcher.Reset()`; wire to the same world-reload hook as `VoxelMeshCache.Clear()`.

---

## Deferred (Phase 1 benign, flag for later)

### 2. `mesh.UploadMeshData(true)` makes mesh permanently non-readable
`WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs:147`

Fine for Phase 1 draw-only. Future phases (greedy meshing, LOD generation, raycasting, mesh-collider baking) need read-back — will throw if this stays.

**Action:** none now. When Phase 5/6/10 need read-back, replace with deferred upload or remove the `markNoLongerReadable` flag.

---

## Smoke-test gate

Issues 1 and 4 will be **visibly broken** the moment `VoxelEntities=true`. They must land before flipping the flag for any in-game test, or the test will be a false-negative.

Issue 3 is a latent bug that probably won't fire on a 500-unit small-kingdom test but will start hitting on multi-thousand actor stress tests.

Issue 5 only matters across multiple world generations in one game session.
