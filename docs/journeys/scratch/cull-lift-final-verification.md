# Cull-Lift Final Verification

Checked `WorldSphereMod/Code/` for the cull-lift bug pattern described by `project_wsm3d_2d_cull_bug`, `phase3-cull-lift-audit`, and `phase3-plus-latent-cull-audit`.

## Result

No remaining sites were found that pass raw `rd.positions[i]` directly into a `Matrix4x4.TRS(...)` without first applying the lift guard.

## Evidence

The only current `rd.positions[i]` reads in `WorldSphereMod/Code/` are in:

- `WorldSphereMod/Code/Voxel/VoxelRender.cs`
- `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs`

In both files, the TRS inputs are lifted before matrix construction:

- `cullPos` is only used for visibility/lod selection.
- `imPos` and `pos` are guarded with `To3DTileHeight(false)` before `Matrix4x4.TRS(...)`.

The earlier fixes in `dbec1ea`, `1a54da4`, and `974cd81` appear to have closed the known raw-pos TRS sites.

## Conclusion

No additional lift-guard delta is needed at this time.
