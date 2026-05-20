# Replace RigDriver Research

## Recommendation

For Phase 6, the best fit is **not** a third-party rigging runtime. Keep `RigDriver` custom and purpose-built for procedurally generated voxel meshes, with **rigid skinning** as the default: one bone per voxel vertex, cached bone assignment, and per-frame matrix palette updates.

## Comparison

- **Unity 2D Animation package**
  - Best conceptual match for **bone weights, sprite skinning, and 2D IK**.
  - Weak fit for this project because it is built around authored sprite rigs and editor workflows, not runtime deformation of procedurally generated voxel meshes.
  - Useful as a reference for UX and data modeling, but not as the core runtime.

- **DragonBones**
  - Good for 2D skeletal animation and runtime playback.
  - Still assumes a hand-authored animation pipeline and weighted attachments.
  - Better than Mixamo for 2D-style runtime integration, but still not aligned with voxel-first, procedural mesh generation.

- **Esoteric Spine**
  - Strong runtime 2D skeletal system with solid tooling and deform timelines.
  - Like DragonBones, it is optimized for authored 2D assets and weighted deformation, not cube-based meshes built on the fly.
  - Overkill and mismatched to the Phase 6 constraint: deforming voxel meshes generated from sprites.

- **Mixamo**
  - Best for quick humanoid FBX rigging and baked animation retargeting.
  - Poor fit here because the content is not pre-rigged FBX, and the goal is not importing a finished skeletal asset pipeline.
  - Useful only as a reference for animation conventions, not implementation.

## Why Custom Wins

Phase 6 needs:

1. Procedural voxel meshes, not imported skinned meshes.
2. Runtime bone assignment from sprite-derived regions.
3. Lightweight deformation for pixel art, where rigid regions are acceptable.
4. A path that handles special cases like Crabzilla and Dragon without forcing an external asset pipeline.

That makes the current `RigDriver` direction the right one: a small internal skeletal system with cached segmentation, `AnimationFrameData` mapping, and a GPU/CPU skinning backend. External packages can inspire tooling, but they do not remove the need for the custom deformation layer.

## Bottom Line

**Best fit: keep `RigDriver` custom.**  
Use Unity 2D Animation concepts for authoring inspiration only. Do not replace Phase 6 with DragonBones, Spine, or Mixamo; they are built for authored rigs, while WSM3D needs runtime deformation of procedurally generated voxel meshes.
