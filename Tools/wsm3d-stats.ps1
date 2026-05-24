#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Auto-generate stats dashboard for WorldSphereMod3D.

.DESCRIPTION
  Collects metrics from the repository (tests, LOC, patches, journeys, git
  activity, releases, CI status) and writes a VitePress-compatible markdown
  dashboard to docs/dashboard.md. Runs as part of the nightly.yml workflow.

.EXAMPLE
  pwsh Tools/wsm3d-stats.ps1
#>

param()

$ErrorActionPreference = 'Continue'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$DashboardPath = Join-Path $RepoRoot 'docs' 'dashboard.md'

Write-Host "Generating stats dashboard for $RepoRoot..."

function Compute-UnitTests {
  $pattern = '\[Fact\]|\[Theory\]'
  $testDir = Join-Path $RepoRoot 'tests/WorldSphereMod.Tests.Unit'
  if (Test-Path $testDir) {
    $files = @(Get-ChildItem -Path "$testDir" -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue)
    $count = 0
    foreach ($file in $files) {
      $count += @(Select-String -Path $file.FullName -Pattern $pattern -ErrorAction SilentlyContinue) | Measure-Object | Select-Object -ExpandProperty Count
    }
    return $count
  }
  return 0
}

function Compute-E2ETests {
  $pattern = '\[Fact\]|\[Theory\]'
  $testDir = Join-Path $RepoRoot 'tests/WorldSphereMod.Tests.E2E'
  if (Test-Path $testDir) {
    $files = @(Get-ChildItem -Path "$testDir" -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue)
    $count = 0
    foreach ($file in $files) {
      $count += @(Select-String -Path $file.FullName -Pattern $pattern -ErrorAction SilentlyContinue) | Measure-Object | Select-Object -ExpandProperty Count
    }
    return $count
  }
  return 0
}

function Compute-IntegrationTests {
  $pattern = '\[Fact\]|\[Theory\]'
  $testDir = Join-Path $RepoRoot 'tests/WorldSphereMod.Tests.Integration'
  if (Test-Path $testDir) {
    $files = @(Get-ChildItem -Path "$testDir" -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue)
    $count = 0
    foreach ($file in $files) {
      $count += @(Select-String -Path $file.FullName -Pattern $pattern -ErrorAction SilentlyContinue) | Measure-Object | Select-Object -ExpandProperty Count
    }
    return $count
  }
  return 0
}

function Compute-SourceLOC {
  $codeDir = Join-Path $RepoRoot 'WorldSphereMod/Code'
  if (Test-Path $codeDir) {
    @(Get-ChildItem -Path "$codeDir/*.cs" -Recurse -ErrorAction SilentlyContinue | Get-Content -ErrorAction SilentlyContinue | Measure-Object -Line) | Select-Object -ExpandProperty Lines
  } else { 0 }
}

function Compute-PhaseBreakdown {
  $codeDir = Join-Path $RepoRoot 'WorldSphereMod/Code'
  $lines = @()
  if (Test-Path $codeDir) {
    @('Voxel', 'ProcGen', 'Foliage', 'Water', 'Lighting', 'Rig', 'Worldspace', 'Fx', 'LOD', 'Perf') | ForEach-Object {
      $phaseDir = Join-Path $codeDir $_
      if (Test-Path $phaseDir) {
        $fileCount = @(Get-ChildItem -Path "$phaseDir/*.cs" -Recurse -ErrorAction SilentlyContinue) | Measure-Object | Select-Object -ExpandProperty Count
        if ($fileCount -gt 0) { $lines += "| $_ | $fileCount |" }
      }
    }
  }
  return $lines
}

function Compute-HarmonyPatches {
  $codeDir = Join-Path $RepoRoot 'WorldSphereMod/Code'
  $allPatches = 0
  $taggedPatches = 0
  if (Test-Path $codeDir) {
    $files = @(Get-ChildItem -Path "$codeDir" -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue)
    foreach ($file in $files) {
      $allPatches += @(Select-String -Path $file.FullName -Pattern '\[HarmonyPatch\(' -ErrorAction SilentlyContinue) | Measure-Object | Select-Object -ExpandProperty Count
      $taggedPatches += @(Select-String -Path $file.FullName -Pattern '\[Phase' -ErrorAction SilentlyContinue) | Measure-Object | Select-Object -ExpandProperty Count
    }
  }
  return @{Total = $allPatches; Tagged = $taggedPatches; Untagged = $allPatches - $taggedPatches}
}

