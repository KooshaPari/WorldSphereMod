# Replace SpriteVoxelizer Research

WSM3D’s `SpriteVoxelizer` is already a purpose-built sprite-to-voxel path: about 500 LOC, atlas-aware reads, alpha thresholding, pivot preservation, color-preserving greedy meshing, and cache/unload hooks. The question is whether any off-the-shelf voxel workflow fits better.

## Options

- **Unity VoxelImporter asset (commercial)**: strongest packaged product, but it is a pipeline/tooling asset, not a drop-in match for runtime sprite atlas voxelization. It is best when art is authored or imported as voxel content ahead of time. It also adds vendor lock-in and licensing cost.
- **MagicaVoxel format + Unity importers**: good for authored voxel art and static asset pipelines. It is a poor fit for WSM3D’s current need, because the source is existing 2D sprites, not `.vox` files. This would shift the problem to an offline conversion step and still leave maintenance around importer behavior.
- **GitHub pixel-to-voxel projects**: useful as reference implementations for flood fill, depth extrusion, or greedy meshing variants, but they are usually editor toys or one-off converters. Maintenance, API stability, and Unity integration quality vary too much to trust as a core runtime dependency.

## Recommendation

**Best fit: keep the hand-rolled `SpriteVoxelizer`.**  
For this repo, the current implementation is already closer to the real requirements than any replacement:

- consumes WorldBox sprites directly
- preserves sprite pivot and color data
- handles unreadable atlases with fallback logic
- plugs into the existing cache and unload lifecycle
- stays small enough to audit and tune in-place

If a replacement is still desired, the only plausible candidate is **VoxelImporter**, but only as an offline/import-time workflow for authored voxel assets. It is not a better fit for WSM3D’s runtime sprite conversion path.

## Decision

Do not replace `SpriteVoxelizer` with a third-party voxel pipeline. Use external projects only as algorithm references, and keep the current custom mesher as the production path.
