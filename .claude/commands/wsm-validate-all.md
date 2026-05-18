---
description: Run all 10 phase journeys and aggregate results
---

# wsm-validate-all

Execute the entire test suite (all 10 phase journeys) back-to-back.

## What This Does

1. Runs journeys for phases 1–10 in sequence
2. Collects pass/fail status for each phase
3. Reports aggregate results and timings
4. Fails early if any phase fails (stops further execution)

This is the primary validation workflow before shipping a build.

## Steps

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$phases = @(1..10)
$results = @{}
$totalStart = Get-Date

foreach ($phase in $phases) {
    $journeyId = "us-wsm-phase-$phase-*"
    Write-Host "Running phase $phase..."
    & pwsh -File "$wsmRoot/Tools/wsm3d.ps1" journey run -Id $journeyId
    $results["phase-$phase"] = $LASTEXITCODE -eq 0
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Phase $phase failed. Stopping."
        break
    }
}

$totalElapsed = (Get-Date) - $totalStart
Write-Host "`nValidation Summary:"
$passed = ($results.Values | Where-Object { $_ }).Count
$failed = ($results.Values | Where-Object { -not $_ }).Count
Write-Host "Passed: $passed/10"
Write-Host "Failed: $failed/10"
Write-Host "Total time: $($totalElapsed.TotalMinutes)m"

if ($failed -gt 0) { exit 1 }
```

## Expected output (all pass)

```
Running phase 1...
✓ us-wsm-phase-1-buildings PASSED (38s)
Running phase 2...
✓ us-wsm-phase-2-procedural PASSED (42s)
Running phase 3...
✓ us-wsm-phase-3-complex PASSED (45s)
...
Running phase 10...
✓ us-wsm-phase-10-lod PASSED (41s)

Validation Summary:
Passed: 10/10
Failed: 0/10
Total time: 6.3m
```

## Expected output (one phase fails)

```
Running phase 1...
✓ us-wsm-phase-1-buildings PASSED (38s)
Running phase 2...
✗ us-wsm-phase-2-procedural FAILED
  - Building 2 gable roof: OCR confidence 0.32 < 0.50
✗ Phase 2 failed. Stopping.

Validation Summary:
Passed: 1/10
Failed: 1/10 (phases 2-10 skipped)
Total time: 1.4m
```

Use this before merging to verify all phases work correctly.
