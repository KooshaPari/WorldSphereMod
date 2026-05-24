**Actor vs Building Render Diff**

The single line most likely making actors invisible is the actor-only gate in [`WorldSphereMod/Code/Voxel/VoxelRender.cs`](WorldSphereMod/Code/Voxel/VoxelRender.cs): `if (!rd.has_normal_render[i]) continue;` at line 309.

Why this stands out:
- `BuildingProcRender.EmitMeshes` has no equivalent early exit before voxel submission.
- Buildings go on to `VoxelRender.Submit(...)` with the same material/batcher path and do render.
- The actor path can therefore skip *all* voxel work before it ever reaches `VoxelMeshCache.Get(sp)` or `Submit(...)`.

Proposed fix:
- Remove that gate, or replace it with a narrower sprite-null check later in the loop.
- Keep the sprite-hide suppression disabled until visibility is confirmed, since the current diagnostic comments already leave `rd.has_normal_render[i]` untouched.

Most likely patch:
```csharp
// if (!rd.has_normal_render[i]) continue;
```

If actors are still supposed to preserve the sprite fallback, let voxel submission happen first and only suppress the normal render after a successful `Submit(...)`.
