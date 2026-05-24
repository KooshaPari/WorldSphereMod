---
description: Tail mod log for recent [WSM3D] entries
argument-hint: <grep pattern> (optional)
---

# wsm-log

Stream recent mod-tagged log lines from the running game.

## What This Does

Shows the last 80 lines of `Player.log` filtered for `[WSM3D]` entries.
With `$ARGS`, accepts a custom grep pattern (e.g., `Phase` or `Error`).

## Steps

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($ARGS.Count -gt 0) {
    $pattern = $ARGS[0]
} else {
    $pattern = "WSM3D"
}
& pwsh -File "$wsmRoot/Tools/wsm3d.ps1" log -Tail 80 -Grep $pattern
```

## Usage

```
/wsm-log                 # Last 80 WSM3D lines
/wsm-log Error           # Last 80 lines containing "Error"
/wsm-log "Phase 2"       # Last 80 lines containing "Phase 2"
```

## Expected output

```
[14:32:42] [WSM3D] Bootstrap: DLL loaded in 125ms
[14:32:43] [WSM3D] Phase 1: Geometry initialization started
[14:32:44] [WSM3D] Phase 1: Voxel grid created (256³)
[14:32:45] [WSM3D] Phase 2: Building render system ready
```

Use this to debug initialization issues or track mod state during play.
