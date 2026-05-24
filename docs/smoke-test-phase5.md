# Phase 5 in-game smoke test — checklist

What to verify when you toggle `HighShadows = true` and `HdrSkybox = true` for the first time.

**Agentic automation:** [`Tools/wsm3d-playcua/sample-scenarios/phase-5-high-shadows.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-5-high-shadows.yaml) drives bridge `toggle_flag`, telemetry, and vision screenshot steps. Full programmatic + agentic gate order: [`docs/live-verification.md`](live-verification.md).

## Setup

```powershell
./Tools/install.ps1
```

That builds the mod (sanity check) and copies sources + assets to
`<WorldBox>/Mods/WorldSphereMod3D/`. Default WorldBox path is the standard Steam install.

Launch WorldBox. NeoModLoader compiles `Code/*.cs` at startup. If you see a red mod icon, the compute-shader/instancing hardware gate failed — Phase 10 will add a billboard fallback for that case; nothing to do here.

Load or generate a world with **terrain, actors, and open sky** (midday village works). Save slot 2 is the baseline used by the PlayCUA scenario.

## Regression checks (with `HighShadows = false` and `HdrSkybox = false`)

Keep `VoxelEntities` **ON** and Phases 2–4 as needed so mesh content exists to light and shadow. Only Phase 5 lighting/sky is under test here.

Open the map. Confirm:

- Terrain and meshes render (Phases 1–4 paths unchanged).
- Actors show **flat sprite shadow quads** under them (vanilla-style), not cascaded mesh shadows.
- Sky reads as **flat / low-dynamic** compared to HDR procedural target.
- Settings tab → WorldSphere → **High Shadows** and **HDR Skybox** toggles are present and OFF (or flip OFF if your save inherited default-on).

If any of those fail, Phase 0–4 plumbing has regressed. Don't proceed.

## Enable cascaded shadows + HDR skybox

1. Settings → WorldSphere → toggle **High Shadows** ON.
2. Toggle **HDR Skybox** ON (Phase 5b; bundled in the same smoke pass).
3. Pan the camera through the village: top-down, low-angle from each cardinal, mid-tilt 360° — look for shadow contact on terrain and voxel/proc meshes.
4. Optional bridge run (game + listener on `127.0.0.1:8766`):

   ```powershell
   python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-5-high-shadows.yaml
   ```

   See [`docs/live-verification.md`](live-verification.md) for OmniRoute vision env and install deps.

### Verify

| Check | Expected | Failure mode if broken |
|---|---|---|
| Directional sun casts mesh shadows | Contact shadows on actors/buildings/terrain from cascades | Flat quads only → `HighShadows` didn't apply, or `ShadowCascadeConfig` failed |
| Flat sprite shadows suppressed on 3D actors | `SpriteShadowPatch` skips when `SunDriver.Active` | Double shadows (flat + mesh) → patch not running |
| Crossed-quad foliage keeps flat shadow quads | Trees still have ground decal shadow | Missing tree shadows is OK; missing actor shadows is not |
| HDR procedural sky visible | Gradient sky + Fresnel on water (if Phase 4 on) | Flat gray wash → `HdrSkybox` off or sky rig not initialized |
| Voxel/proc meshes show directional shading | Lit faces when `VoxelLit` bundle loads; unlit fallback still acceptable for gate | All flat color → `EnsureMaterial` still on unlit path |
| Telemetry shows render work | Bridge `drawCalls > 0`, `frameMs` stable after settle | Massive hitch → cascade count 4 on weak GPU; try toggling `HighShadows` off for A/B |
| No shader error banner | Clean UI, no red compile overlay | Console: `[WorldSphereMod3D]` + URP pipeline errors |

## Color grading LUT (optional)

`ColorGradingLut` defaults **ON** alongside Phase 5b. With **HDR Skybox** on, verify the scene is not crushed to black/white. Toggle LUT off for A/B if colors look wrong — not required to clear Phase 5.

## Multi-world session check (optional)

`ShadowCascadeConfig.Reset()` and sun rig teardown should run on world unload, but live toggle reapplication without reload can leave stale cascade settings (known follow-up). Workaround: restart WorldBox between worlds during testing.

## Capture screenshots

Drop comparison shots into `docs/screenshots/`:

- `phase-5-before.png` — `HighShadows = false`, `HdrSkybox = false`, same scene.
- `phase-5-after.png` — both ON, same scene + camera angle.
- `phase-5-shadows-sky.png` — shadow contact + sky closeup (matches PlayCUA artifact `phase-5-high-shadows/shadows-sky.png`).

Link them in the Phase 5 PR body when marking ready for review. Diff optional against `docs/journeys/phase-previews/` per [`docs/live-verification.md`](live-verification.md#ssim-optional-gate).

## What's expected to look bad

- **Frozen time-of-day.** `SunDriver` ships at ~11:00 until Phase 8 drives the cycle — see `docs/phase5-architecture.md`.
- **Foliage still unlit.** Crossed-quad wind shader distinct from `VoxelLit`; full foliage lighting is a follow-up.
- **Runtime toggle gaps.** Turning `HighShadows` off mid-session may not fully restore pipeline asset until reload.

## If something is broken

- Check the WorldBox console (default toggle: backtick) for `[WorldSphereMod3D]` / URP errors.
- Check `<WorldBox>/output_log.txt` or NeoModLoader's compile-error log.
- Toggle **High Shadows** and **HDR Skybox** OFF — flat shadows and simpler sky return without restart (mesh content unchanged).
- Re-run the PlayCUA scenario or `pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-5-shadows` (mock) for manifest drift.
- Open an issue on the PR with console excerpt + which check from this list failed.
