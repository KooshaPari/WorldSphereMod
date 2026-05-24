# ADR-0014 — AutoTest cycle artifacts: persisted toggles + tile-dirty methodology

## Status

Resolved (commit `9de404d`, 2026-05-19).

## Context

The `AutoTestDriver` cycle is the autonomous-mode measurement harness — load
latest save, then `SetPhase(true) → wait 3s → log peak → SetPhase(false)` for
each phase flag. After landing the Flush-gate fix (ADR-0013) and the per-phase
peak-drawCalls window, the full 10-row telemetry surfaced two more artifacts
unrelated to whether the phases themselves work.

### Bug 1 — SaveSettings persisted test toggles to disk

`SetPhase` called `Core.SaveSettings()` after every flag flip. The cycle ends
with every phase flag at `false`. That state landed in
`mods_config/WorldSphereMod.json` — including default-on flags like
`CrossedQuadFoliage` and `MeshWater`. Subsequent boots started with everything
off, breaking the user's expectation that "default-on" means "on after I
install the mod."

Worse, this masked Phase 3's "is it actually rendering when its flag is on"
question: when we re-launched with `AutoTest=true` to remeasure, the default-on
flags were already false from the prior cycle's persisted exit state.

### Bug 2 — Tile-driven phases need an external dirty trigger

`FoliageTileRender.Prefix` patches `WorldTilemap.renderTile(pTile)`. Vanilla
WorldBox only calls `renderTile` when a tile's `last_rendered_tile_type` /
`current_rendered_tile_graphics` diff says the tile is dirty (grass growth,
biome change, damage, etc.). A settled world during AutoTest's 3s measurement
window doesn't dirty tiles — so the Postfix never fires, the Submit never
runs, and `peakDrawCalls` reads zero.

The Phase 3 telemetry "peak=0" therefore did NOT mean foliage is broken — it
meant the AutoTest methodology can't see tile-driven phases.

## Fix

```csharp
static void SetPhase(FieldInfo field, string flagName, bool value)
{
    field.SetValue(Core.savedSettings, value);
    // Do NOT persist AutoTest's mutations to disk — they leave the
    // user's default-on flags toggled OFF after every cycle.
    // Core.SaveSettings();
    Core.ApplyPhaseToggle(flagName, value);
}
```

And in the cycle loop, after `SetPhase(true)` and a one-frame settle:

```csharp
ForceTilemapRefresh();
yield return null;
// ... then the 180-frame peak window
```

`ForceTilemapRefresh` uses reflection to call any of `rerenderEverything`,
`refreshAll`, or `clearAndRedraw` on the active `WorldTilemap` instance —
whichever exists in the current WorldBox build. Swallows missing-method
errors gracefully (the log warning tells us when to add another fallback
name).

## Consequences

1. The user's default-on flags survive AutoTest cycles — `CrossedQuadFoliage`
   stays true unless the user themselves toggles it.
2. Phase 3, 3b, and any other tile-driven phase can now be measured. Their
   peak rows reflect the actual Submit volume their Postfix produces when
   given fresh tiles to process.
3. Future phases that hook tile-driven events (terrain decals, walls,
   overlays) inherit the refresh-before-measure behavior automatically.

## Followup

- If `ForceTilemapRefresh` logs "failed" on a WorldBox version that doesn't
  expose any of the three method names, add a chunk-iteration fallback that
  manually dirties every tile via the public WorldTile API.
- Consider exposing a wsm3d.ps1 subcommand (`wsm3d telemetry`) that triggers
  an AutoTest cycle, waits, and tails the per-phase rows for human reading.

## Linked

- ADR-0011 (Phase 1 visibility postmortem)
- ADR-0012 (Phase 2 procedural diagnosis methodology)
- ADR-0013 (Flush-gate silent-drop bug)
