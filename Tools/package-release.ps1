# WorldSphereMod Installer Package Builder
# Creates a ZIP that users extract into their WorldBox Mods folder.
# NeoModLoader compiles Code/*.cs at runtime, so this ships source.

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $repoRoot "VERSION")).Trim()
$stageDir = Join-Path $repoRoot "_stage/WorldSphereMod"
$outputZip = Join-Path $repoRoot "_stage/WorldSphereMod-v$version-win.zip"

Write-Host "=== Building WorldSphereMod v$version installer package ===" -ForegroundColor Cyan

# Clean staging
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

# Copy mod source (NML compiles this at runtime)
$modSrc = Join-Path $repoRoot "WorldSphereMod"
Copy-Item $modSrc -Destination $stageDir -Recurse -Exclude "__pycache__"

# Copy game resources (icons, textures, phase icons)
$grSrc = Join-Path $repoRoot "WorldSphereMod/GameResources"
if (Test-Path $grSrc) {
    $grDst = Join-Path $stageDir "GameResources"
    Copy-Item $grSrc -Destination $grDst -Recurse
}

# Copy install script
Copy-Item (Join-Path $repoRoot "Tools/install.ps1") -Destination $stageDir

# Copy uninstall script
Copy-Item (Join-Path $repoRoot "Tools/uninstall.ps1") -Destination $stageDir -ErrorAction SilentlyContinue

# Create README for the package
@"
# WorldSphereMod v$version

## Quick Install (PowerShell)

```powershell
# From this directory:
./install.ps1
```

Or with a custom WorldBox path:
```powershell
./install.ps1 -WorldBoxPath "D:/Games/Worldbox"
```

## Manual Install

1. Copy this entire folder to:
   \`C:/Program Files (x86)/Steam/steamapps/common/Worldbox/worldbox_Data/StreamingAssets/Mods/WorldSphereMod\`
2. Restart WorldBox
3. NeoModLoader will compile and load the mod automatically

## Uninstall

```powershell
./uninstall.ps1
```

Or delete the WorldSphereMod folder from WorldBox's Mods directory.

## Requirements

- WorldBox (Steam)
- NeoModLoader (NML) installed in WorldBox
- .NET Framework 4.8 (for NML compilation)

## Links

- Source: https://github.com/KooshaPari/WorldSphereMod
- Issues: https://github.com/KooshaPari/WorldSphereMod/issues
"@ | Set-Content (Join-Path $stageDir "INSTALL.md")

# Create ZIP
if (Test-Path $outputZip) { Remove-Item $outputZip -Force }
Compress-Archive -Path $stageDir -DestinationPath $outputZip

Write-Host "=== Package created: $outputZip ===" -ForegroundColor Green
Write-Host "Size: $([math]::Round((Get-Item $outputZip).Length / 1MB, 2)) MB" -ForegroundColor Green
