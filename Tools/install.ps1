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
    [string]$Tfm = "net48",
    [string]$AssemblyName = "WorldSphereMod3D",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Write-InstallFailureHint {
    Write-Host "[install] For environment diagnostics, run: pwsh Tools/wsm3d.ps1 doctor" -ForegroundColor Yellow
}

try {
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

# --- Wipe prior install (retry loop for Windows lock / antivirus contention) ---
if (Test-Path $modDst) {
    Write-Host "[install] removing prior install at $modDst" -ForegroundColor DarkGray
    $retries = 3
    for ($attempt = 1; $attempt -le $retries; $attempt++) {
        Remove-Item -Recurse -Force $modDst -ErrorAction SilentlyContinue
        if (-not (Test-Path $modDst)) { break }
        if ($attempt -lt $retries) {
            Write-Host "[install]   removal incomplete (locked handles?), retry $attempt/$retries..." -ForegroundColor Yellow
            Start-Sleep -Seconds 1
        } else {
            throw "Could not fully remove $modDst after $retries attempts. Close WorldBox / antivirus and retry."
        }
    }
}
New-Item -ItemType Directory -Force -Path $modDst | Out-Null

# --- Copy every item fresh via robocopy /MIR (dirs) or Copy-Item (files) ---
# robocopy /MIR guarantees a byte-identical mirror regardless of timestamps,
# file locks on the *source* side, or Copy-Item merge-vs-replace quirks.
$items = @("Code", "Assemblies", "AssetBundles", "GameResources", "Locales", "mod.json")
foreach ($item in $items) {
    $src = Join-Path $modSrc $item
    if (Test-Path $src) {
        Write-Host "[install]   $item" -ForegroundColor DarkCyan
        if ((Get-Item $src) -is [System.IO.DirectoryInfo]) {
            $dst = Join-Path $modDst $item
            # robocopy exit codes 0-7 are success (bitmask); 8+ are errors
            & robocopy $src $dst /MIR /NJH /NJS /NP /NFL /NDL /R:2 /W:1 | Out-Null
            $roboExit = $LASTEXITCODE
            if ($roboExit -ge 8) {
                throw "robocopy failed copying $item (exit $roboExit)"
            }
            # Reset so downstream callers don't see robocopy's non-zero success codes
            $global:LASTEXITCODE = 0
        } else {
            Copy-Item -Force -Path $src -Destination $modDst
        }
    } else {
        Write-Host "[install]   $item (skipped — not present in source)" -ForegroundColor DarkGray
    }
}

# --- Verify critical artifacts landed ---
$csDst = Join-Path $modDst "Code"
$csCount = (Get-ChildItem -Path $csDst -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue).Count
$csSrc  = (Get-ChildItem -Path (Join-Path $modSrc "Code") -Filter "*.cs" -Recurse).Count
if ($csCount -ne $csSrc) {
    throw "[install] MISMATCH: source has $csSrc .cs files but destination has $csCount"
}
Write-Host "[install]   verified $csCount .cs files" -ForegroundColor DarkGreen

$compoundDll = Join-Path $modDst "Assemblies/CompoundSpheres.dll"
if (-not (Test-Path $compoundDll)) {
    throw "[install] CompoundSpheres.dll missing from installed Assemblies — NML will fail with CS0246"
}

$bundleDir = Join-Path $modDst "AssetBundles"
if ((Test-Path (Join-Path $modSrc "AssetBundles")) -and -not (Test-Path $bundleDir)) {
    throw "[install] AssetBundles directory missing from install"
}

# CompoundSpheres.dll IS a real runtime dependency (Code/ references its
# types: SphereTile / SphereManager / SphereManagerSettings / IBufferData /
# IncompatibleHardwareException). Removing it makes NML's Roslyn compile
# fail with ~60 CS0246 errors. Leave it in place.
#
# DO NOT ship bin/Release/net48/WorldSphereMod3D.dll alongside Code/.
# Observed failure: NML loads the DLL as a reference AND Roslyn-compiles
# Code/, which produces duplicate-type CS0121 errors on every Tools.* call
# ("ambiguous between WorldSphereMod.Tools.X and WorldSphereMod.Tools.X").
# Code/ is the source of truth; NML's Roslyn pass owns the compile. Costs
# ~1s extra startup but works. Re-enable the DLL copy only after a future
# investigation figures out how to make NML treat the DLL as "the mod"
# instead of just another reference.
# See docs/adr/ADR-0007-nml-precompiled-detection.md before changing DLL shipping behavior.
$installedAssemblies = Join-Path $modDst "Assemblies"
if (Test-Path $builtDll) {
    Write-Host "[install] skipping $AssemblyName.dll copy (NML double-loads it + Code/ → CS0121). See install.ps1 comment." -ForegroundColor DarkYellow
}

# Defensive: remove any stale WSM3D DLL that a prior install left in the
# game folder. Without this, a stale net48 DLL would keep colliding even
# after the install.ps1 change is in effect.
$staleSelfDll = Join-Path $installedAssemblies "$AssemblyName.dll"
$staleSelfPdb = Join-Path $installedAssemblies "$AssemblyName.pdb"
if (Test-Path $staleSelfDll) { Remove-Item -Force $staleSelfDll }
if (Test-Path $staleSelfPdb) { Remove-Item -Force $staleSelfPdb }

Write-Host ""
Write-Host "[install] installed to $modDst" -ForegroundColor Green
Write-Host "[install] launch WorldBox; NeoModLoader will Roslyn-compile Code/*.cs on startup (~1s)."
Write-Host "[install] verify in-game: WorldSphere tab -> '3D Phases' window. Phase 1 = 'Voxel Actors' toggle."
} catch {
    Write-InstallFailureHint
    throw
}
