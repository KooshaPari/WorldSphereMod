# Smoke-test screenshots

Local PNG captures for in-game smoke checks, handoff evidence, and optional SSIM comparison against canonical phase previews.

**Related:** [Smoke test index](../smoke-test-index.md) · [Phase preview gallery](../journeys/phase-previews/) · [Live verification — canonical bundle](../live-verification.md#canonical-live-proof-bundle)

## Capture

With WorldBox focused and the mod loaded:

```powershell
pwsh Tools/wsm3d.ps1 screenshot phase <n> -Name <slug> -WindowOnly
```

`<slug>` values per phase are listed in the [smoke test index](../smoke-test-index.md) matrix and per-phase sections. Output path:

`docs/screenshots/phase-<n>-<slug>.png`

## Git

The `**/screenshots/` gitignore rule applies here — PNGs are usually not committed. Keep captures locally for PR notes, live-verify bundles, or SSIM triage. Canonical committed fixtures for `before` / `after` SSIM live under [`docs/journeys/phase-previews/`](../journeys/phase-previews/).

## Expected layout (phases 1–10)

Each phase expects three slugs: `before`, `after`, plus one phase-specific closeup.

| Phase | Files |
|------:|-------|
| 1 | `phase-1-before.png`, `phase-1-after.png`, `phase-1-buildings.png` |
| 2 | `phase-2-before.png`, `phase-2-after.png`, `phase-2-buildings.png` |
| 3 | `phase-3-before.png`, `phase-3-after.png`, `phase-3-foliage.png` |
| 4 | `phase-4-before.png`, `phase-4-after.png`, `phase-4-water.png` |
| 5 | `phase-5-before.png`, `phase-5-after.png`, `phase-5-shadows-sky.png` |
| 6 | `phase-6-before.png`, `phase-6-after.png`, `phase-6-actors-rig.png` |
| 7 | `phase-7-before.png`, `phase-7-after.png`, `phase-7-nameplates.png` |
| 8 | `phase-8-before.png`, `phase-8-after.png`, `phase-8-sky-cycle.png` |
| 9 | `phase-9-before.png`, `phase-9-after.png`, `phase-9-effects.png` |
| 10 | `phase-10-before.png`, `phase-10-after.png`, `phase-10-lod-ladder.png` |

For live handoff proof, attach these captures (or PlayCUA artifacts) per the [canonical live proof bundle](../live-verification.md#canonical-live-proof-bundle).
