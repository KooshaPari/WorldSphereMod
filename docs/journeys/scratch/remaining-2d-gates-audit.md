# Remaining 2D Gates Audit

These are the remaining early exits in `ActorVoxelEmit.EmitVoxels` that can stop an actor from reaching the voxel mesh path after the `has_normal_render` gate was removed.

1. `Constants.PerpActors.ContainsKey(a.asset.id)` at [`WorldSphereMod/Code/Voxel/VoxelRender.cs:309-318`](../../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L309-L318)
   - `PerpActors` is empty in source (`Constants.cs:26-31`) and is only populated at runtime through `WorldSphereModAPI.MakeActorPerp` (`WorldSphereMod/Code/WorldSphereAPI.cs:20-25`).
   - Symptom: intentional sprite-only / ground-aligned billboard behavior. These actors are excluded from voxelization by design, so they can still look 2D.

2. `Sprite sp = rd.main_sprites[i]; if (sp == null) continue;` at [`WorldSphereMod/Code/Voxel/VoxelRender.cs:358-359`](../../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L358-L359)
   - Symptom: no voxel submit happens because there is no sprite to voxelize. This is usually a data/path issue upstream, and the actor may disappear or stay on whatever non-voxel render path still exists.

3. `if (Core.savedSettings.SkeletalAnimation && tier != LodTier.Impostor)` + rig hit at [`WorldSphereMod/Code/Voxel/VoxelRender.cs:332-355`](../../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L332-L355)
   - When `ResolveRigType(a.asset.id)` returns a rig, the code submits through `RigDriver.SubmitSkinnedActor(...)` and `continue`s before `VoxelMeshCache.Get(sp)`.
   - Symptom: rigged actors take the separate skinned-actor path instead of voxelization. They can remain sprite/rig-driven rather than becoming voxel meshes.

4. `if (tier == LodTier.Impostor)` at [`WorldSphereMod/Code/Voxel/VoxelRender.cs:361-387`](../../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L361-L387)
   - The actor submits an impostor billboard mesh and `continue`s, bypassing the voxel mesh path.
   - Symptom: flat impostor rendering. This is still a 2D-ish visual, just via the impostor system instead of the original sprite quad.

Net: the four remaining skip/alternate-submit gates are `PerpActors`, missing `main_sprites`, skeletal rig routing, and impostor routing. Any actor still showing as 2D after the `has_normal_render` removal is most likely hitting one of the last three, or intentionally opted out via `PerpActors`.
