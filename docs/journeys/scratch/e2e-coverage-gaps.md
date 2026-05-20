# WSM3D E2E Coverage Gaps

Scope: `docs/journeys/manifests/*/manifest.json` vs `PLAN.md` phases 1-10, plus harness support in `Tools/wsm3d-mcp/` and `Tools/wsm3d.ps1`.

## Inventory

| Phase | Manifest | Golden path | Edge cases | Failure cases | Notes |
|---|---|---:|---:|---:|---|
| 1 Voxel Entities | yes | yes | partial | no | Actor-only smoke; plan also covers items, drops, projectiles, talk bubbles, status icons, batching/cache/fallback. |
| 2 Procedural Buildings | yes | yes | partial | no | Only checks “buildings become meshes”; plan also covers footprint extrusion, roof inference, door/window detection, rules overrides, orientation. |
| 3 Crossed-Quad Foliage | yes | yes | partial | no | Covers trees/bushes; plan also calls out clouds, decorations, wind behavior, and no billboard pop from all angles. |
| 4 Mesh Water | yes | yes | partial | no | Confirms water replacement; plan also requires Gerstner waves, shoreline foam, depth tint, and double-blue removal. |
| 5 High Shadows | yes | yes | partial | no | Confirms shadow visibility; plan also includes cascades, per-vertex normals, SSAO, and fallback shadow path behavior. |
| 6 Skeletal Animation | yes | yes | partial | no | Generic animation only; plan also covers humanoid/quadruped rigs, Crabzilla/Dragon, and static fallback for unknown rigs. |
| 7 Worldspace UI | yes | yes | partial | no | Covers nameplates/HP/selection rings; plan also includes damage numbers, depth fade, and terrain-intersection fade. |
| 8 Day-Night Cycle | yes | yes | partial | no | Covers sky color shift; plan also requires procedural sky, sun linkage, ambient temperature shifts, fog, and slider wiring. |
| 9 PostFX | yes | yes | partial | no | Covers bloom/color grading; plan also includes particles, decals, and pooled effect replacements. |
| 10 LOD Impostor | yes | yes | partial | no | Covers near/far split; plan also requires mid LOD, compute-shader fallback, culling, and perf-budget verification. |

## Harness support

`Tools/wsm3d.ps1` adds the operational driver for journeys: `journey list`, `journey run`, `journey verify`, `journey capture`, `settings toggle`, and `screenshot`. It can toggle phase flags and capture manifest steps, but it does not add scenario diversity beyond what the manifest declares.

`Tools/wsm3d-mcp/` mirrors that surface for MCP use:
- `wsm3d_mcp/tools/journey.py` shells to `phenotype-journey list/run/verify`.
- `wsm3d_mcp/tools/settings.py` toggles SavedSettings booleans.
- `wsm3d_mcp/tools/game.py` handles launch/kill/screenshot.
- `wsm3d_mcp/server.py` exposes those as `journey_*`, `settings_*`, and `game_*` tools.

These helpers improve execution and repeatability, but the actual E2E assertions still come from the manifests.

## Ranked gaps

1. Phase 1, most under-tested: the manifest only proves actor voxel replacement and misses most of the plan surface.
2. Phase 2: no coverage for roof/door/window heuristics, overrides, or orientation rules.
3. Phase 6: no rig-variant coverage, no Crabzilla/Dragon special cases, no fallback path checks.
4. Phase 10: no mid-LOD, fallback compatibility, or performance-budget assertions.
5. Phase 5: no cascade/normal/SSAO/fallback behavior.
6. Phase 8: no procedural sky, sun linkage, or fog coverage.
7. Phase 3: no cloud/decor/360-degree pop checks.
8. Phase 9: no particles or decal verification.
9. Phase 4: no shoreline foam, water depth, or double-blue regression checks.
10. Phase 7, least under-tested of the set: still missing damage-number and fade/intersection coverage, but it at least covers the core visible UI trio.

## Bottom line

Every phase has a manifest and every manifest includes a happy-path plus a broad “no Exception/Error” guard. None of the manifests exercise explicit failure cases such as mod load failure, missing assets, or named fallback behavior; edge coverage is limited to a baseline phase-off snapshot rather than a regression scenario.
