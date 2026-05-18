---
description: Check build/install/game state of WorldSphereMod3D
---

# wsm-status

Quick diagnostic of the mod installation and game state.

## What This Does

Calls `wsm3d status` to report:
- Build status and last rebuild time
- Installation status and deployed DLL hash
- Running game instance (PID, loaded phases)
- Last few log entries tagged with `[WSM3D]`

## Steps

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
& pwsh -File "$wsmRoot/Tools/wsm3d.ps1" status
```

## Expected output

```
BUILD: ✓ Release (2026-05-18 14:32:15)
INSTALL: ✓ G:\SteamLibrary\...\BepInEx\plugins\WorldSphereMod.dll (hash: abc123...)
GAME: ✓ Running (PID 8492)
  Loaded phases: phase-1, phase-2-geometry
LOG (last 5 [WSM3D] lines):
  [WSM3D] Bootstrap: DLL loaded
  [WSM3D] Phase 1: Initialized geometry system
```

Use this before attempting to launch, build, or toggle phases.
