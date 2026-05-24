# Ignore-generic render hoist analysis

**Date:** 2026-05-19  
**Scope:** `WorldSphereMod/Code/QuantumSprites.cs` around the `ActorManager.precalculateRenderDataParallel` patch, plus the downstream voxel consumer in `WorldSphereMod/Code/Voxel/VoxelRender.cs`.

## Question

`gameperf-01` suggested hoisting the renderability gate:

- `tActor.asset.ignore_generic_render`
- `Constants.PerpActors`

to before `updatePos()` / `Get3DRot()`.

## Trace

Upstream path in `QuantumSprites.cs:468-500`:

1. `tActor.updatePos()` populates the actor's transform state.
2. `tActor.Get3DRot()` reads that transform state and, for upright actors, calls `updateRotation()`.
3. The render pass then writes `render_data.positions`, `render_data.rotations`, `render_data.shadows`, and `render_data.shadow_position`.
4. `ActorVoxelEmit` in `VoxelRender.cs:289-318` consumes that render data later and already has its own `PerpActors` + `has_normal_render` gates.

## What `updatePos()` actually does

Decompiled `Actor.updatePos()` in the publicized WorldBox assembly shows it is not a pure getter:

- computes the actor's transform from `current_position`, `shake_offset`, `move_jump_offset`, and `position_height`
- writes `current_shadow_position`
- writes `cur_transform_position`
- returns `cur_transform_position`

`Get3DRot()` is also stateful for upright actors because it calls `updateRotation()`, which syncs `current_rotation` to `target_angle` before composing the camera-facing rotation.

## Safety call

Do **not** hoist the gate as a blanket early-`continue` before `updatePos()` / `Get3DRot()`.

Why:

- `current_shadow_position` would stay stale for ignored actors.
- `cur_transform_position` would stay stale for ignored actors.
- `render_data.shadow_position` and `render_data.shadow_scales` are still computed in the same method even when `tHasNormalRender` is false, so skipping the transform update breaks the shadow path.
- `Get3DRot()` can advance `current_rotation`, so skipping it also leaves rotation state stale for any later consumer.

There is no second local postfix on `ActorManager.precalculateRenderDataParallel` to absorb that loss. The risk is the base render-data pass itself and any downstream consumer of the stale transform/shadow state.

## Proposed patch

Keep the transform/rotation update in place. Hoist only the cheap renderability flag, and use it to skip the expensive sprite/material work, not the transform bookkeeping.

Pseudo-shape:

```csharp
bool hasNormalRender = !tActor.asset.ignore_generic_render;
bool isPerpActor = Constants.PerpActors.ContainsKey(tActor.asset.id);

Vector3 v = tActor.updatePos();
Vector3 rot = tActor.Get3DRot();

if (!hasNormalRender || isPerpActor)
{
    // still keep positions, rotations, and shadow writes valid
    // skip voxel-relevant sprite work only
}
```

`VoxelRender.ActorVoxelEmit` does not need a correctness change for this hoist; it already exits on `PerpActors` and `!rd.has_normal_render[i]`.

## Conclusion

The early hoist is **not correctness-safe** if it skips `updatePos()` / `Get3DRot()` entirely. That would break shadow/transform state for actors that still participate in the render-data pass. The safe optimization is to keep those calls and move only the expensive sprite-generation work behind the renderability gate.
