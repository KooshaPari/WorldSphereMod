# Wave 14 — 12 codex-spark actor invisibility findings

**Dispatched:** 2026-05-19, **Model:** gpt-5.3-codex-spark, **Cycle:** 12 agents in parallel.

## Hypotheses

### H10 — empty-mesh fallback from non-readable sprite atlas textures

`SpriteVoxelizer.Build()` could return a 0-vertex mesh for atlas sprites because
`sprite.texture.isReadable` is usually false.

### H06 — draw calls issued to the wrong camera in batch flush

`MeshInstanceBatcher.Flush()` could render through `CameraManager.MainCamera`
instead of the same null-camera path used by `SanityTestCube`.

## Real bugs (newly surfaced)

### H10 (resolved)

`SpriteVoxelizer.Build()` returned `Mesh.CreateEmpty()` when `sprite.texture.isReadable`
was false (Unity default for atlas textures). The empty mesh was cached in
`VoxelMeshCache.Get()`, and `VoxelRender.EmitVoxels()` guarded only `m == null`.
It still submitted (and incremented counters) via all four branches, but no pixels
were emitted because `vertexCount == 0`.

**Fix in commit `1a9ba62`:**

- Read atlas textures through `RenderTexture` + `Graphics.Blit` + `Texture2D.ReadPixels`
  so `Build()` can return readable data.
- Prevent caching of zero-vertex meshes in `VoxelMeshCache.Get()`.
- Add `vertexCount > 0` checks in all four `EmitVoxels` Submit branches.

### H06 (resolved)

`MeshInstanceBatcher.Flush()` passed `CameraManager.MainCamera` into both
`Graphics.DrawMeshInstanced` and `Graphics.DrawMesh`. If `MainCamera` was
secondary, disabled, or RT-bound, draws were not visible in the player view even
if they existed.

**Fix in commit `6c7f4b4`:**

- Use `camera = null` in both draw code paths, matching the existing
  `SanityTestCube` behavior.

## Phase health verdicts

| Phase | Verdict | Notes |
|---|---|---|
| 1 | ✅ visible again | Phase 1 actor invisibility is fixed after both H10 and H06 are merged. ADR-0015 records the final root-cause chain and interaction. |

## Why both fixes were needed

- With only H10 fixed: meshes became readable but often rendered to the wrong camera.
- With only H06 fixed: meshes reached the right camera path but were empty.
- With both fixed: actors became visible.

## Linked

- ADR-0011 (Phase 1 visibility postmortem)
- ADR-0012 (Phase 2 procedural not rendering)
- ADR-0013 (Flush gate silently dropped)
- ADR-0014 (AutoTest persist + tile-dirty methodology)
- ADR-0015 (Actor invisibility final root causes)
