# MesinInstanceBatcher overflow audit

Scope: `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs`

## Findings

1. **Instance overflow is split, not truncated.** `Flush()` uses `while (offset < total)` and computes `n = Mathf.Min(kBatch, total - offset)`, so a bucket larger than 1023 is emitted in multiple `Graphics.DrawMeshInstanced` calls until `offset` reaches `total`. There is no early-return truncation path in the normal instancing branch. Relevant lines: `kBatch = 1023` at `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:67`, the loop and chunk sizing at `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:147-167`, and the final bucket clear at `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:184-185`.

2. **Edge cases behave as expected for instancing.**
   - `1023` submissions: one draw call, one chunk of size 1023.
   - `1024` submissions: two draw calls, chunk sizes 1023 + 1.
   - `5000` submissions: five draw calls, chunk sizes 1023 + 1023 + 1023 + 1023 + 908.
   - `0` submissions: the loop is skipped and nothing is drawn.
   These outcomes follow directly from `total = bucket.Matrices.Count` and the `while (offset < total)` loop at `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:137-167`.

3. **Scratch-buffer resize logic does not overflow on these cases.** `MatScratch` and `ColScratch` start at `kBatch` length and only grow when `Length < n`; since `n` is always capped at `kBatch`, the resize branch is never needed for 1023, 1024, 5000, or 0. Relevant lines: `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:40-43` and `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:150-155`.

4. **Null material is not a mid-batch recovery case.** `Submit()` drops null meshes/materials up front (`if (mesh == null || mat == null) return;`), so a true null material cannot enter a bucket through the public API. But `Flush()` itself does not guard `kv.Key.Material` before `Graphics.DrawMeshInstanced`; if a material reference were destroyed or otherwise invalid by the time of flush, the code would not split or skip that bucket gracefully. It would hit the `try/catch` only for `InvalidOperationException`, so a different exception type would escape. Relevant lines: `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:87-92` and `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:160-181`.

## Bottom line

The overflow path is correct for instancing: it drains the full bucket in 1023-instance chunks. The remaining risk is not truncation, but exception coverage around an invalid material handle during `Flush()`.
