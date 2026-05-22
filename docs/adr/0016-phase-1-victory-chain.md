# ADR-0016 — Alpha.8 to Alpha.9 victory chain

## Status

Accepted

## Context

`v2.0.0-alpha.8` to `v2.0.0-alpha.9` stabilized Phase 1 visibility by closing one material-surface regression set and hardening transpiler patching. This ADR records the exact cumulative chain so future waves can audit the same sequence without rediscovering prior state.

## Decision

Accept the alpha.9 cumulative chain as a single gated progression from alpha.8, including:

- 7 material-system fixes with explicit fix SHAs
- 12 transpiler guards with explicit patch SHAs
- one 7-step post-change verification checklist

## Cumulative material fixes (7)

1. **Sprites/Default → Standard** — `0936614`
2. **RenderQueue `Geometry+1`** — `41eeb61`
3. **AlphaTest disable on voxel material** — `2cacb29`
4. **Y-lift adjustment** — `0b6ba35`
5. **`VoxelScaleMultiplier` to `16`** — `698883e`
6. **EMISSION set to grey (`60%`)** — `96ca036`
7. **LOD `entityHeight` to `16`** — `96ca036`

## Transpiler guards (12)

1. Guard Main QuantumSprite transpiler entrypoint — `725f101`
2. Guard TileMapToSphere transpiler A — `36d7814`
3. Guard TileMapToSphere transpiler B — `36d7814`
4. Guard TileMapToSphere transpiler C — `36d7814`
5. Guard TileMapToSphere transpiler D — `36d7814`
6. Guard Lerp3D transpiler — `e9392d5`
7. Guard QuantumSprite effects transpiler — `105a928`
8. Guard QuantumSprite buildings transpiler — `105a928`
9. Guard drawProjectiles transpiler — `afb8a24`
10. Guard UpdateVelocity transpiler — `a6a7cce`
11. Guard DontShowPossessedUnit transpiler — `0d313bd`
12. Guard Move3D transpiler — `f89ff38`

## 7-step alpha.8 victory checklist

1. Validate base/parent material integrity pre-patch.
2. Replay transpiler sequence for each targeted method.
3. Confirm null/empty guards are present at every material boundary.
4. Validate Y-lift deltas in seam and terrain edge scenarios.
5. Validate LOD transition continuity and no pop/noise regressions.
6. Verify emission progression state machine across load/unload transitions.
7. Run end-to-end alpha.8→alpha.9 pass with zero regression deltas.

## References

- `d35f5c8` (`v2.0.0-alpha.8`) baseline
- `96ca036` (`v2.0.0-alpha.9`) release-end fix point
