# Actor legs/accessory sprite locator gap

**Date:** 2026-05-20  
**Trigger:** request to extend `VoxelRender.ActorVoxelEmit` from `main_sprites` to the new multi-layer actor sprite pipeline  
**Method:** checked the current local decompile snapshot under `C:/Users/koosh/AppData/Local/Temp/wb_decomp/` and the repo's `docs/render-data-fields.md`

## What I checked

- `docs/render-data-fields.md`
- `C:/Users/koosh/AppData/Local/Temp/wb_decomp/ActorRenderData.decompiled.cs`
- `C:/Users/koosh/AppData/Local/Temp/wb_decomp/ActorManager.decompiled.cs`

## Current snapshot result

The current `ActorRenderData` snapshot still only exposes these sprite layers:

- `main_sprites`
- `main_sprite_colored`
- `shadow_sprites`
- `item_sprites`

I did **not** find any legs/accessory/held-weapon sprite array in the current publicized decompile. I also did not find any `legs_offset` field on `ActorRenderData`.

## Implication for `ActorVoxelEmit`

The repo can safely keep the existing main-sprite voxelization path, but I cannot wire the requested additional actor layers without a confirmed field name from a newer engine snapshot.

If a later decompile exposes the new arrays, the intended wiring is:

- `VoxelMeshCache.Get(layer_sprite)` per non-null layer sprite
- submit with the layer-specific TRS
- use `rd.positions[i] + rd.legs_offset[i]` for legs if present, otherwise reuse the actor TRS

## Evidence

Current decompile excerpt from `ActorRenderData.decompiled.cs` shows only:

- `positions`
- `scales`
- `rotations`
- `colors`
- `has_normal_render`
- `main_sprites`
- `main_sprite_colored`
- `materials`
- `flip_x_states`
- `shadows`
- `shadow_position`
- `shadow_scales`
- `shadow_sprites`
- `has_item`
- `item_scale`
- `item_pos`
- `item_sprites`

## Next step

Re-run the decompile against the updated WorldBox publicized assembly, then search for a new actor sprite-layer field name before touching `WorldSphereMod/Code/Voxel/VoxelRender.cs`.
