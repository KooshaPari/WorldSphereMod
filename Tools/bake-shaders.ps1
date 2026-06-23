#!/usr/bin/env pwsh
# WSM3D Shader AssetBundle Bake — fully automated, no UI clicks.
# Requires Unity 2022.3.60f1 — the EXACT WorldBox runtime build (confirmed from
# worldbox_Data/globalgamemanagers: m_UnityVersion 2022.3.60f1). Shader assets
# baked with a different 2022.3 patch (e.g. 62f3) deserialize at WorldBox runtime
# with an EMPTY Shader.name and trigger the native ManagedStream crash
# ("Mismatched serialization in builtin class 'Shader'") — see ADR-0013. The
# SerializedShader binary layout is NOT stable across 2022.3 patch releases, so
# the bake editor must match the game's patch exactly. Pass -UnityExe when
# auto-detect fails.
#
# P2: BakeShaders.cs also copies + tags the GPU-compute keystone
# External/Compound-Spheres/Default Assets/CompoundSphereCompute.compute into the
# wsm3d-shaders bundle so ManagerBase<T>.Init() can FindKernel(OutputMatrices /
# OutputColors) at runtime (the bake previously globbed *.shader only).
#
# Shader sources under WorldSphereMod/AssetBundles/Shaders/ and Resources/Shaders/
# (BrpBloom, BrpACES, ScreenSpaceGI, ProceduralSky, ScreenSpaceAO) must stay
# BRP-compatible for bundle bake:
# use built-in Fallback shaders (Diffuse, Skybox/Procedural, Unlit/Color), explicit
# vert/frag instead of vert_img where depth/RT flip matters, and avoid uniform
# float4[] / static const arrays in CGPROGRAM — corrupted compiles load with
# empty .name at runtime. Rebake requires Unity 2022.3 at a standard Hub path.

