# Phase 8 in-game smoke test — checklist

What to verify when you toggle `DayNightCycle = true` for the first time.

See [`docs/live-verification.md`](live-verification.md) and PlayCUA scenario `Tools/wsm3d-playcua/sample-scenarios/phase-8-day-night.yaml`.

## Capture screenshots

Drop comparison shots into `docs/screenshots/`:

- `phase-8-before.png` — `DayNightCycle = false`, same scene.
- `phase-8-after.png` — `DayNightCycle = true`, same scene + camera angle.
- `phase-8-day-night.png` — sky color cycle closeup (dawn/noon/dusk).
