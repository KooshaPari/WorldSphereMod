---
description: Capture a screenshot and open it for journey validation
argument-hint: <output filename> (optional)
---

# wsm-validate-screenshot

Capture the current game viewport and open the PNG in the OS-default viewer.

## What This Does

Saves to `journeys/scratch/<timestamp>.png`, or to a custom filename from
`$ARGS`, then opens the image for quick journey-manifest validation.

## Steps

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($ARGS.Count -gt 0) {
    $filename = $ARGS[0]
} else {
    $filename = (Get-Date -Format "yyyyMMdd_HHmmss") + ".png"
}

$relativePath = "journeys/scratch/$filename"
$fullPath = Join-Path $wsmRoot $relativePath

& pwsh -File "$wsmRoot/Tools/wsm3d.ps1" screenshot -Path $relativePath
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$resolvedPath = (Resolve-Path $fullPath).Path
if ($IsWindows) {
    Start-Process $resolvedPath
} elseif ($IsMacOS) {
    & open $resolvedPath
} else {
    & xdg-open $resolvedPath
}

Write-Host "Opened screenshot: $resolvedPath"
```

## Usage

```
/wsm-validate-screenshot
/wsm-validate-screenshot phase1-village.png
```
