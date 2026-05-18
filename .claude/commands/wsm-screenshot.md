---
description: Capture a screenshot for journey authoring
argument-hint: <output filename> (optional)
---

# wsm-screenshot

Take a screenshot of the running game for use in test journeys.

## What This Does

Captures the current game viewport to `journeys/scratch/<timestamp>.png`.
With `$ARGS`, uses a custom filename.

Screenshots are useful for:
- Building before/after evidence
- Journey OCR assertion baselines
- Debugging visual issues

## Steps

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($ARGS.Count -gt 0) {
    $filename = $ARGS[0]
} else {
    $filename = (Get-Date -Format "yyyyMMdd_HHmmss") + ".png"
}
& pwsh -File "$wsmRoot/Tools/wsm3d.ps1" screenshot -Path "journeys/scratch/$filename"
```

## Usage

```
/wsm-screenshot                    # Capture to journeys/scratch/<timestamp>.png
/wsm-screenshot phase1-village.png # Capture to journeys/scratch/phase1-village.png
```

## Expected output

```
✓ Screenshot saved to: journeys/scratch/20260518_143256.png (1920x1080, 890KB)
```

Use the screenshot path in journey manifests for visual assertions.
