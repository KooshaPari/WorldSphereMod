# Phase 2 in-game smoke test — checklist

What to verify when you toggle `ProceduralBuildings = true` for the first time.

**Agentic automation:** [`Tools/wsm3d-playcua/sample-scenarios/phase-2-procedural-buildings.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-2-procedural-buildings.yaml) drives bridge `toggle_flag`, telemetry, and vision screenshot steps. Full programmatic + agentic gate order: [`docs/live-verification.md`](live-verification.md).

## Setup

```powershell
./Tools/install.ps1
```

That builds the mod (sanity check) and copies sources + assets to
`<WorldBox>/Mods/WorldSphereMod3D/`. Default WorldBox path is the standard Steam install.

Launch WorldBox. NeoModLoader compiles `Code/*.cs` at startup. If you see a red mod icon, the compute-shader/instancing hardware gate failed — Phase 10 will add a billboard fallback for that case; nothing to do here.

Load or generate a **building-heavy** world (village with huts, walls, towers). Save slot 2 is the baseline used by the PlayCUA scenario.

## Regression checks (with `ProceduralBuildings = false`)

Keep `VoxelEntities` **ON** so Phase 1 actor coverage stays warm; only Phase 2 is under test here.

Open the building-heavy map. Confirm:

- Terrain renders identically to upstream WorldSphereMod (sphere, cylinder, or flat shape per `CurrentShape`).
- Buildings still draw as **voxel cube-silhouettes** (Phase 1 building path), not flat 2D sprites.
- Settings tab → WorldSphere → **Procedural Buildings** toggle is present and OFF (or flip OFF if your save inherited default-on).
- Stockpiles (`stockpile`, `stockpile_acidproof`, `stockpile_fireproof`) still render as flat billboards per `Constants.PerpBuildings`.

If any of those fail, Phase 0/1 plumbing has regressed. Don't proceed.

## Enable procedural building meshes

1. Settings → WorldSphere → toggle **Procedural Buildings** ON.
2. Pan the camera through the village: top-down, low-angle from each cardinal, mid-tilt 360°.
3. Optional bridge run (game + listener on `127.0.0.1:8766`):

   ```powershell
   python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-2-procedural-buildings.yaml
   ```

   See [`docs/live-verification.md`](live-verification.md) for OmniRoute vision env and install deps.

### Verify

| Check | Expected | Failure mode if broken |
|---|---|---|
| Buildings render as procedural meshes | Extruded walls + inferred roof (not per-pixel cubes) | Still voxel cubes → `ProceduralBuildings` didn't apply, or `BuildingProcRender.EmitMeshes` never submitted (see ADR-0012 diag) |
| Non-stockpile buildings switch path | Huts/walls/towers are 3D proc meshes | Flat sprites → flag off or `VoxelRender` still drawing buildings (proc path gated wrong) |
| Stockpiles stay flat billboards | Ground-decal stockpiles unchanged | Stockpiles voxelized → `PerpBuildings` filter regressed |
| Telemetry shows mesh work | Bridge `drawCalls > 0`, `instances > 10` after toggle | `drawCalls=0` with buildings visible → silent no-op (ADR-0012); check `[WSM3D] ProcMeshEmit` logs |
| FPS stays playable in village | Mid-range hardware; compare to Phase 1 voxel-building baseline | Severe hitch → profile `ProcGenCache` / mesh gen; reduce visible building count for triage |
| No shader error banner | Clean UI, no red compile overlay | Console: `[WorldSphereMod3D]` + `output_log.txt` |

## Style procgen branch (optional)

`BuildingStyleProcgen` stays **OFF** by default. With it OFF, `BuildingProcRender` uses the voxel-derived proc mesh path. If you enable **Building Style Procgen** for a second pass, verify stylized roofs don't double-draw the vanilla sprite — that's a separate toggle, not required to clear Phase 2.

## Multi-world session check (optional)

Same limitation as Phase 1: material/voxel reset across world reload without restart may leave proc meshes invisible after the second generation. Workaround: restart WorldBox between worlds during testing.

## Capture screenshots

Drop comparison shots into `docs/screenshots/`:

- `phase-2-before.png` — `ProceduralBuildings = false`, `VoxelEntities = true`, same scene.
- `phase-2-after.png` — `ProceduralBuildings = true`, same scene + camera angle.
- `phase-2-buildings.png` — procedural building closeup (matches PlayCUA artifact `phase-2-procedural-buildings/buildings.png`).

Link them in the Phase 2 PR body when marking ready for review. Diff optional against `docs/journeys/phase-previews/` per [`docs/live-verification.md`](live-verification.md#ssim-optional-gate).

## What's expected to look bad

- **Heuristic roofs.** Gable/hipped inference from sprite silhouettes will mis-classify some assets. Tune `BuildingMeshGen` thresholds or add `BuildingRules` overrides — see `docs/phase2-architecture.md`.
- **Unlit meshes.** Same flat shading as Phase 1 until Phase 5 lighting lands.
- **No skeletal building animation.** Static proc meshes only; Phase 6 covers actors.

## If something is broken

- Check the WorldBox console (default toggle: backtick) for `[WorldSphereMod3D]` / `[WSM3D]` errors.
- Check `<WorldBox>/output_log.txt` or NeoModLoader's compile-error log.
- Toggle **Procedural Buildings** OFF — buildings fall back to Phase 1 voxel silhouettes (when `VoxelEntities` is on) without restart.
- Re-run the PlayCUA scenario or `pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-2-mesh-buildings` (mock) for manifest drift.
- Open an issue on the PR with console excerpt + which check from this list failed.