param(
    [string]$UnityExe = ""
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$BakeProject = Join-Path $RepoRoot 'Tools/Unity-Bake-Project'
$LogFile = Join-Path $RepoRoot 'Tools/bake-shaders.log'
$ProjectVersionFile = Join-Path $BakeProject 'ProjectSettings/ProjectVersion.txt'

function Write-BakeNextSteps {
    param([string]$Reason)
    Write-Output ""
    Write-Output "[bake] $Reason"
    Write-Output ""
    Write-Output "Next steps:"
    Write-Output "  1. Install Unity 2022.3.60f1 via Unity Hub — the EXACT WorldBox runtime build."
    Write-Output "       Headless: & `"`$env:ProgramFiles\Unity Hub\Unity Hub.exe`" -- --headless install --version 2022.3.60f1 --changeset 5f63fdee6d95"
    Write-Output "  2. Open Tools/Unity-Bake-Project once in Hub so ProjectVersion.txt uses 2022.3.60f1."
    Write-Output "  3. Re-run with an explicit editor path, for example:"
    Write-Output "       pwsh Tools/bake-shaders.ps1 -UnityExe `"`$env:ProgramFiles\Unity\Hub\Editor\2022.3.60f1\Editor\Unity.exe`""
    Write-Output "  4. Confirm Tools/bake-shaders.log ends with [WSM3D-Bake] success and bundles under WorldSphereMod/AssetBundles/."
    Write-Output ""
    Write-Output "See docs/journeys/scratch/unity-version-blocker.md for the full checklist."
}

function Test-ProjectVersionRecommends2022 {
    if (-not (Test-Path $ProjectVersionFile)) {
        Write-Output "[bake] WARN: ProjectVersion.txt not found at $ProjectVersionFile"
        return $false
    }
    $line = Get-Content $ProjectVersionFile | Where-Object { $_ -match '^m_EditorVersion:\s*(\S+)' } | Select-Object -First 1
    if (-not $line) {
        Write-Output "[bake] WARN: ProjectVersion.txt has no m_EditorVersion line"
        return $false
    }
    $version = if ($line -match ':\s*(\S+)') { $Matches[1] } else { $line }
    if ($version -eq '2022.3.60f1') {
        return $true
    }
    if ($line -match '2022\.3\.') {
        Write-Output "[bake] WARN: Bake project targets $version, but WorldBox runs 2022.3.60f1 EXACTLY."
        Write-Output "[bake]       Shader assets baked with a different 2022.3 patch deserialize with an empty"
        Write-Output "[bake]       Shader.name at runtime (native ManagedStream crash, ADR-0013). Set"
        Write-Output "[bake]       ProjectVersion.txt to 2022.3.60f1 and bake with that exact editor."
        return $false
    }
    Write-Output "[bake] WARN: Bake project targets $version — WorldBox needs bundles built with Unity 2022.3.60f1."
    Write-Output "[bake]       Open Tools/Unity-Bake-Project in Unity Hub 2022.3.60f1 to upgrade ProjectVersion.txt, then re-run."
    return $false
}

# Auto-locate Unity 2022.3 install (WorldBox runtime family)
if ([string]::IsNullOrEmpty($UnityExe)) {
    # Prefer the EXACT WorldBox runtime build first; a different 2022.3 patch bakes
    # shaders that deserialize with an empty name at runtime (ADR-0013).
    $candidates = @(
        "${env:ProgramFiles}\Unity\Hub\Editor\2022.3.60f1\Editor\Unity.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { $UnityExe = $c; break } }

    if ([string]::IsNullOrEmpty($UnityExe)) {
        $editorRoot = "${env:ProgramFiles}\Unity\Hub\Editor"
        if (Test-Path $editorRoot) {
            # Exact-match 2022.3.60f1 wins; otherwise fall back to the newest 2022.3.x
            # but warn loudly below — a patch mismatch is the empty-name root cause.
            $found = Get-ChildItem $editorRoot -Directory |
                Where-Object { $_.Name -eq '2022.3.60f1' } |
                Select-Object -First 1
            if (-not $found) {
                $found = Get-ChildItem $editorRoot -Directory |
                    Where-Object { $_.Name -match '^2022\.3\.' } |
                    Sort-Object Name -Descending |
                    Select-Object -First 1
            }
            if ($found) {
                $UnityExe = Join-Path $found.FullName 'Editor\Unity.exe'
            }
        }
    }
}

if ([string]::IsNullOrEmpty($UnityExe) -or -not (Test-Path $UnityExe)) {
    Write-BakeNextSteps -Reason "Unity 2022.3.60f1 not found. Pass -UnityExe '<path-to-Unity.exe>'."
    exit 1
}

if ($UnityExe -notmatch '\\2022\.3\.60f1\\') {
    Write-Output "[bake] WARN: Selected editor is NOT 2022.3.60f1: $UnityExe"
    Write-Output "[bake]       WorldBox runs 2022.3.60f1 exactly. Shaders baked with a different patch"
    Write-Output "[bake]       deserialize with an empty Shader.name at runtime (ADR-0013 ManagedStream crash)."
}

$projectOk = Test-ProjectVersionRecommends2022
if (-not $projectOk) {
    Write-Output "[bake]       Continuing anyway; fix ProjectVersion.txt before shipping bundles."
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
    if (Test-Path $LogFile) {
        Get-Content $LogFile -Tail 30 | ForEach-Object { Write-Output "  $_" }
    }
    Write-BakeNextSteps -Reason "Unity bake failed (exit $($proc.ExitCode))."
    exit $proc.ExitCode
}

Write-Output "[bake] OK. Tail of log:"
Get-Content $LogFile -Tail 12 | ForEach-Object { Write-Output "  $_" }

Write-Output ""
Write-Output "[bake] Bundles in:"
$assetBundlesPath = Join-Path $RepoRoot 'WorldSphereMod/AssetBundles'
Get-ChildItem -Path "$assetBundlesPath\*" -Include 'worldsphere','wsm3d-shaders' -Recurse -File |
    ForEach-Object { Write-Output "  $($_.FullName) — $($_.Length) bytes" }
