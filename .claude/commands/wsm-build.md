---
description: Build WorldSphereMod3D and report DLL size + warning count
---

# wsm-build

Compile the mod in Release mode and assess build health.

## What This Does

1. Runs `dotnet build` in Release configuration
2. Reports compiled DLL size and warning count
3. Flags any new compiler errors
4. Shows build time

## Steps

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
& pwsh -File "$wsmRoot/Tools/wsm3d.ps1" build -Configuration Release
```

## Expected output

```
Building WorldSphereMod3D (Release)...
✓ Build succeeded in 45.2s
  DLL size: 2.1 MB (WorldSphereMod.dll)
  Warnings: 0
  Errors: 0
Ready to install.
```

If warnings are reported, review them with the compiler output. Errors block installation until fixed.
