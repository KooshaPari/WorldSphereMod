---
description: Diagnostic check of WorldSphereMod3D environment
---

# wsm-doctor

Verify all dependencies and game setup are correct.

## What This Does

Checks:
- (a) `dotnet` CLI on PATH
- (b) Steam install path accessible
- (c) Mods folder writable
- (d) `Player.log` readable
- (e) BepInEx + NML loaded in game
- (f) `gh` + `vercel` CLI versions
- (g) `phenotype-journey` on PATH (warn if missing)

Reports findings and remediation steps if issues are found.

## Steps

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$checks = @{
    "dotnet"        = { (dotnet --version) -match "^\d+\.\d+" }
    "steam-path"    = { Test-Path "G:\SteamLibrary\steamapps\common\WorldBox" }
    "mods-writable" = { Test-Path "$env:APPDATA\Balatro" -PathType Container }
    "player-log"    = { Test-Path "G:\SteamLibrary\steamapps\common\WorldBox\Player.log" }
    "gh"            = { (gh --version) -match "gh version" }
    "vercel"        = { (vercel --version) -match "Vercel" }
    "phenotype-j"   = { (Get-Command phenotype-journey -ErrorAction SilentlyContinue) -ne $null }
}

$failed = @()
foreach ($check in $checks.Keys) {
    try {
        $result = & $checks[$check]
        if ($result) {
            Write-Host "✓ $check"
        } else {
            Write-Host "✗ $check"
            $failed += $check
        }
    } catch {
        Write-Host "✗ $check (error: $_)"
        $failed += $check
    }
}

if ($failed.Count -gt 0) {
    Write-Host "`nFailed checks: $($failed -join ', ')"
    Write-Host "Run /wsm-doctor again after fixing issues."
    exit 1
}
Write-Host "`n✓ All checks passed. Ready to build."
```

## Expected output (all pass)

```
Diagnostic Check
================
✓ dotnet          (8.0.100)
✓ steam-path      (G:\SteamLibrary\...)
✓ mods-writable   (writable)
✓ player-log      (accessible)
✓ gh              (2.35.0)
✓ vercel          (32.1.0)
⚠ phenotype-journey (NOT FOUND - optional, install with: npm install -g @phenotype-org/phenotype-journey)

✓ All checks passed. Ready to build.
```

## Expected output (one check fails)

```
Diagnostic Check
================
✓ dotnet
✗ steam-path     (path not found)
✓ mods-writable
...

Failed checks: steam-path
Remediation:
  - Verify Steam is installed at G:\SteamLibrary
  - Or update the path in .claude/settings.json
  - Run /wsm-doctor again after fixing

Run /wsm-doctor again after fixing issues.
```

Run this whenever the build fails mysteriously or before deploying to a new machine.
