#!/usr/bin/env pwsh
# WSM3D Shader AssetBundle Bake — fully automated, no UI clicks.
# Requires Unity 6.3 installed via Unity Hub.

[CmdletBinding()]
param(
    [string]$UnityExe = "",
    [switch]$Verbose
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$BakeProject = Join-Path $RepoRoot 'Tools/Unity-Bake-Project'
$LogFile = Join-Path $RepoRoot 'Tools/bake-shaders.log'

# Auto-locate Unity 6.3 install
if ([string]::IsNullOrEmpty($UnityExe)) {
    $candidates = @(
        "${env:ProgramFiles}\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe",
        "${env:ProgramFiles}\Unity\Hub\Editor\6000.3.0\Editor\Unity.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { $UnityExe = $c; break } }

    if ([string]::IsNullOrEmpty($UnityExe)) {
        $editorRoot = "${env:ProgramFiles}\Unity\Hub\Editor"
        if (Test-Path $editorRoot) {
            $found = Get-ChildItem $editorRoot -Directory | Where-Object { $_.Name -match '^6000\.3\.' } | Select-Object -First 1
            if ($found) {
                $UnityExe = Join-Path $found.FullName 'Editor\Unity.exe'
            }
        }
    }
}

if (-not (Test-Path $UnityExe)) {
    Write-Error "Unity 6.3 not found. Pass -UnityExe '<path-to-Unity.exe>' or install Unity 6.3 via Unity Hub."
    exit 1
}

Write-Output "[bake] Unity: $UnityExe"
Write-Output "[bake] Project: $BakeProject"
Write-Output "[bake] Log: $LogFile"

$args = @(
    '-batchmode',
    '-nographics',
    '-quit',
    '-projectPath', $BakeProject,
    '-executeMethod', 'BakeShaders.BakeAll',
    '-logFile', $LogFile
)

Write-Output "[bake] Running Unity headless bake..."
$proc = Start-Process -FilePath $UnityExe -ArgumentList $args -NoNewWindow -PassThru -Wait
if ($proc.ExitCode -ne 0) {
    Write-Output "[bake] EXIT $($proc.ExitCode). Tail of log:"
    Get-Content $LogFile -Tail 30 | ForEach-Object { Write-Output "  $_" }
    exit $proc.ExitCode
}

Write-Output "[bake] OK. Tail of log:"
Get-Content $LogFile -Tail 12 | ForEach-Object { Write-Output "  $_" }

Write-Output ""
Write-Output "[bake] Bundles in:"
Get-ChildItem (Join-Path $RepoRoot 'WorldSphereMod/AssetBundles') -Filter 'worldsphere' -Recurse |
    ForEach-Object { Write-Output "  $($_.FullName) — $($_.Length) bytes" }
