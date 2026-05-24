# Phase 8 in-game smoke test — checklist

What to verify when you toggle `DayNightCycle = true` for the first time.

**Agentic automation:** [`Tools/wsm3d-playcua/sample-scenarios/phase-8-day-night.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-8-day-night.yaml) drives bridge `toggle_flag`, telemetry, and vision screenshot steps. Full programmatic + agentic gate order: [`docs/live-verification.md`](live-verification.md).

## Setup

```powershell
./Tools/install.ps1
```

That builds the mod (sanity check) and copies sources + assets to
`<WorldBox>/Mods/WorldSphereMod3D/`. Default WorldBox path is the standard Steam install.

Launch WorldBox. NeoModLoader compiles `Code/*.cs` at startup. If you see a red mod icon, the compute-shader/instancing hardware gate failed — Phase 10 will add a billboard fallback for that case; nothing to do here.

Load or generate a world with **open sky** (midday village or plains). Save slot 2 is the baseline used by the PlayCUA scenario.

## Regression checks (with `DayNightCycle = false`)

Keep Phases 1–7 as needed so terrain and sun exist; only Phase 8 time-of-day is under test here.

Open the map. Confirm:

- Sky reads as **static** (Phase 5 HDR or flat skybox frozen ~11:00 when `HighShadows` / `HdrSkybox` are on).
- Sun position does not advance over ~30 seconds of real time.
- Settings tab → WorldSphere → **Day Night Cycle** toggle is present and OFF (or flip OFF if your save inherited default-on).
- Fog may still apply via `FogDensity` slider even when cycle is off.

If any of those fail, Phase 0–7 plumbing has regressed. Don't proceed.

## Enable day/night cycle

1. Settings → WorldSphere → toggle **Day Night Cycle** ON.
2. Let the simulation run 30–60 seconds; watch sky gradient and sun color shift (dawn → noon → dusk).
3. Optional bridge run (game + listener on `127.0.0.1:8766`):

   ```powershell
   python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-8-day-night.yaml
   ```

   See [`docs/live-verification.md`](live-verification.md) for OmniRoute vision env and install deps.

### Verify

| Check | Expected | Failure mode if broken |
|---|---|---|
| Sky shows procedural gradient | Horizon tint + sun disc, not flat solid wash | Static gray → `DayNightCycle` didn't apply, or `ProceduralSky` not created |
| Time advances autonomously | `TimeOfDay` driver moves; `SunRig` color updates | Frozen noon → `TimeOfDay.EnsureCreated` skipped |
| Fog when `FogDensity > 0` | Atmospheric haze with cycle | No fog → density zero in save; bump slider for A/B |
| Phase 5 sun still coherent | When `HighShadows` on, shadows track sun direction | Shadow direction frozen → `SunDriver` not receiving TOD |
| Telemetry shows render work | Bridge `drawCalls > 0`, `frameMs` stable | Hitch on first sky rebuild only |
| No shader error banner | Clean UI, no red compile overlay | Console: `[WorldSphereMod3D]` + sky shader errors |

## Fog density knob (optional)

`FogDensity` (default `0.05`) scales atmospheric haze. At `0` fog is off while cycle may still run. Not required to clear Phase 8.

## Multi-world session check (optional)

`TimeOfDay` / `ProceduralSky` teardown on unload may leave stale sky state when toggling mid-session without reload. Workaround: restart WorldBox between worlds during testing.

## Capture screenshots

Drop comparison shots into `docs/screenshots/`:

- `phase-8-before.png` — `DayNightCycle = false`, same scene.
- `phase-8-after.png` — `DayNightCycle = true`, same scene + camera angle (ideally mid-cycle color).
- `phase-8-sky-cycle.png` — sky / horizon closeup (matches PlayCUA artifact `phase-8-day-night/sky-cycle.png`).

Link them in the Phase 8 PR body when marking ready for review. Diff optional against `docs/journeys/phase-previews/` per [`docs/live-verification.md`](live-verification.md#ssim-optional-gate).

## What's expected to look bad

- **HDR skybox vs procedural sky.** Phase 5 `HdrSkybox` and Phase 8 procedural sky can disagree until unified — see `docs/phase8-architecture.md`.
- **Fast cycle in test saves.** Default day speed may wrap quickly; tune Sky Settings sliders for screenshots.
- **Runtime toggle gaps.** Turning `DayNightCycle` off mid-session may not restore Phase 5 frozen noon until reload.

## If something is broken

- Check the WorldBox console (default toggle: backtick) for `[WorldSphereMod3D]` / `[TimeOfDay]` errors.
- Check `<WorldBox>/output_log.txt` or NeoModLoader's compile-error log.
- Toggle **Day Night Cycle** OFF — static sky returns; Phase 5 sun may stay at default hour.
- Re-run the PlayCUA scenario or `pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-8-day-night` (mock) for manifest drift.
- Open an issue on the PR with console excerpt + which check from this list failed.
