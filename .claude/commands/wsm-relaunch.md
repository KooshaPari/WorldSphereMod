---
description: Kill game, reinstall, and relaunch (destructive - confirm first)
---

# wsm-relaunch

Full restart cycle: kill → reinstall → launch → tail log.

## ⚠️ Warning

This command is destructive. It will:
- Kill all running game instances
- Deploy the latest DLL
- Launch a fresh game
- Monitor the log for 5 seconds

You will be asked to confirm before proceeding.

## What This Does

1. Prompts for confirmation (destructive)
2. Kills existing game processes
3. Reinstalls the DLL
4. Launches the game
5. Tails the log for 5 seconds looking for `[WSM3D]` errors

## Steps

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$confirm = Read-Host "Kill game, reinstall, and launch? (yes/no)"
if ($confirm -eq "yes") {
    & pwsh -File "$wsmRoot/Tools/wsm3d.ps1" kill
    Start-Sleep -Seconds 2
    & pwsh -File "$wsmRoot/Tools/wsm3d.ps1" install
    & pwsh -File "$wsmRoot/Tools/wsm3d.ps1" launch
    Start-Sleep -Seconds 3
    & pwsh -File "$wsmRoot/Tools/wsm3d.ps1" log -Tail 50 -Grep WSM3D
}
```

## Expected output

```
Kill game, reinstall, and launch? (yes/no): yes
✓ Game process killed (PID 8492)
✓ DLL installed (hash: abc123...)
✓ Game launched (PID 9205)
Waiting for log initialization...
[WSM3D] Bootstrap: DLL loaded
[WSM3D] Phase 1: Initialization complete
✓ No errors detected in first 5 seconds.
```

After the command finishes, switch to the game window to interact with the mod.
