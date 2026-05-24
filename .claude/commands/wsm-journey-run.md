---
description: Run a test journey and report pass/fail
argument-hint: <journey id> (required)
---

# wsm-journey-run

Verify a single journey by ID and stream the results.

## What This Does

1. Verifies a journey by ID (e.g., `us-wsm-phase-1-buildings`)
2. Streams the journey output to console
3. On failure, parses `manifest.verified.json` to summarize OCR assertion failures
4. Reports final pass/fail status

Journey IDs follow the pattern: `us-wsm-phase-<N>-<name>`

## Steps

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($ARGS.Count -eq 0) {
    Write-Error "Usage: /wsm-journey-run <journey id>"
    exit 1
}
$journeyId = $ARGS[0]
$result = & pwsh -File "$wsmRoot/Tools/wsm3d.ps1" journey verify -Id $journeyId
if ($LASTEXITCODE -ne 0) {
    Write-Host "Journey failed. Reading failure summary..."
    $manifest = Get-Content "docs/journeys/manifests/$journeyId/manifest.verified.json" | ConvertFrom-Json
    Write-Host "Failed assertions:"
    $manifest.assertions | Where-Object { -not $_.passed } | ForEach-Object {
        Write-Host "  - $($_.description): $($_.reason)"
    }
}
exit $LASTEXITCODE
```

## Usage

```
/wsm-journey-run us-wsm-phase-1-buildings
/wsm-journey-run us-wsm-phase-2-procedural
/wsm-journey-run us-wsm-phase-5-lighting
```

## Expected output (pass)

```
Running journey: us-wsm-phase-1-buildings...
[14:45:20] Launching game...
[14:45:30] Game ready. Waiting for world...
[14:45:45] World loaded. Running assertions...
[14:46:00] ✓ All assertions passed (8/8)
✓ Journey PASSED in 40 seconds
```

## Expected output (fail)

```
Running journey: us-wsm-phase-2-procedural...
[14:47:20] ✓ Building 1 roofline detected
[14:47:25] ✗ Building 2 gable roof not detected
[14:47:30] ✗ Building 3 color mismatch (expected faction color)
Journey FAILED. Failed assertions:
  - Building 2 gable roof: OCR confidence 0.32 < 0.50 threshold
  - Building 3 color: Pixel variance 12% > 5% tolerance
```

By default this uses mock verification. Use `-Live` when you want to validate against a live game session.

Use `/wsm-validate-all` to verify all 10 phase journeys at once.
