#!/usr/bin/env pwsh
# Interactive helper for HANDOFF in-game smoke (phases 1–10).
# Automates install/launch/PlayCUA baselines; human still judges visuals.

param(
    [int[]]$Phase = @(1),
    [switch]$Install,
    [switch]$Launch,
    [switch]$PlaycuaBaseline,
    [switch]$OpenChecklists,
    [switch]$AllPhases
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$DocsScreenshots = Join-Path $RepoRoot 'docs/screenshots'
$SmokeIndex = Join-Path $RepoRoot 'docs/smoke-test-index.md'

if ($AllPhases) {
    $Phase = 1..10
}

function Get-PhaseMeta([int]$n) {
    $map = @{
        1  = @{ Slug = 'voxel-actors'; Flag = 'VoxelEntities'; Yaml = 'phase-1-voxel-actors.yaml' }
        2  = @{ Slug = 'procedural-buildings'; Flag = 'ProceduralBuildings'; Yaml = 'phase-2-procedural-buildings.yaml' }
        3  = @{ Slug = 'crossed-quad-foliage'; Flag = 'CrossedQuadFoliage'; Yaml = 'phase-3-crossed-quad-foliage.yaml' }
        4  = @{ Slug = 'mesh-water'; Flag = 'MeshWater'; Yaml = 'phase-4-mesh-water.yaml' }
        5  = @{ Slug = 'high-shadows'; Flag = 'HighShadows'; Yaml = 'phase-5-high-shadows.yaml' }
        6  = @{ Slug = 'skeletal-animation'; Flag = 'SkeletalAnimation'; Yaml = 'phase-6-skeletal-animation.yaml' }
        7  = @{ Slug = 'worldspace-ui'; Flag = 'WorldspaceUI'; Yaml = 'phase-7-worldspace-ui.yaml' }
        8  = @{ Slug = 'day-night'; Flag = 'DayNightCycle'; Yaml = 'phase-8-day-night.yaml' }
        9  = @{ Slug = 'postfx-particles'; Flag = 'PostFX'; Yaml = 'phase-9-postfx-particles.yaml' }
        10 = @{ Slug = 'lod'; Flag = 'LODScale'; Yaml = 'phase-10-lod.yaml' }
    }
    if (-not $map.ContainsKey($n)) {
        throw "Unknown phase number: $n (use 1–10)"
    }
    return $map[$n]
}

if (-not (Test-Path -LiteralPath $DocsScreenshots)) {
    New-Item -ItemType Directory -Force -Path $DocsScreenshots | Out-Null
}

Write-Host ''
Write-Host 'WSM3D manual smoke helper' -ForegroundColor Cyan
Write-Host "Screenshots dir: $DocsScreenshots"
Write-Host "Index: $SmokeIndex"
Write-Host ''

if ($Install) {
    Write-Host '[1/4] install.ps1 ...' -ForegroundColor Yellow
    & (Join-Path $RepoRoot 'Tools/install.ps1')
}

if ($Launch) {
    Write-Host '[2/4] wsm3d launch ...' -ForegroundColor Yellow
    pwsh (Join-Path $RepoRoot 'Tools/wsm3d.ps1') launch
}

Write-Host '[checklist] Per-phase steps:' -ForegroundColor Yellow
foreach ($n in $Phase) {
    $meta = Get-PhaseMeta $n
    $checklist = Join-Path $RepoRoot "docs/smoke-test-phase$n.md"
    Write-Host "  Phase $n — flag: $($meta.Flag) — checklist: $checklist"
    Write-Host "    PlayCUA: Tools/wsm3d-playcua/sample-scenarios/$($meta.Yaml)"
    Write-Host "    Capture: pwsh Tools/wsm3d.ps1 screenshot phase $n -Name before -WindowOnly"
    Write-Host "             pwsh Tools/wsm3d.ps1 screenshot phase $n -Name after -WindowOnly"
    Write-Host "    Output:  docs/screenshots/phase-$n-*.png"
}

Write-Host ''
Write-Host '[human] Load save slot 2, enter 3D view, enable phase flag(s), kingdom ~500 units, 360 camera sweep.' -ForegroundColor Magenta

if ($OpenChecklists) {
    foreach ($n in $Phase) {
        $path = Join-Path $RepoRoot "docs/smoke-test-phase$n.md"
        if (Test-Path -LiteralPath $path) {
            Start-Process $path
        }
    }
}

if ($PlaycuaBaseline) {
    Write-Host '[3/4] PlayCUA baselines (vision off) ...' -ForegroundColor Yellow
    pwsh (Join-Path $RepoRoot 'Tools/wsm3d.ps1') doctor | Out-Null
    foreach ($n in $Phase) {
        $meta = Get-PhaseMeta $n
        $yaml = Join-Path $RepoRoot "Tools/wsm3d-playcua/sample-scenarios/$($meta.Yaml)"
        if (-not (Test-Path -LiteralPath $yaml)) {
            Write-Warning "Missing scenario: $yaml"
            continue
        }
        Write-Host "  playcua $($meta.Yaml) ..."
        python (Join-Path $RepoRoot 'Tools/wsm3d-playcua/main.py') $yaml --vision-backend off
        if ($LASTEXITCODE -ne 0) {
            throw "PlayCUA failed for phase $n (exit $LASTEXITCODE)"
        }
    }
}

Write-Host '[4/4] Done. File screenshots under docs/screenshots/ when satisfied.' -ForegroundColor Green
