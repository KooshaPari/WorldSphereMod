# Phase 4 in-game smoke test — checklist

What to verify when you toggle `MeshWater = true` for the first time.

**Agentic automation:** [`Tools/wsm3d-playcua/sample-scenarios/phase-4-mesh-water.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-4-mesh-water.yaml) drives bridge `toggle_flag`, telemetry, and vision screenshot steps. Full programmatic + agentic gate order: [`docs/live-verification.md`](live-verification.md).

## Setup

```powershell
./Tools/install.ps1
```

That builds the mod (sanity check) and copies sources + assets to
`<WorldBox>/Mods/WorldSphereMod3D/`. Default WorldBox path is the standard Steam install.

Launch WorldBox. NeoModLoader compiles `Code/*.cs` at startup. If you see a red mod icon, the compute-shader/instancing hardware gate failed — Phase 10 will add a billboard fallback for that case; nothing to do here.

Load or generate a **water-heavy** world (ocean coast, lakes, rivers). Save slot 2 is the baseline used by the PlayCUA scenario.

## Regression checks (with `MeshWater = false`)

Keep `VoxelEntities` **ON** and prior phases as needed so upstream 3D paths stay warm; only Phase 4 is under test here.

Open the water-heavy map. Confirm:

- Terrain renders identically to upstream WorldSphereMod (sphere, cylinder, or flat shape per `CurrentShape`).
- Water tiles still use **vanilla flat tile colors** (no Gerstner mesh plane).
- Settings tab → WorldSphere → **Mesh Water** toggle is present and OFF (or flip OFF if your save inherited default-on).
- Land tiles at shorelines render normally (no black terrain holes).

If any of those fail, Phase 0–3 plumbing has regressed. Don't proceed.

## Enable mesh water

1. Settings → WorldSphere → toggle **Mesh Water** ON.
2. Pan the camera along shorelines and across open water: top-down, low-angle from each cardinal, mid-tilt 360°.
3. Optional bridge run (game + listener on `127.0.0.1:8766`):

   ```powershell
   python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-4-mesh-water.yaml
   ```

   See [`docs/live-verification.md`](live-verification.md) for OmniRoute vision env and install deps.

### Verify

| Check | Expected | Failure mode if broken |
|---|---|---|
| Water renders as animated mesh surface | Gerstner waves, depth tint, shoreline foam | Flat colored tiles → `MeshWater` didn't apply, or `WaterSurface` never created |
| Terrain under water hidden | `SphereTileColor` alpha suppressed on water tiles | Z-fighting / double draw → `WaterRender` Postfix not running |
| Shoreline foam visible at coast | Screen-space or per-tile foam band | Hard tile edge only → depth prepass off; check `WaterGerstner` fallback |
| Telemetry shows render work | Bridge `drawCalls > 0`, `instances > 10` after toggle | `drawCalls=0` with water visible → water mesh not in batcher flush |
| FPS stays playable over water | Mid-range hardware; watch full-map rebuild hitch on first toggle | Long freeze → `WaterMaskBuffer.RebuildMask` on large map; reduce map size for triage |
| No shader error banner | Clean UI, no red compile overlay | Black world / magenta water → shader missing; see `WaterSurface.EnsureMaterial` |

## Water detail knob (optional)

`WaterDetail` (default `1.0`) scales wave frequency and amplitude. At `0` the surface is nearly flat; at `2` chop is heavier. Not required to clear Phase 4.

## Multi-world session check (optional)

`WaterSurface.Destroy()` should run on `Sphere.Finish`, but a second world without restart may leave a stale water plane or mask. Workaround: restart WorldBox between worlds during testing.

## Capture screenshots

Drop comparison shots into `docs/screenshots/`:

- `phase-4-before.png` — `MeshWater = false`, same scene.
- `phase-4-after.png` — `MeshWater = true`, same scene + camera angle.
- `phase-4-water.png` — shoreline / open-water closeup (matches PlayCUA artifact `phase-4-mesh-water/water.png`).

Link them in the Phase 4 PR body when marking ready for review. Diff optional against `docs/journeys/phase-previews/` per [`docs/live-verification.md`](live-verification.md#ssim-optional-gate).

## What's expected to look bad

- **Phase 4-lite depth.** Per-tile float depth, not full SSBO — see `docs/phase4-architecture.md` (Phase 5 retrofits SSBO).
- **Cylinder seam sensitivity.** Gerstner phase uses cylindrical coords on default shape; flat-world uses `_FLAT_WORLD` keyword branch.
- **AssetBundle shader bake deferred.** Runtime may use built-in fallback until Phase 5b bundle ships — water should still render, not black void.

## If something is broken

- Check the WorldBox console (default toggle: backtick) for `[WorldSphereMod3D]` / `[WSM3D]` errors.
- Check `<WorldBox>/output_log.txt` or NeoModLoader's compile-error log.
- Toggle **Mesh Water** OFF — vanilla tile water colors return without restart.
- Re-run the PlayCUA scenario or `pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-4-mesh-water` (mock) for manifest drift.
- Open an issue on the PR with console excerpt + which check from this list failed.
