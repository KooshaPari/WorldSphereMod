---
description: Parse startup InitProfiler timings from Player.log
argument-hint: -DryRun -Json
---

# wsm-profile

Collect startup profiler buckets from `[WSM3D] InitProfiler` lines.

## What This Does

- Launches WorldBox when not already running.
- Waits 90 seconds for initialization to settle.
- Kills the launched instance.
- Parses the latest `Player.log` for `[WSM3D] InitProfiler ... name=<bucket> ... duration_s=<seconds>` lines.
- Sorts buckets by slowest total duration and prints per-bucket sums.

Use `-DryRun` when you only want to parse an existing `Player.log` without launching/killing WorldBox.

## Steps

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$args = @()
if ($ARGS.Count -gt 0) {
    $args += $ARGS
} else {
    $args = @()
}
& pwsh -File "$wsmRoot/Tools/wsm3d.ps1" profile @args
```

## Usage

```
/wsm-profile
/wsm-profile -DryRun
/wsm-profile -Json
```

## Expected output

```
Name                                 Count     Sum_s        Avg_s
----                                 -----     -----        -----
GenerateNavmesh                        1      12.834000     12.834000
LoadWorld                             1       8.102000      8.102000

Overall total: 24.001100 s
```

Use `/wsm-profile -DryRun` to analyze existing logs when the game is not running.
