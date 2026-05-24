# Phase 3 in-game smoke test — checklist

What to verify when you toggle `CrossedQuadFoliage = true` for the first time.

**Agentic automation:** [`Tools/wsm3d-playcua/sample-scenarios/phase-3-crossed-quad-foliage.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-3-crossed-quad-foliage.yaml) drives bridge `toggle_flag`, telemetry, and vision screenshot steps. Cloud crossed-quad (`fx_cloud`): [`phase-3b-cloud-crossed-quad.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-3b-cloud-crossed-quad.yaml). Full programmatic + agentic gate order: [`docs/live-verification.md`](live-verification.md).

## Setup

```powershell
./Tools/install.ps1
```

That builds the mod (sanity check) and copies sources + assets to
`<WorldBox>/Mods/WorldSphereMod3D/`. Default WorldBox path is the standard Steam install.

Launch WorldBox. NeoModLoader compiles `Code/*.cs` at startup. If you see a red mod icon, the compute-shader/instancing hardware gate failed — Phase 10 will add a billboard fallback for that case; nothing to do here.

Load or generate a **foliage-heavy** world (forest, jungle, savanna with trees/bushes). Save slot 2 is the baseline used by the PlayCUA scenario.

## Regression checks (with `CrossedQuadFoliage = false`)

Keep `VoxelEntities` **ON** (and `ProceduralBuildings` **ON** if your save uses proc buildings) so Phase 1–2 coverage stays warm; only Phase 3 is under test here.

Open the foliage-heavy map. Confirm:

- Terrain renders identically to upstream WorldSphereMod (sphere, cylinder, or flat shape per `CurrentShape`).
- Trees and bushes still draw as **2D billboards** or Phase 2 proc meshes (not crossed-quad pairs).
- Settings tab → WorldSphere → **Crossed Quad Foliage** toggle is present and OFF (or flip OFF if your save inherited default-on).
- Rocks and walls behave as before (no accidental crossed-quad spam on non-foliage assets).

If any of those fail, Phase 0–2 plumbing has regressed. Don't proceed.

## Enable crossed-quad foliage

1. Settings → WorldSphere → toggle **Crossed Quad Foliage** ON.
2. Pan the camera through the forest: top-down, low-angle from each cardinal, mid-tilt 360°.
3. Optional bridge run (game + listener on `127.0.0.1:8766`):

   ```powershell
   python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-3-crossed-quad-foliage.yaml
   ```

   See [`docs/live-verification.md`](live-verification.md) for OmniRoute vision env and install deps.

### Verify

| Check | Expected | Failure mode if broken |
|---|---|---|
| Trees/bushes render as crossed-quad meshes | Two perpendicular quads (+ ground quad for rocks), wind sway on foliage | Still flat sprites → `CrossedQuadFoliage` didn't apply, or `BuildingProcRender` / `FoliageTileRender` never submitted |
| Surface overlays (grass/snow/road) use 3b path when visible | Overlay tiles skip 2D Tilemap when flag on | Flat tilemap only → `FoliageTileRender` prefix not running or `IsWorld3D` false |
| Clouds use crossed-quad when enabled | `fx_cloud` via `EmitCrossedQuad` (optional 3b scenario) | Flat cloud sprites → cloud refactor not wired; run `phase-3b-cloud-crossed-quad.yaml` |
| Telemetry shows mesh work | Bridge `drawCalls > 0`, `instances > 10` after toggle | `drawCalls=0` with foliage visible → flush gate dropped meshes (see ADR-0013) |
| FPS stays playable in forest | Mid-range hardware; compare to billboard baseline | Severe hitch → profile `CrossedQuadMeshCache` / wind driver |
| No shader error banner | Clean UI, no red compile overlay | Console: `[WorldSphereMod3D]` + `output_log.txt` |

## Foliage density knob (optional)

`FoliageDensity` in settings scales overlay emission (default `1.0`). With it at `0`, surface-overlay crossed-quads thin out; trees routed via `BuildingRules` shape are unaffected. Not required to clear Phase 3.

## Multi-world session check (optional)

Same limitation as Phase 1–2: foliage material / cache reset across world reload without restart may leave crossed-quads invisible after the second generation. Workaround: restart WorldBox between worlds during testing.

## Capture screenshots

Drop comparison shots into `docs/screenshots/`:

- `phase-3-before.png` — `CrossedQuadFoliage = false`, same scene.
- `phase-3-after.png` — `CrossedQuadFoliage = true`, same scene + camera angle.
- `phase-3-foliage.png` — tree/bush closeup (matches PlayCUA artifact `phase-3-crossed-quad-foliage/foliage.png`).

Link them in the Phase 3 PR body when marking ready for review. Diff optional against `docs/journeys/phase-previews/` per [`docs/live-verification.md`](live-verification.md#ssim-optional-gate).

## What's expected to look bad

- **Unlit foliage.** Wind displacement only until Phase 5 `VoxelLit` / foliage lit shader ships — see `docs/phase3-architecture.md`.
- **Readable atlas guard.** Non-readable sprite textures return empty meshes (same pattern as Phase 1 `GetPixels32` guard).
- **Heuristic shape routing.** Some assets may mis-classify between `CrossedQuad` and `Single` (rocks); tune `BuildingRulesRegistry` overrides.

## If something is broken

- Check the WorldBox console (default toggle: backtick) for `[WorldSphereMod3D]` / `[WSM3D]` errors.
- Check `<WorldBox>/output_log.txt` or NeoModLoader's compile-error log.
- Toggle **Crossed Quad Foliage** OFF — trees revert to Phase 2 proc or Phase 1 voxel path without restart.
- Re-run the PlayCUA scenario or `pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-3-crossed-foliage` (mock) for manifest drift.
- Open an issue on the PR with console excerpt + which check from this list failed.
