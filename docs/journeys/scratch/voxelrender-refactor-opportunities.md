# VoxelRender refactor opportunities

Scope: `WorldSphereMod/Code/Voxel/VoxelRender.cs` and `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs`

## What repeats

Three blocks share the same skeleton:

- cull-lift guard on `rd.positions[i]`
- LOD tier resolution from `LodSelector.Select(...)`
- impostor branch with `ImpostorBillboard.GetOrCreate(...)`
- voxel/mesh branch with TRS construction and submit/hide semantics

The duplication is clearest in:

- `ActorVoxelEmit.EmitVoxels` (`VoxelRender.cs:289-400`)
- `BuildingVoxelEmit.EmitVoxels` (`VoxelRender.cs:445-526`)
- `BuildingProcRender.ProcMeshEmit.EmitMeshes` (`BuildingProcRender.cs:19-137`)

## Ranked refactors

### 1) Extract a shared `ResolveLiftedCullAndTier(...)` helper

**Benefit:** highest. **Risk:** low.

Shape:

- input: `rd.positions[i]`, instance id, radius
- output: lifted cull position plus resolved `LodTier`

This removes the repeated lift/cull/tier sequence in all three methods and centralizes the `To3DTileHeight(false)` rule. It also makes the seam/cull behavior easier to audit later.

Why low risk:

- no branching semantics change
- caller still owns the per-type radius, logging, and instance hash source

### 2) Extract a shared impostor submit helper

**Benefit:** high. **Risk:** medium.

Shape:

- input: sprite, `rd.positions[i]`, scale, flip flag, color, and a callback for “hide source render”
- output: `bool submitted`

This collapses the duplicate impostor path in actor/building/procgen emitters:

- fetch sprite
- create impostor mesh/material
- lift position
- mirror scale if needed
- rotate to camera
- submit through `MeshInstanceBatcher`

Why medium risk:

- actor uses `rd.has_normal_render[i] = false`
- building/procgen use `rd.scales[i] = Vector3.zero`
- procgen has profiler timing wrapped around the impostor path

The helper should not own those side effects; it should return success and let the caller decide how to hide the original render.

### 3) Extract a voxel-body submit helper for building + procgen voxel branches

**Benefit:** medium. **Risk:** medium-high.

Shape:

- input: mesh, lifted pos, rotation, scale, color, and a hide callback
- output: `bool submitted`

This would unify:

- `BuildingVoxelEmit` voxel branch (`VoxelRender.cs:506-526`)
- `BuildingProcRender` voxel branch (`BuildingProcRender.cs:123-137`)

Common work:

- lift `pos`
- mirror `scl.x`
- set `scl.z = scl.x` for voxel bodies
- build `Matrix4x4.TRS`
- submit mesh
- hide the source sprite/render data on success

Why higher risk:

- procgen has a separate crossed-quad branch that should stay outside the helper
- `BuildingVoxelEmit` uses `Vector3.zero`, while actor code uses a different hide contract
- a too-generic helper could turn into an abstraction sink

## Recommendation

Do 1 first, then 2. Only do 3 if the building paths keep growing; otherwise the two current building emitters may be clearer left separate with only shared lift/tier and impostor helpers.
