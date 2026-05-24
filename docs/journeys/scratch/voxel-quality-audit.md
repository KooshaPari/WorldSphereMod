# Voxel mesh quality audit

Scope: `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs` and the local preview tool in `Tools/wsm3d-preview/src/`.

`SpriteVoxelizer.Build` already uses 3-axis greedy meshing over all 6 face directions, so actor quality is mostly driven by silhouette shape and color breaks, not by a missing axis pass. The build logs in `VoxelMeshCache` report `verts` and `tris` for each first-time sprite build (`WorldSphereMod/Code/Voxel/VoxelMeshCache.cs:258-261`).

## Sample voxelized entries

Sampled from the local preview path and a 10x16 walk-frame surrogate:

- `walk_0`-like 10x16 surrogate, opaque: `124 tris / 248 verts`
- same surrogate with 4 edge pixels at alpha 12: `112 tris / 224 verts`
- same surrogate with 4 edge pixels at alpha 20: `132 tris / 264 verts`
- in-repo `voxel_actor_sprite` fixture, depth 1: `162 tris / 324 verts`
- in-repo `humanoid_sprite` fixture, depth 2: `436 tris / 872 verts` per pose

## Findings

1. **Consistency across actor types: mostly yes.** Small humanoid-like sprites stay in the same broad band: the 10x16 surrogate is 224-264 verts, the larger actor fixture is 324 verts, and the humanoid preview pose is 872 verts. The ratio is stable enough that quality varies more with sprite footprint than with mesh failure.
2. **Failures / degenerate meshes: none found in the sampled set.** The code only returns empty/null on unreadable textures or null sprites, and the cache rejects zero-vertex meshes before storing them.
3. **Alpha threshold effect: stepwise and monotone.** `Build` treats `alpha > 16` as solid. In the surrogate, changing only four border pixels from alpha 12 to 20 moved the mesh from `112 tris` to `132 tris`. Pixels below threshold vanish entirely; pixels above threshold become voxels and can increase both silhouette area and face merges.
4. **Vertex reduction opportunities: limited, because the mesher is already axis-greedy.** The remaining wins are:
   - relax color equality on large flat body regions, or quantize to a smaller palette before greedy merge;
   - pre-collapse obviously flat fully-opaque frames into fewer slabs when a sprite is close to a solid rectangle;
   - for actor families, consider caching a coarser proxy mesh for distant LOD instead of squeezing more out of the base pass.

Bottom line: actor voxel quality is consistent, not fragile. The current blocker is not degenerate meshing; it is that alpha noise and color fragmentation directly translate into extra quads, so sprite cleanup and LOD proxying are higher ROI than another face-direction pass.
