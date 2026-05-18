---
description: Install WorldSphereMod3D DLL to game (no launch)
---

# wsm-install

Deploy the compiled DLL to the game directory.

## What This Does

1. Validates the DLL was built
2. Cleans up any stale DLLs from previous .NET versions
3. Copies DLL to `BepInEx/plugins/`
4. Verifies deployment hash

Does NOT launch the game. Use `/wsm-relaunch` to launch after installing.

## Steps

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
& pwsh -File "$wsmRoot/Tools/wsm3d.ps1" install
```

## Expected output

```
Installing WorldSphereMod3D...
✓ DLL deployed to G:\SteamLibrary\...\BepInEx\plugins\WorldSphereMod.dll
✓ Hash: abc123def456...
✓ Stale DLLs cleaned (0 files removed)
Installation complete. Use /wsm-relaunch to launch.
```

If the game is running, stop it first with `/wsm-kill` before installing.
