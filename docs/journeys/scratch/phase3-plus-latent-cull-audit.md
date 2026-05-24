# Phase 3+ latent cull audit

**Date:** 2026-05-19
**Trigger:** mirror the earlier Phase 3 cull-lift audit across the remaining phase folders
**Scope:** `WorldSphereMod/Code/Foliage/`, `WorldSphereMod/Code/Water/`, `WorldSphereMod/Code/Lighting/`, `WorldSphereMod/Code/Rig/`, `WorldSphereMod/Code/Worldspace/`, `WorldSphereMod/Code/Fx/`, `WorldSphereMod/Code/LOD/`
**Method:** grep `render_data.positions`, `render_data.scales`, `To3DTileHeight`, and `FrustumCuller` across the scope above

## Result

No additional latent cull-lift bugs were found in the remaining phase folders.

I did not find any Postfix in the scoped folders that uses raw `render_data.positions[i]` in a `Matrix4x4.TRS(...)` call or `Bounds` construction without lifting first.

## Checked sites

- `WorldSphereMod/Code/Foliage/FoliageTileRender.cs:91` lifts `pos2` with `Tools.To3DTileHeight(...)` before `Matrix4x4.TRS(...)` at `:93`.
- `WorldSphereMod/Code/Foliage/WallTileRender.cs:61` lifts `pos2` with `Tools.To3DTileHeight(...)` before `Matrix4x4.TRS(...)` at `:63`.
- `WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs:106` lifts `a.current_position` with `Tools.To3DTileHeight(...)` for rig placement.
- `WorldSphereMod/Code/Worldspace/SelectionRing.cs:69` lifts `a.current_position` with `Tools.To3DTileHeight(...)` before ring placement.
- `WorldSphereMod/Code/Rig/RigDriver.cs:128` uses the caller-provided world `pos` in `Matrix4x4.TRS(...)`; this path is not sourcing `render_data.positions[i]`.
- `WorldSphereMod/Code/LOD/FrustumCuller.cs:24` builds a `Bounds` from `worldPos`; no scoped Postfix feeds it raw `render_data.positions[i]`.

## Notes

- `render_data` hits were confined to the earlier voxel/proc-gen audit surface; the remaining phase folders did not surface a raw `render_data.positions[i]` pattern.
- The remaining phase folders mostly contain lifecycle Postfixes or already-lifted placement code, so the Phase 3 cull-lift bug class does not recur there.