function Compute-Journeys {
  $manifestDir = Join-Path $RepoRoot 'docs/journeys/manifests'
  $mc = 0; $fc = 0
  if (Test-Path $manifestDir) {
    $mc = @(Get-ChildItem -Path "$manifestDir/*/manifest.json" -Recurse -ErrorAction SilentlyContinue) | Measure-Object | Select-Object -ExpandProperty Count
    $fc = @(Get-ChildItem -Path "$manifestDir/*/frame-*.png" -Recurse -ErrorAction SilentlyContinue) | Measure-Object | Select-Object -ExpandProperty Count
  }
  return @{Manifests = $mc; Frames = $fc}
}

function Compute-GitActivity {
  $result = @{TotalCommits = 'n/a'; Last7Days = 'n/a'; RecentSHA = 'n/a'; RecentSubject = 'n/a'}
  Push-Location $RepoRoot -ErrorAction SilentlyContinue
  if (git rev-parse --git-dir 2>$null) {
    $total = git rev-list --count HEAD 2>$null
    if ($total) { $result.TotalCommits = $total }
    $week = @(git log --since="7 days ago" --oneline 2>$null) | Measure-Object | Select-Object -ExpandProperty Count
    if ($week -gt 0) { $result.Last7Days = $week }
    $recent = git log -1 --pretty=format:"%H %s" 2>$null
    if ($recent) {
      $parts = $recent -split ' ', 2
      $result.RecentSHA = $parts[0].Substring(0, 7)
      $result.RecentSubject = $parts[1]
    }
  }
  Pop-Location
  return $result
}

function Compute-Releases {
  $result = @{LatestTag = 'n/a'; TagCount = 0}
  Push-Location $RepoRoot -ErrorAction SilentlyContinue
  if (git rev-parse --git-dir 2>$null) {
    $latest = git describe --tags --abbrev=0 2>$null
    if ($latest) { $result.LatestTag = $latest }
    $tagCount = @(git tag -l 2>$null) | Measure-Object | Select-Object -ExpandProperty Count
    if ($tagCount) { $result.TagCount = $tagCount }
  }
  Pop-Location
  return $result
}

function Compute-CIStatus {
  $result = @()
  if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { return $result }
  try {
    $runs = gh run list --limit 5 --json status,conclusion,name --repo KooshaPari/WorldSphereMod 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue
    foreach ($run in $runs) {
      $icon = @{'success'='pass'; 'failure'='fail'; 'cancelled'='skip'}[$run.conclusion] ?? 'pending'
      $result += "| $icon | $($run.name) |"
    }
  } catch { }
  return $result
}

# Collect metrics
$unitTests = Compute-UnitTests
$e2eTests = Compute-E2ETests
$integrationTests = Compute-IntegrationTests
$totalTests = $unitTests + $e2eTests + $integrationTests
$sourceLOC = Compute-SourceLOC
$phaseBreakdown = Compute-PhaseBreakdown
$patches = Compute-HarmonyPatches
$journeys = Compute-Journeys
$gitActivity = Compute-GitActivity
$releases = Compute-Releases
$ciStatus = Compute-CIStatus
$timestamp = Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ'

Write-Host "Collected: $totalTests tests, $sourceLOC LOC, $($patches.Total) patches, $($journeys.Manifests) journeys"

$patchTotal = $patches.Total
$patchTagged = $patches.Tagged
$patchUntagged = $patches.Untagged
$journeyManifests = $journeys.Manifests
$journeyFrames = $journeys.Frames
$ciLines = $ciStatus -join "`n"
$phaseLines = $phaseBreakdown -join "`n"
$gitSHA = $gitActivity.RecentSHA
$gitSubject = $gitActivity.RecentSubject
$gitTotal = $gitActivity.TotalCommits
$gitWeek = $gitActivity.Last7Days
$releaseTag = $releases.LatestTag
$releaseCount = $releases.TagCount

