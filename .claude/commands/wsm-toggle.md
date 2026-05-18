---
description: Toggle a 3D conversion phase on/off
argument-hint: <phase slug> (required)
---

# wsm-toggle

Enable or disable a phase in the running mod.

## What This Does

Toggles a phase feature on/off without restarting the game.
Requires the phase slug as an argument.

After toggling, you must reload the world in-game (Options > Reload World or new save).

## Supported Phases

- `phase-1` — 3D voxel buildings
- `phase-2` — Procedural building geometry
- `phase-3` — Complex building shapes
- `phase-4` — Water rendering
- `phase-5` — Lighting & shadows
- `phase-6` — Skeletal animation
- `phase-7` — Worldspace UI
- `phase-8` — Day/night cycle
- `phase-9` — Decals
- `phase-10` — LOD & culling

## Steps

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($ARGS.Count -eq 0) {
    Write-Error "Usage: /wsm-toggle <phase slug>"
    exit 1
}
$phase = $ARGS[0]
& pwsh -File "$wsmRoot/Tools/wsm3d.ps1" toggle -Phase $phase
```

## Usage

```
/wsm-toggle phase-1      # Toggle voxel buildings
/wsm-toggle phase-2      # Toggle procedural buildings
/wsm-toggle phase-5      # Toggle lighting & shadows
```

## Expected output

```
Toggling phase-1...
✓ phase-1 is now ENABLED
ℹ Reload world in-game to apply changes (Options > Reload World)
```

After toggling, reload your world or start a new game for the change to take effect.
