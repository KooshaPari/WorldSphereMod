[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TestRoot,
    [string]$InstallScript = (Join-Path $PSScriptRoot 'install.ps1')
)
$ErrorActionPreference = 'Stop'
if (-not [IO.Path]::IsPathFullyQualified($TestRoot)) { throw 'Explicit absolute TestRoot required.' }
$sandbox = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE 'agents/sandbox')).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
$root = [IO.Path]::GetFullPath($TestRoot).TrimEnd('\','/')
if (-not $root.StartsWith($sandbox, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)) { throw 'TestRoot must be a new unique directory under agents/sandbox.' }
New-Item -ItemType Directory -Path $root | Out-Null
function Assert-FixturePath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Fixture path escaped: $full" }
}
function Write-FixtureFile([string]$Path, [string]$Content) {
    Assert-FixturePath $Path
    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Content)
}
function Get-FixtureHashes([string]$Path) {
    @((Get-ChildItem -LiteralPath $Path -Recurse -File) | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($Path, $_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash
    }) -join "`n"
}
function robocopy([string]$source, [string]$destination) {
    Assert-FixturePath $source
    Assert-FixturePath $destination
    if ($fixtureMode -eq 'copy') { $global:LASTEXITCODE = 8; return }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Force | ForEach-Object { Microsoft.PowerShell.Management\Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force }
    $global:LASTEXITCODE = 0
}
function Move-Item {
    param([string]$LiteralPath, [string]$Destination)
    Assert-FixturePath $LiteralPath
    Assert-FixturePath $Destination
    if ($fixtureMode -eq 'swap' -and (Split-Path $LiteralPath -Leaf) -eq 'new') { throw 'Injected promotion failure.' }
    Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination
}
$results = @()
foreach ($fixtureMode in @('copy','validation','swap','success','fresh','precompiled','missing-dll','linked-data','linked-source')) {
    $case = Join-Path $root $fixtureMode
    $repo = Join-Path $case 'repo'
    $game = Join-Path $case 'game'
    $target = Join-Path $game 'worldbox_Data/StreamingAssets/Mods/WorldSphereMod'
    Write-FixtureFile (Join-Path $repo 'Tools/install.ps1') ([IO.File]::ReadAllText($InstallScript))
    Write-FixtureFile (Join-Path $repo 'WorldSphereMod/Code/Example.cs') 'new-source'
    Write-FixtureFile (Join-Path $repo 'WorldSphereMod/Assemblies/CompoundSpheres.dll') 'fixture-dependency'
    $guid = if ($fixtureMode -eq 'validation') { 'wrong.guid' } else { 'worldsphere3d.fork' }
    Write-FixtureFile (Join-Path $repo 'WorldSphereMod/mod.json') ('{"GUID":"' + $guid + '"}')
    Write-FixtureFile (Join-Path $repo 'bin/Release/net48/WorldSphereMod3D.dll') 'fixture-precompiled'
    if ($fixtureMode -ne 'fresh') {
        Write-FixtureFile (Join-Path $target 'original.txt') 'original-mod'
        Write-FixtureFile (Join-Path $target 'config/settings.json') '{"keep":true}'
        Write-FixtureFile (Join-Path $target 'data/world.dat') 'never-delete'
    } else { New-Item -ItemType Directory -Path (Join-Path $game 'worldbox_Data') -Force | Out-Null }
    Write-FixtureFile (Join-Path $game 'user-saves/keep.wbox') 'outside-mod-data'
    if ($fixtureMode -in @('linked-data','linked-source')) {
        $linkParent = if ($fixtureMode -eq 'linked-data') { $target } else { Join-Path $repo 'WorldSphereMod/Code' }
        New-Item -ItemType Junction -Path (Join-Path $linkParent 'linked') -Target (Join-Path $game 'user-saves') | Out-Null
    }
    $before = if (Test-Path -LiteralPath $target) { Get-FixtureHashes $target } else { $null }
    $saveHash = Get-FixtureHashes (Join-Path $game 'user-saves')
    $threw = $false
    try {
        $options = @{WorldBoxPath=$game; SkipBuild=$true}
        if ($fixtureMode -in @('precompiled','missing-dll')) { $options.Precompiled=$true }
        if ($fixtureMode -eq 'missing-dll') { $options.AssemblyName='Missing' }
        & (Join-Path $repo 'Tools/install.ps1') @options | Out-Null
    } catch { $threw = $true }
    $expectFailure = $fixtureMode -in @('copy','validation','swap','missing-dll','linked-data','linked-source')
    if ($threw -ne $expectFailure) { throw "Unexpected failure outcome: $fixtureMode threw=$threw" }
    if ($expectFailure -and (Get-FixtureHashes $target) -ne $before) { throw "Original mod/config/data changed on $fixtureMode failure." }
    if ((Get-FixtureHashes (Join-Path $game 'user-saves')) -ne $saveHash) { throw 'External save changed.' }
    if (-not $expectFailure) {
        if ($fixtureMode -ne 'fresh' -and [IO.File]::ReadAllText((Join-Path $target 'data/world.dat')) -ne 'never-delete') { throw 'User data lost on success.' }
        if ($fixtureMode -ne 'fresh' -and [IO.File]::ReadAllText((Join-Path $target 'config/settings.json')) -ne '{"keep":true}') { throw 'User config lost on success.' }
        if ($fixtureMode -eq 'precompiled') {
            if ((Test-Path -LiteralPath (Join-Path $target 'Code')) -or [IO.File]::ReadAllText((Join-Path $target 'WorldSphereMod3D.dll')) -ne 'fixture-precompiled') { throw 'Precompiled layout mismatch.' }
        } elseif ([IO.File]::ReadAllText((Join-Path $target 'Code/Example.cs')) -ne 'new-source') { throw 'New source missing.' }
    }
    $results += [pscustomobject]@{case=$fixtureMode;pass=$true;expectedFailure=$expectFailure}
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'results.json')
Write-Output "PASS $($results.Count) preserve-first installer fixtures; all artifacts retained at $root"
