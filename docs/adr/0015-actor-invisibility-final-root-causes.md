# ADR-0015 - Actor invisibility final root causes

## Status

Resolved (v2.0.0-alpha.7, 2026-05-19, commits `1a9ba62`, `6c7f4b4`).

## Context

After `ADR-0011` fixed sub-pixel scale (`VoxelScaleMultiplier=8.0f`), actors were still intermittently invisible. Two independent production bugs were found in the voxel actor render path during Wave-14, and both had to be fixed together to restore visibility.

## H10 — unreadable sprite atlas => empty mesh cached and submitted

`SpriteVoxelizer.Build()` returned `Mesh.CreateEmpty()` when `sprite.texture.isReadable == false`, the default for atlas textures in Unity. `VoxelMeshCache.Get()` cached this 0-vertex mesh by sprite key. `VoxelRender.EmitVoxels()` only guarded `m == null`, so cached empty meshes still flowed through all Submit paths and incremented telemetry counters, but produced no visible geometry.

Fixes in commit `1a9ba62`:

- `SpriteVoxelizer.Build()` now makes atlas textures readable via `RenderTexture` + `Graphics.Blit` + `Texture2D.ReadPixels` fallback.
- `VoxelMeshCache.Get()` no longer caches meshes where `vertexCount == 0`.
- `VoxelRender.EmitVoxels()` now also guards `vertexCount > 0` in all four `Submit(...)` branches.

## H06 — wrong draw camera in batch flush

`MeshInstanceBatcher.Flush()` passed `CameraManager.MainCamera` into `Graphics.DrawMeshInstanced` and `Graphics.DrawMesh`. Only `SanityTestCube` used `camera = null` and remained reliable. When `MainCamera` was secondary/disabled/RT-bound, actor draws landed on a non-visible target.

Fix in commit `6c7f4b4`:

- `MeshInstanceBatcher.Flush()` now passes `camera = null` in both draw code paths, matching `SanityTestCube` behavior.

## Why both fixes were required

- With only H10 fixed, geometry existed but often rendered to a camera the user could not see.
- With only H06 fixed, renders reached the visible camera but were still empty meshes.
- Only both fixes together made actor meshes visibly render on-screen.

## ADR scope and lineage

`ADR-0011` was a **real** and still-correct fix for Phase 1 sub-pixel scale, but predates and does not explain the later zero-visibility state. `ADR-0012`, `ADR-0013`, and `ADR-0014` were also real fixes for different issues (`Phase 2` cull, flush-gate, and AutoTest hygiene respectively), and remain valid.

## Consequences

- Phase 1 actor visibility is restored as a complete root-cause resolution (`v2.0.0-alpha.7`).
- Telemetry and rendering behavior now align: submit counters reflect real drawn geometry only.

## Linked

- ADR-0011 (Phase 1 visibility postmortem)
- ADR-0012 (Phase 2 procedural not rendering)
- ADR-0013 (Flush gate silently dropped)
- ADR-0014 (AutoTest persist + tile-dirty)
