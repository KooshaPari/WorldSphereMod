# Phase 1 in-game smoke test — checklist

What to verify when you toggle `VoxelEntities = true` for the first time.

## Setup

```powershell
./Tools/install.ps1
```

That builds the mod (sanity check) and copies sources + assets to
`<WorldBox>/Mods/WorldSphereMod3D/`. Default WorldBox path is the standard Steam install.

Launch WorldBox. NeoModLoader compiles `Code/*.cs` at startup. If you see a red mod icon, the compute-shader/instancing hardware gate failed — Phase 10 will add a billboard fallback for that case; nothing to do here.

## Regression checks (with `VoxelEntities = false`, the default)

Open a small map. Confirm:
- Terrain renders identically to upstream WorldSphereMod (sphere, cylinder, or flat shape per `CurrentShape`).
- Actors still draw as 2D billboards.
- Buildings still draw as 2D billboards.
- Settings tab → WorldSphere section opens; the "Voxel Entities" toggle is present and OFF.

If any of those fail, Phase 0/1 plumbing has regressed. Don't proceed.

## Enable voxel actors

1. Settings → WorldSphere → toggle "Voxel Entities" ON.
2. Generate a small kingdom (~500 units).
3. Sweep the camera through 6 positions: top-down, low-angle from each cardinal direction, mid-tilt 360°.

### Verify

| Check | Expected | Failure mode if broken |
|---|---|---|
| Actors render as voxel meshes | Per-pixel cube silhouette of each sprite, unlit | Sprites still 2D → Voxel toggle didn't apply, or `EnsureMaterial` failed (check console for `[WorldSphereMod3D] No voxel shader found`) |
| Walking actors stay upright | Body stays vertical, only yaw rotates | Actors lean/topple sideways → Phase 1 fix #2 (yaw-only) regressed |
| All actors are correctly tinted | Each kingdom's actors keep their kingdom color, no white actors in any batch | Final batch has white/wrong tint → Phase 1 fix #1 (color array tail) regressed |
| No flicker on actor count crossing 1023 | Smooth as army grows | Random voxel flicker → cache eviction destroying in-flight meshes (Phase 1 fix #3 regressed) |
| FPS stays > 30 with 500 actors | Mid-range hardware target | Lower → check `_voxel_stats` profiling logs (when shipped) |

## Enable voxel buildings (no procgen yet)

Verify `ProceduralBuildings` is **OFF** (default) and `VoxelEntities` is ON.

1. Place several buildings (huts, walls, stockpiles).
2. Verify they render as voxel meshes (cube-silhouette of each sprite).
3. Pan camera 360° — same checks as actors.

Buildings tagged in `Constants.PerpBuildings` (stockpiles) should still render as flat billboards — they're ground-decals where voxelization adds nothing.

## Multi-world session check (optional)

Phase 1 fix #4 covers a `VoxelRender.Reset()` for the material across world reloads, but the reset is not wired to a hook yet. If you generate a new world without restarting WorldBox, voxel rendering may stop after the second generation. This is the "Medium" issue from the review — known limitation in alpha, will be fixed when a world-reload Postfix lands in `Core`. Workaround: restart the game between worlds during testing.

## Capture screenshots

Drop comparison shots into `docs/screenshots/`:
- `phase-1-before.png` — `VoxelEntities = false`, same scene.
- `phase-1-after.png` — `VoxelEntities = true`, same scene + camera angle.
- `phase-1-buildings.png` — voxel buildings closeup.

Link them in PR #1's body when marking the PR ready for review.

## What's expected to look bad

- **Unlit appearance.** No ambient occlusion, no directional shading. Everything has the same flat color across all faces. Phase 5 (lighting + cascaded shadows) fixes this.
- **Square voxels.** Per-pixel cubes — that's the design. Greedy meshing merges same-color faces but keeps the cubic silhouette. Phase 6 adds skeletal animation; Phase 10 adds an LOD ladder + impostor fallback.
- **No real shadows.** The flat shadow sprite still draws under each actor. Phase 5 replaces it.

## If something is broken

- Check the WorldBox console (default toggle: backtick) for `[WorldSphereMod3D]` errors.
- Check `<WorldBox>/output_log.txt` or NeoModLoader's compile-error log.
- Toggle `VoxelEntities` OFF in settings — that immediately reverts to vanilla sprite rendering, no restart needed.
- Re-run the PlayCUA scenario or `pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase1` (mock) for manifest drift.
- Open an issue on the PR with the console excerpt + which check from this list failed.
