# Skeletal position consistency audit

Scope: `WorldSphereMod/Code/Voxel/VoxelRender.cs`, `WorldSphereMod/Code/Rig/RigDriver.cs`, `WorldSphereMod/Code/Rig/HumanoidRig.cs`, `WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs`

## Conclusion

The Phase 6 skeletal branch does **not** repeat the cull-lift bug pattern found in earlier render paths.

- `VoxelRender.cs` lifts `skPos` before submit and passes the lifted value into `RigDriver.SubmitSkinnedActor` (`WorldSphereMod/Code/Voxel/VoxelRender.cs:325-336`).
- `RigDriver.SubmitSkinnedActor` never re-reads `a.current_position` or any raw actor position; it uses the `pos` argument directly when building the root transform with `Matrix4x4.TRS(pos, rot, scl)` (`WorldSphereMod/Code/Rig/RigDriver.cs:68-129`).
- `HumanoidRig.Evaluate` builds bone matrices from animation frame data only. The only positional effect is a local root rotation for prone actors, not a world-space translation (`WorldSphereMod/Code/Rig/HumanoidRig.cs:166-185`).
- The separate worldspace rig root path also lifts once at placement time: `rig.position = Tools.To3DTileHeight(a.current_position, kRigLift)` (`WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs:101-106`).

## Evidence

1. `VoxelRender.cs`
   - `skPos` is read from `rd.positions[i]`, optionally lifted with `To3DTileHeight(false)`, and then submitted (`:325-336`).
2. `RigDriver.cs`
   - The submit method accepts `pos` as an input parameter and uses it directly in `Matrix4x4.TRS(pos, rot, scl)` (`:68-129`).
   - The GPU skinning branch only flattens matrices from `HumanoidRig.Evaluate(fd, 1f)` into `_matricesScratch`; there is no world-position lookup in the bone path (`:132-175`).
3. `HumanoidRig.cs`
   - Bone evaluation is pose-local. It starts with identity matrices, optionally rotates the root for prone posture, and applies arm/leg swing offsets; no world translation is computed there (`:166-185`).
4. `WorldUIRenderer.cs`
   - The rig root placement uses `a.current_position` only through `Tools.To3DTileHeight`, so the transform is lifted exactly once at assignment time (`:101-106`).

## Audit note

The only raw-vs-lifted distinction in the skeletal submit path is the diagnostic snapshot (`skPosBeforeLift` vs `skPos`) in `VoxelRender.cs:326-334`. That is logging-only and does not affect the submitted root transform or the bone matrices.
