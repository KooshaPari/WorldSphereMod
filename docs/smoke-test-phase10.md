# Phase 10 in-game smoke test — checklist

What to verify for LOD impostor fallback at distance (no dedicated `wsm3d toggle` phase key — tune via settings / camera distance).

See [`docs/live-verification.md`](live-verification.md) and PlayCUA scenario `Tools/wsm3d-playcua/sample-scenarios/phase-10-lod.yaml`.

## Capture screenshots

Drop comparison shots into `docs/screenshots/`:

- `phase-10-before.png` — full-detail / near-camera baseline.
- `phase-10-after.png` — impostor tier visible at distance, same scene + camera angle.
- `phase-10-lod.png` — distant-entity impostor closeup.
