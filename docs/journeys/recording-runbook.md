# Journey recording runbook

Use this when you need a clean screenshot set, a short GIF, or a video clip
for a Phenotype journey. It is written for the WorldSphereMod3D workflow,
but the capture discipline applies to any future journey added under
`docs/journeys/manifests/`.

## What this runbook covers

- Capturing the exact game state a journey step references
- Recording short GIFs and longer clips without drifting off the target UI
- Keeping filenames, paths, and crops stable enough for review and OCR
- Separating mock validation from live-game capture

## Before you capture

1. Start from a clean run. Use a fresh WorldBox launch, or at least a world
   whose state matches the journey text. Do not reuse a frame from a previous
   branch or an older build unless the journey explicitly says it is a
   regression comparison.
2. Disable unrelated overlays. Close debug windows, log viewers, and tools
   that cover the region you are trying to show.
3. Set the game to the intended resolution before recording. Keep the same
   resolution for every frame in the journey.
4. Decide whether you are capturing a baseline, a toggle action, or a
   verification state. The filename should reflect that role, not the
   underlying implementation detail.

## Recommended capture sequence

1. Open the journey doc and the matching manifest together.
2. Run the journey in the game until you reach the next labelled step.
3. Capture one frame per step, in order.
4. Review each frame immediately. If a frame is wrong, recapture it before
   you move on.
5. Only after the whole set is done, run mock verification for schema and
   OCR preflight, then live verification when the desktop/game setup is
   available.

## Screenshot rules

- Use a crop tight enough that OCR can read the relevant text without the
  noise of the whole desktop.
- Prefer 1280x720 screenshots unless the journey calls for a tighter crop.
- Keep every frame in a journey at the same aspect ratio.
- Name files exactly as the manifest expects, usually `frame-000.png`,
  `frame-001.png`, and so on.
- If the journey uses a before/after comparison, make the composite from the
  same scene, same zoom level, and same camera angle.

## GIF rules

- Use GIFs for short state transitions, not for long play sessions.
- Keep them to 2-6 seconds when possible.
- Favor visible transitions: install success, a toggle flipping, or the
  before/after moment where a feature becomes obvious.
- If the GIF is too large, cut the duration before lowering the visual
  quality.

## Recording guidance

- Record only the area that matters. A settings tab, a world view, or a
  single log tail is better than the whole desktop.
- If the capture depends on a hover or a menu being open, narrate that in the
  step intent so the next person can reproduce it.
- For live-game verification, keep one human-readable note outside the image
  explaining what changed between the baseline and the verified frame.
- If a step depends on a world reload, mark that explicitly in the manifest
  and do not compress it away into a single ambiguous frame.

## Validation split

Use both layers, but do not confuse them:

- `phenotype-journey verify --mode mock` checks manifest shape and OCR
  expectations without needing the game.
- `phenotype-journey verify --mode live` checks the actual captured frames
  and should be run once the screenshots are in place.

## Live capture checklist

- WorldBox is open in the intended window size.
- The correct mod is enabled and the conflicting upstream mod is disabled.
- The game state matches the journey step you are about to capture.
- The file path you are saving to matches the manifest.
- The frame you just captured is legible before you continue.

## Related docs

- [Journey README](./README.md)
- [Asset capture protocol](./assets/README.md)
- [Install & play](./install-and-play.md)
- [Diagnose performance](./diagnose-perf.md)
