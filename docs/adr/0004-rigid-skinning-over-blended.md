# ADR 0004 — Rigid skinning over blended for voxel actors

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-05-17 |
| Deciders | KooshaPari |

## Context

Phase 6 introduces skeletal animation: voxelized actor meshes need to be deformed by a bone hierarchy driven by WorldBox's `AnimationFrameData`. Two skinning models:

1. **Blended skinning.** Each vertex has N bone influences with weights summing to 1.0. Standard for high-fidelity character meshes. Vertex memory ≥ 16 bytes (4 bone indices + 4 weights).
2. **Rigid skinning.** Each vertex belongs to exactly one bone. 1 byte per vertex for the bone index. Joint regions show discontinuity unless the topology is dense enough — which voxelized art naturally is, because each "limb segment" is its own group of independent texel cubes.

## Decision

Rigid skinning. One bone per vertex. Memory: `byte[] BoneIndices` parallel to the mesh's vertex array.

Reasoning specific to this codebase:

- **Pixel-art preserves region identity.** The `HumanoidRig.SegmentVoxels` heuristic classifies each *texel* (and thus each cube's vertices) by pixel-coordinate region — head row, arm column, leg column, torso. Sub-voxel bone overlap doesn't exist in the source art.
- **GPU buffer cost matters.** Phase 10's perf budget targets 1000 skinned actors. At ~200 verts each, blended skinning would need 200KB+ per actor of skin metadata vs ~50KB for rigid. The `VoxelSkin.compute` kernel becomes one `mul(M, v4)` instead of a 4-bone weighted sum.
- **No deformation budget either way.** Voxel pixel art is *meant* to read as low-poly. A visible 1-cube jump at the shoulder joint is fine; we're not chasing CG-realism.

## Consequences

- **Positive.** GPU buffer half the size. Compute kernel half the ALU. CPU bone-index assignment is a one-pass operation, deterministic per (sprite, rig) pair.
- **Negative.** Visible discontinuity at bone joints during extreme poses. Acceptable for the art style; flag if mocap-quality bend ever becomes a requirement.
- **Open.** If Phase 6 Step 9+ needs smoother bend on Crabzilla / Dragon hand-rigged paths, those specific rigs can opt into a blended subset by allocating a parallel weighted-vertex stream. The architecture doesn't preclude it; the default just isn't.

## References

- `docs/phase6-architecture.md` section 3 (Voxel Segmentation Heuristic)
- `WorldSphereMod/Code/Rig/HumanoidRig.cs` `SegmentVoxels`
- `WorldSphereMod/Resources/Shaders/VoxelSkin.compute`
