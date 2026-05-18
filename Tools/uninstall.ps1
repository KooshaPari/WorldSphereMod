<#
.SYNOPSIS
  Remove the installed copy of WorldSphereMod3D from the WorldBox Mods folder.

.DESCRIPTION
  Inverse of install.ps1. Safe to run if not installed (no-ops).
  Does NOT touch upstream's THE_3D_WORLDBOX_MOD folder.

.EXAMPLE
  ./Tools/uninstall.ps1

.EXAMPLE
  ./Tools/uninstall.ps1 -WorldBoxPath "D:/Games/Worldbox"
#>

[CmdletBinding()]
param(
    [string]$WorldBoxPath = $(if ($env:WORLDBOX_PATH) { $env:WORLDBOX_PATH } else { "C:/Program Files (x86)/Steam/steamapps/common/Worldbox" }),
    [string]$InstallFolderName = "WorldSphereMod3D"
)

$ErrorActionPreference = "Stop"

$modDst = Join-Path $WorldBoxPath "Mods/$InstallFolderName"

if (Test-Path $modDst) {
    Write-Host "[uninstall] removing $modDst" -ForegroundColor Cyan
    Remove-Item -Recurse -Force $modDst
    Write-Host "[uninstall] done" -ForegroundColor Green
} else {
    Write-Host "[uninstall] nothing to remove at $modDst" -ForegroundColor DarkGray
}
