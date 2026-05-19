# ADR-0011 - Phase 1 visibility postmortem

## 2026-05-19 update: ROOT CAUSE FOUND

Phase 1 voxel actors were rendering, but at sub-pixel scale. `SpriteVoxelizer`
outputs sprite-local voxel meshes around `11x5x1`; the actor render path then
applied the vanilla sprite scale of `0.1`, producing about `1.1x0.5x0.1`
world-units at WorldBox's default camera zoom. That was too small to see in
normal gameplay captures.

Commit `97ca065` fixed the visible scale by adding
`SavedSettings.VoxelScaleMultiplier = 8.0f` and multiplying it into `scl` in
every voxel/procedural `Submit` branch. Commit `17760fe` added
`SanityTestCube`, and `docs/journeys/scratch/sanity-cube-163650.png`
confirmed the rendering path was visible in-game.

The earlier fixes were still real: `_Cull = 0` avoided winding/camera-side
loss, `scl.z = scl.x` gave the mesh real depth, position lift put draws on the
3D terrain plane, the `DrawMesh` fallback proved instancing was not the only
path, and `PhasePatchManager` made runtime toggles apply consistently. None
could unlock visibility alone because the final actor meshes remained
sub-pixel at the applied sprite scale.
