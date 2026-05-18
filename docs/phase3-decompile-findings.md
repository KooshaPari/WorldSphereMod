# Phase 3 decompile findings — scope correction

Investigation by `general-purpose` decompile agent, 2026-05-17.
Result invalidates a core assumption of `docs/phase3-architecture.md`.

---

## Key finding

**Trees, bushes, rocks in WorldBox are NOT top tiles.** They are
`BuildingAsset` instances drawn through the already-patched
`BuildingManager.precalculateRenderDataParallel` path (see
`BuildingLibrary.cs` lines 188+ — `tree_green_1`, `corrupted_tree`,
`palm_tree`, etc).

WorldBox's `TopTileType` covers only **surface overlays**: grass,
savanna, biomass, snow_sand, frozen, road, field, fuse, tnt, landmine,
water_bomb, walls.

## Render paths in WorldBox

### Surface overlay path (NOT QuantumSpriteLibrary)
- `WorldTilemap.redrawTiles(bool pForceAll)` — entry, called from `MapBox`.
- `WorldTilemap.checkZoneToRender(TileZone)` — iterates dirty visible zones.
- `WorldTilemap.renderTile(WorldTile pTile)` — per-tile dispatcher.
- `TilemapExtended.addToQueueToRedraw(...)` — appends to per-layer batch.
- `TilemapExtended.redraw()` — flushes via `Tilemap.SetTiles(positions, tiles)`.

**Hybrid collect-then-flush.** Diff-based: `pTile.current_rendered_tile_graphics` skips unchanged tiles. Uses Unity's built-in `Tilemap` system.

### Wall sub-path (single QuantumSpriteLibrary exception)
- `QuantumSpriteLibrary.drawWalls(QuantumSpriteAsset)` line 2122
- `QuantumSpriteLibrary.drawWallType(TopTileType, QuantumSpriteAsset, bool, Material)` line 2205
- Per-wall-type iteration over `pTileTypeAsset.getCurrentTiles()`, emits `QuantumSprite` per tile.

### `drawTopTiles` does NOT exist
The full enumeration of `QuantumSpriteLibrary` was decompiled. No method by that name. The architecture doc's primary patch target is invalid.

## Phase 3 scope revision

The original `docs/phase3-architecture.md` planned "crossed-quad foliage / clouds / decorations." Three sub-features, three different paths:

1. **Crossed-quad foliage for trees/bushes/rocks.**
   These are **buildings**, not top tiles. Path: extend the existing
   `BuildingProcRender` Postfix (or add a sibling) to detect tree/bush
   assets (`asset.id.StartsWith("tree_")` or check a tag/material
   field) and route them to `CrossedQuadMesher` instead of
   `BuildingMeshGen`. **Reuses everything already shipped in Phase 2.**

2. **Surface overlay 3D (grass/biomass/walls).**
   Path: Prefix on `WorldTilemap.renderTile` (or `redrawTiles`) to
   skip the Unity Tilemap pipe when `IsWorld3D && CrossedQuadFoliage`,
   and emit per-tile crossed-quad meshes for the overlay sprite. Diff
   tracking via `pTile.current_rendered_tile_graphics` already in
   place — keep using it.

3. **Cloud refactor.**
   Unchanged. `fx_cloud` `EffectData` path still applies.

## Architecture doc impact

The current `docs/phase3-architecture.md`:
- Section 3 (TopTile Integration Sketch) needs full rewrite — patch target is wrong, sub-path 1 doesn't apply, sub-path 2 needs to target `WorldTilemap.renderTile`.
- Section 6 (Decision Tree Per Top Tile) — half the cases are "trees/bushes" which actually live under `BuildingManager`; reframe under building-asset filtering.
- Section 8 (Build Sequence) — commit 4 ("Postfix on QuantumSpriteLibrary.drawTopTiles") is invalid as written.

The rest (CrossedQuadMesher, WindSwayDriver, mesh cache, shader contract, decision tree mechanics, API additions) is reusable.

## Recommended next move

Rather than implement Phase 3 against the broken doc, **redesign Phase 3 with the corrected understanding** before any code is written. Two-pronged scope:

- **Phase 3a (trees as buildings).** Extends Phase 2 procgen with a "shape selector": `BuildingRules.Shape` enum with `Procgen` (default, current behavior) vs `CrossedQuad`. The `BuildingProcRender` Postfix routes to `BuildingMeshGen.Generate` or a new `FoliageMesher.Build` depending on the resolved rule. Trees auto-detected by asset id prefix or tag.
- **Phase 3b (surface overlays).** `WorldTilemap.renderTile` Prefix that skips the Tilemap path for `IsWorld3D` and emits crossed-quad meshes per overlay tile. Walls handled via a separate transpile on `QuantumSpriteLibrary.drawWallType`.

The `WindSwayDriver` + `FoliageWind.shader` + `CrossedQuadMesher` modules are unchanged; only the integration shim and the decision tree change.

## Decompile artifacts kept

- `C:\Users\koosh\AppData\Local\Temp\wb_decomp\toptile\TopTileLibrary.decompiled.cs`
- `C:\Users\koosh\AppData\Local\Temp\wb_decomp\toptile\TopTileType.decompiled.cs`
- `C:\Users\koosh\AppData\Local\Temp\wb_decomp\toptile\TileTypeBase.decompiled.cs`
- `C:\Users\koosh\AppData\Local\Temp\wb_decomp\toptile\WorldTilemap.decompiled.cs`
- `C:\Users\koosh\AppData\Local\Temp\wb_decomp\toptile\TilemapExtended.decompiled.cs`
- `C:\Users\koosh\AppData\Local\Temp\wb_decomp\toptile\WorldTile.decompiled.cs`
- `C:\Users\koosh\AppData\Local\Temp\wb_decomp\toptile\BuildingLibrary.decompiled.cs`
- `C:\Users\koosh\AppData\Local\Temp\wb_decomp\toptile\QuantumSpriteLibrary.decompiled.cs`
