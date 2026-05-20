# Phase 3+ cull-lift audit (rd.positions[i] TRS path)

**Date:** 2026-05-19 (autonomous audit during agent dispatch)
**Trigger:** memory `project_wsm3d_2d_cull_bug.md` — "Audit pattern for Phase 3+ when touching those Postfixes"
**Method:** grep `\.positions\[i\]` across `WorldSphereMod/Code/` (10 hits)

## Background

`render_data.positions[i]` arrives with z=0 (raw 2D world coord). The
fix from wave-09 lifted cullPos via `To3DTileHeight(false)` BEFORE the
`FrustumCuller.IsVisible` test (3 sites — BuildingProcRender + VoxelRender).

What that fix did NOT do: lift the *downstream* TRS-translation `pos`
that comes from re-reading `rd.positions[i]` after the cull pass. The
mesh's actual world-space placement still uses raw z=0 in 3 sites.

## Findings (lift status per site)

### `BuildingProcRender.cs` (CrossedQuadEmit Postfix)
- **L42** `Vector3 cullPos` → ✅ lifted L43–46
- **L64** `Vector3 imPos` (impostor TRS) → ✅ lifted L67–70
- **L95** `Vector3 pos` (CrossedQuad / Single / procgen TRS) → ❌ **NO LIFT**
  - Used directly at L105 (`Matrix4x4.TRS(pos, ...)`) for foliage
  - Used directly at L123 (`Matrix4x4.TRS(pos, ...)`) for procgen building mesh

### `VoxelRender.cs` ActorVoxelEmit Postfix (L308–397)
- **L308** `cullPos` → ✅ lifted L309–312
- **L325** `skPos` (skeletal TRS) → ✅ lifted L330–333
- **L353** `imPos` (impostor TRS) → ✅ lifted L357–360
- **L379** `pos` (voxel actor TRS) → ❌ **NO LIFT**
  - Used at L390 `Matrix4x4.TRS(pos, ...)` for the voxel actor mesh
  - `LogFirstActorPos(posBeforeLift, pos, scl)` passes pos as both args — telltale that `posBeforeLift == pos` because no lift happened
  - Actors are visible today only because `VoxelScaleMultiplier=8.0` (memory `project_wsm3d_phase1_visible`) — visibility may collapse if scale is restored and z=0 actor sinks below terrain

### `VoxelRender.cs` BuildingVoxelEmit Postfix (L437–520)
- **L456** `cullPos` → ✅ lifted L457–460
- **L481** `imPos` (impostor TRS) → ✅ lifted L484–487
- **L505** `pos` (voxel building TRS) → ❌ **NO LIFT**
  - Used at L510 `Matrix4x4.TRS(pos, ...)`

## Recommended fix pattern

After each `Vector3 pos = rd.positions[i];`, add:

```csharp
if (pos.z < Constants.ZDisplacement * 0.5f)
{
    pos = pos.To3DTileHeight(false);
}
```

Or refactor to compute lifted pos ONCE at the top of the loop body and
reuse for both cull and TRS.

## Why this wasn't caught

The phase-1 visibility fix (`VoxelScaleMultiplier=8.0`) made actors
big enough that even ground-buried meshes peek above terrain — hiding
the missing lift. Same story for buildings: thick voxel shells visible
through terrain. The bug surfaces when scale is dialed back to realistic
values for the alpha.8 visual pass.

## Suggested commit grouping (when fixed)

1. `fix(phase-1): lift voxel-actor TRS pos in ActorVoxelEmit` (VoxelRender.cs L379)
2. `fix(phase-1): lift voxel-building TRS pos in BuildingVoxelEmit` (VoxelRender.cs L505)
3. `fix(phase-2): lift CrossedQuad/procgen TRS pos in BuildingProcRender` (BuildingProcRender.cs L95)

Three small targeted commits, each gated by the corresponding phase
SavedSettings flag in the enclosing class.