$md = "<!-- AUTO-GENERATED by Tools/wsm3d-stats.ps1. Do not hand-edit. -->" + [Environment]::NewLine + [Environment]::NewLine
$md += "# WorldSphereMod3D Stats Dashboard" + [Environment]::NewLine + [Environment]::NewLine
$md += "**Last regenerated:** ``$timestamp``" + [Environment]::NewLine + [Environment]::NewLine
$md += "## Test Coverage" + [Environment]::NewLine + [Environment]::NewLine
$md += "| Category | Count |" + [Environment]::NewLine
$md += "|---|---|" + [Environment]::NewLine
$md += "| Unit tests | $unitTests |" + [Environment]::NewLine
$md += "| E2E tests | $e2eTests |" + [Environment]::NewLine
$md += "| Integration tests | $integrationTests |" + [Environment]::NewLine
$md += "| **Total** | **$totalTests** |" + [Environment]::NewLine + [Environment]::NewLine
$md += "## Source Code Size" + [Environment]::NewLine + [Environment]::NewLine
$md += "| Phase | Files |" + [Environment]::NewLine
$md += "|---|---|" + [Environment]::NewLine
$md += $phaseLines + [Environment]::NewLine
$md += "| **Total LOC** | **$sourceLOC** |" + [Environment]::NewLine + [Environment]::NewLine
$md += "## Harmony Patches" + [Environment]::NewLine + [Environment]::NewLine
$md += "| Metric | Count |" + [Environment]::NewLine
$md += "|---|---|" + [Environment]::NewLine
$md += "| Total patches | $patchTotal |" + [Environment]::NewLine
$md += "| Tagged (with [Phase]) | $patchTagged |" + [Environment]::NewLine
$md += "| Untagged | $patchUntagged |" + [Environment]::NewLine + [Environment]::NewLine
$md += "## Phenotype Journeys" + [Environment]::NewLine + [Environment]::NewLine
$md += "| Metric | Count |" + [Environment]::NewLine
$md += "|---|---|" + [Environment]::NewLine
$md += "| Manifests | $journeyManifests |" + [Environment]::NewLine
$md += "| Captured frames | $journeyFrames |" + [Environment]::NewLine + [Environment]::NewLine
$md += "## Git Velocity" + [Environment]::NewLine + [Environment]::NewLine
$md += "| Metric | Value |" + [Environment]::NewLine
$md += "|---|---|" + [Environment]::NewLine
$md += "| Total commits | $gitTotal |" + [Environment]::NewLine
$md += "| Commits (last 7 days) | $gitWeek |" + [Environment]::NewLine
$md += "| Latest commit | ``$gitSHA`` $gitSubject |" + [Environment]::NewLine + [Environment]::NewLine
$md += "## Releases" + [Environment]::NewLine + [Environment]::NewLine
$md += "| Metric | Value |" + [Environment]::NewLine
$md += "|---|---|" + [Environment]::NewLine
$md += "| Latest tag | ``$releaseTag`` |" + [Environment]::NewLine
$md += "| Total tags | $releaseCount |" + [Environment]::NewLine + [Environment]::NewLine
$md += "## Recent CI Status" + [Environment]::NewLine + [Environment]::NewLine
$md += "| Status | Workflow |" + [Environment]::NewLine
$md += "|---|---|" + [Environment]::NewLine
if ($ciLines) { $md += $ciLines } else { $md += "| n/a | No recent runs |" }
$md += [Environment]::NewLine + [Environment]::NewLine
$md += "---" + [Environment]::NewLine + [Environment]::NewLine
$md += "*Regenerated at $timestamp by Tools/wsm3d-stats.ps1*"

Set-Content -Path $DashboardPath -Value $md -Encoding UTF8
Write-Host "✓ Dashboard written to $DashboardPath"
Write-Host ""
$md -split [Environment]::NewLine | Select-Object -First 30
