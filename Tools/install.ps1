<#
.SYNOPSIS
  Build the mod and copy it into the WorldBox Mods folder so NeoModLoader can pick it up.

.DESCRIPTION
  NeoModLoader compiles Code/*.cs at runtime, so the install copies source
  (not the built DLL). The dotnet build runs first only as a sanity check —
  if the source doesn't compile via dotnet, NML's Roslyn pass will also fail.

  Default WorldBox path: C:/Program Files (x86)/Steam/steamapps/common/Worldbox/
  Override with -WorldBoxPath or $env:WORLDBOX_PATH.

  Default install folder name: WorldSphereMod3D (matches mod.json GUID family).

.EXAMPLE
  ./Tools/install.ps1
  Builds and installs to the default WorldBox path.

.EXAMPLE
  ./Tools/install.ps1 -SkipBuild
  Skips the dotnet sanity build and just copies sources.

.EXAMPLE
  ./Tools/install.ps1 -WorldBoxPath "D:/Games/Worldbox"
  Installs into a non-default WorldBox location.
#>

[CmdletBinding()]
param(
    [string]$WorldBoxPath = $(if ($env:WORLDBOX_PATH) { $env:WORLDBOX_PATH } else { "C:/Program Files (x86)/Steam/steamapps/common/Worldbox" }),
    [string]$InstallFolderName = "WorldSphereMod3D",
    [string]$Configuration = "Release",
    [string]$Tfm = "net5.0",
    [string]$AssemblyName = "WorldSphereMod3D",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$modSrc   = Join-Path $repoRoot "WorldSphereMod"
$modDst   = Join-Path $WorldBoxPath "Mods/$InstallFolderName"
$builtDll = Join-Path $repoRoot "bin/$Configuration/$Tfm/$AssemblyName.dll"

if (-not (Test-Path (Join-Path $WorldBoxPath "worldbox_Data"))) {
    throw "WorldBox not found at $WorldBoxPath (no worldbox_Data subfolder). Pass -WorldBoxPath or set `$env:WORLDBOX_PATH."
}
if (-not (Test-Path $modSrc)) {
    throw "Mod source folder not found at $modSrc"
}

if (-not $SkipBuild) {
    Write-Host "[install] dotnet build (sanity check) ..." -ForegroundColor Cyan
    Push-Location $repoRoot
    try {
        & dotnet build WorldSphereMod.csproj -c Release | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed; aborting install." }
    } finally {
        Pop-Location
    }
}

if (Test-Path $modDst) {
    Write-Host "[install] removing prior install at $modDst" -ForegroundColor DarkGray
    Remove-Item -Recurse -Force $modDst
}
New-Item -ItemType Directory -Force -Path $modDst | Out-Null

$items = @("Code", "Assemblies", "AssetBundles", "GameResources", "Locales", "mod.json")
foreach ($item in $items) {
    $src = Join-Path $modSrc $item
    if (Test-Path $src) {
        Write-Host "[install]   $item" -ForegroundColor DarkCyan
        Copy-Item -Recurse -Force -Path $src -Destination $modDst
    } else {
        Write-Host "[install]   $item (skipped — not present in source)" -ForegroundColor DarkGray
    }
}

# Note: CompoundSpheres.dll IS a real runtime dependency, not just a legacy
# upstream artifact. Our Code/ has `using CompoundSpheres;` and references
# SphereTile / SphereManager / SphereManagerSettings / IBufferData /
# IncompatibleHardwareException — all of which live in that DLL. Removing
# it makes NML's Roslyn compile fail with ~60 CS0246 errors and the mod
# silently never initializes. Leave it in place.
#
# The net5.0 fork DLL drop (commented out below) is the genuinely unloadable
# one — Mono rejects with CS1705 "System.Runtime 5.0 vs 4.1". Re-enable
# once the csproj is retargeted to net48.
$installedAssemblies = Join-Path $modDst "Assemblies"
if (Test-Path $builtDll) {
    Write-Host "[install] skipping $AssemblyName.dll copy (net5.0 build is unloadable by Mono — NML will Roslyn-compile Code/)." -ForegroundColor DarkYellow
}

Write-Host ""
Write-Host "[install] installed to $modDst" -ForegroundColor Green
Write-Host "[install] launch WorldBox; NeoModLoader will compile Code/*.cs on startup (~1s)."
Write-Host "[install] verify in-game: WorldSphere tab -> '3D Phases' window. Phase 1 = 'Voxel Actors' toggle."
