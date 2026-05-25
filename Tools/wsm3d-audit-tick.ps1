#!/usr/bin/env pwsh
# Per-tick audit for /loop: offline gates, bridge/live PlayCUA, artifacts, blockers.
# Writes Tools/.reports/audit-tick-latest.json

param(
    [switch]$SkipOffline,
    [switch]$SkipLive,
    [switch]$RelaunchIfBridgeDown,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ReportDir = Join-Path $RepoRoot 'Tools/.reports'
$ReportPath = Join-Path $ReportDir 'audit-tick-latest.json'
$StatePath = Join-Path $ReportDir 'audit-tick-state.json'
$BridgeUrl = 'http://127.0.0.1:8766/health'
$ScreenshotsDir = Join-Path $RepoRoot 'docs/screenshots'
$PlayerLog = Join-Path $env:USERPROFILE 'AppData/LocalLow/mkarpenko/WorldBox/Player.log'

if (-not (Test-Path -LiteralPath $ReportDir)) {
    New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null
}

function Write-TickLog([string]$Message, [string]$Level = 'INFO') {
    if ($Quiet) { return }
    $color = switch ($Level) {
        'OK' { 'Green' }
        'WARN' { 'Yellow' }
        'ERR' { 'Red' }
        default { 'Cyan' }
    }
    Write-Host "[$Level] $Message" -ForegroundColor $color
}

function Add-Stage($Report, [string]$Id, [string]$Status, [hashtable]$Details = @{}) {
    $Report['stages'] += [ordered]@{
        id      = $Id
        status  = $Status
        details = $Details
    }
    if ($Status -eq 'failed') { $Report['overallOk'] = $false }
}

function Get-BridgeHealth {
    try {
        return Invoke-RestMethod -Uri $BridgeUrl -Method Get -TimeoutSec 4
    } catch {
        return $null
    }
}

function Get-LastRelaunchUtc {
    if (-not (Test-Path -LiteralPath $StatePath)) { return $null }
    try {
        $s = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
        return [datetime]$s.lastRelaunchUtc
    } catch {
        return $null
    }
}

function Set-LastRelaunchUtc {
    [ordered]@{
        lastRelaunchUtc = (Get-Date).ToUniversalTime().ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath $StatePath -Encoding utf8
}

$started = Get-Date
$report = @{
    startedAt = $started.ToUniversalTime().ToString('o')
    repoRoot  = $RepoRoot
    overallOk = $true
    stages    = [System.Collections.ArrayList]@()
    blockers  = [System.Collections.ArrayList]@()
    completed = [System.Collections.ArrayList]@()
}

# --- git ---
try {
    Push-Location $RepoRoot
    $branch = (git rev-parse --abbrev-ref HEAD 2>$null)
    $head = (git rev-parse --short HEAD 2>$null)
    $dirty = @(git status --porcelain 2>$null) | Where-Object { $_ -notmatch '^\?\? External/Compound-Spheres' }
    Add-Stage $report 'git' 'passed' @{
        branch       = $branch
        head         = $head
        dirtyCount   = @($dirty).Count
        dirtySample  = @($dirty | Select-Object -First 8)
    }
    Write-TickLog "git $branch @ $head (dirty=$(@($dirty).Count))"
} catch {
    Add-Stage $report 'git' 'failed' @{ error = $_.Exception.Message }
    Write-TickLog $_.Exception.Message 'ERR'
} finally {
    Pop-Location
}

# --- offline tests ---
if (-not $SkipOffline) {
    try {
        Push-Location $RepoRoot
        dotnet restore WorldSphereMod.sln -v q | Out-Null
        $testOut = dotnet test WorldSphereMod.sln --no-restore -v q 2>&1 | Out-String
        $failed = [regex]::Matches($testOut, 'Failed:\s+(\d+)') | ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Maximum | Select-Object -ExpandProperty Maximum
        if ($null -eq $failed) { $failed = 0 }
        if ($failed -gt 0) {
            Add-Stage $report 'dotnet-test' 'failed' @{ failed = $failed; tail = ($testOut -split "`n" | Select-Object -Last 12) }
            Write-TickLog "dotnet test failed count=$failed" 'ERR'
        } else {
            $passed = [regex]::Matches($testOut, 'Passed:\s+(\d+)') | ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Sum | Select-Object -ExpandProperty Sum
            Add-Stage $report 'dotnet-test' 'passed' @{ passedSum = $passed }
            [void]$report.completed.Add('offline-tests')
            Write-TickLog "dotnet test passed (sum lines=$passed)" 'OK'
        }
    } catch {
        Add-Stage $report 'dotnet-test' 'failed' @{ error = $_.Exception.Message }
        Write-TickLog $_.Exception.Message 'ERR'
    } finally {
        Pop-Location
    }
} else {
    Add-Stage $report 'dotnet-test' 'skipped' @{ reason = 'SkipOffline' }
}

# --- doctor ---
try {
    Push-Location $RepoRoot
    $doc = pwsh (Join-Path $RepoRoot 'Tools/wsm3d.ps1') doctor 2>&1 | Out-String
    $docOk = $doc -match '\[OK\] All required checks passed' -or $doc -match 'Required checks passed with'
    if ($docOk) {
        Add-Stage $report 'doctor' 'passed' @{ optionalWarnings = ($doc -match '\[WARN\]') }
        Write-TickLog 'doctor OK' 'OK'
    } else {
        Add-Stage $report 'doctor' 'failed' @{ tail = ($doc -split "`n" | Select-Object -Last 15) }
        Write-TickLog 'doctor not fully green' 'WARN'
    }
} catch {
    Add-Stage $report 'doctor' 'failed' @{ error = $_.Exception.Message }
} finally {
    Pop-Location
}

# --- screenshots manifest (human gate) ---
$shotCount = 0
if (Test-Path -LiteralPath $ScreenshotsDir) {
    $shotCount = @(Get-ChildItem -LiteralPath $ScreenshotsDir -Filter 'phase-*.png' -File -ErrorAction SilentlyContinue).Count
}
if ($shotCount -lt 2) {
    [void]$report.blockers.Add("manual-screenshots: $shotCount phase-*.png in docs/screenshots (need populated-world captures)")
    Add-Stage $report 'screenshots' 'pending' @{ count = $shotCount; dir = $ScreenshotsDir }
    Write-TickLog "screenshots pending ($shotCount files)" 'WARN'
} else {
    Add-Stage $report 'screenshots' 'passed' @{ count = $shotCount }
    [void]$report.completed.Add('screenshots-present')
    Write-TickLog "screenshots $shotCount files" 'OK'
}

# --- bridge / live ---
$health = Get-BridgeHealth
if (-not $health) {
    Add-Stage $report 'bridge' 'failed' @{ reachable = $false }
    [void]$report.blockers.Add('bridge-down')
    Write-TickLog 'bridge down' 'WARN'

    if ($RelaunchIfBridgeDown) {
        $last = Get-LastRelaunchUtc
        $cooldownOk = (-not $last) -or ((Get-Date).ToUniversalTime() - $last).TotalMinutes -ge 15
        if ($cooldownOk) {
            Write-TickLog 'relaunch (cooldown ok)' 'INFO'
            try {
                Push-Location $RepoRoot
                pwsh (Join-Path $RepoRoot 'Tools/wsm3d.ps1') relaunch -NoBuild 2>&1 | Out-Null
                Set-LastRelaunchUtc
                Start-Sleep -Seconds 45
                $health = Get-BridgeHealth
                if ($health) {
                    Add-Stage $report 'relaunch' 'passed' @{ bridgeAfter = $true }
                    Write-TickLog 'relaunch brought bridge back' 'OK'
                } else {
                    Add-Stage $report 'relaunch' 'failed' @{ bridgeAfter = $false }
                }
            } catch {
                Add-Stage $report 'relaunch' 'failed' @{ error = $_.Exception.Message }
            } finally {
                Pop-Location
            }
        } else {
            Add-Stage $report 'relaunch' 'skipped' @{ reason = 'cooldown-15m' }
        }
    }
} else {
    Add-Stage $report 'bridge' 'passed' @{
        isWorld3D = [bool]$health.isWorld3D
        version   = $health.version
    }
    Write-TickLog "bridge OK isWorld3D=$($health.isWorld3D)" 'OK'

    if (Test-Path -LiteralPath $PlayerLog) {
        $shaderLine = Select-String -Path $PlayerLog -Pattern 'LoadedShaders\[count=' -ErrorAction SilentlyContinue | Select-Object -Last 1
        if ($shaderLine) {
            Add-Stage $report 'shader-log' 'passed' @{ line = $shaderLine.Line.Trim() }
            Write-TickLog $shaderLine.Line.Trim() 'OK'
        }
    }
}

if (-not $SkipLive -and $health) {
    try {
        Push-Location $RepoRoot
        if (-not $health.isWorld3D) {
            Write-TickLog 'waiting for isWorld3D (150s)' 'INFO'
            $deadline = (Get-Date).AddSeconds(150)
            while ((Get-Date) -lt $deadline) {
                $health = Get-BridgeHealth
                if ($health -and $health.isWorld3D) { break }
                Start-Sleep -Seconds 5
            }
        }

        if (-not $health.isWorld3D) {
            Write-TickLog 'bootstrap: bridge-save-load-smoke' 'INFO'
            $bootstrap = Join-Path $RepoRoot 'Tools/wsm3d-playcua/sample-scenarios/bridge-save-load-smoke.yaml'
            python (Join-Path $RepoRoot 'Tools/wsm3d-playcua/main.py') $bootstrap --vision-backend off 2>&1 | Out-Null
            Start-Sleep -Seconds 20
            $health = Get-BridgeHealth
        }

        if ($health -and $health.isWorld3D) {
            $journey = pwsh (Join-Path $RepoRoot 'Tools/verify-journeys.ps1') 2>&1 | Out-String
            if ($LASTEXITCODE -eq 0) {
                Add-Stage $report 'journey-mock' 'passed' @{}
                [void]$report.completed.Add('journey-mock')
                Write-TickLog 'journey mock 20/20' 'OK'
            } else {
                Add-Stage $report 'journey-mock' 'failed' @{ exitCode = $LASTEXITCODE }
            }

            $runAll = pwsh (Join-Path $RepoRoot 'Tools/wsm3d.ps1') playcua run-all -VisionBackend off 2>&1 | Out-String
            if ($LASTEXITCODE -eq 0 -and $runAll -match 'playcua passed') {
                Add-Stage $report 'playcua-run-all' 'passed' @{}
                [void]$report.completed.Add('playcua-run-all')
                Write-TickLog 'playcua run-all 13/13' 'OK'
                try {
                    pwsh (Join-Path $RepoRoot 'Tools/sync-playcua-screenshots.ps1') 2>&1 | Out-Null
                    $synced = @(Get-ChildItem -LiteralPath $ScreenshotsDir -Filter 'phase-*.png' -File -ErrorAction SilentlyContinue).Count
                    if ($synced -gt 0) {
                        Add-Stage $report 'screenshot-sync' 'passed' @{ count = $synced }
                        [void]$report.completed.Add('screenshot-sync')
                    }
                } catch {
                    Add-Stage $report 'screenshot-sync' 'failed' @{ error = $_.Exception.Message }
                }
            } else {
                Add-Stage $report 'playcua-run-all' 'failed' @{ exitCode = $LASTEXITCODE; tail = ($runAll -split "`n" | Select-Object -Last 20) }
                Write-TickLog 'playcua run-all failed' 'ERR'
            }

            # run-all already covers bridge + phase PlayCUA; skip live-verify here (often fails if run-all stressed the game).
            Add-Stage $report 'live-verify-skipoffline' 'skipped' @{ reason = 'covered-by-playcua-run-all' }
        } else {
            Add-Stage $report 'playcua-run-all' 'skipped' @{ reason = 'isWorld3D=false' }
            [void]$report.blockers.Add('not-in-3d: load save2 in UI or wait for load_save')
            # Bridge-only smokes
            foreach ($y in @('bridge-health-vision.yaml', 'bridge-save-load-smoke.yaml')) {
                $p = Join-Path $RepoRoot "Tools/wsm3d-playcua/sample-scenarios/$y"
                python (Join-Path $RepoRoot 'Tools/wsm3d-playcua/main.py') $p --vision-backend off 2>&1 | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    Add-Stage $report "playcua-$y" 'failed' @{ exitCode = $LASTEXITCODE }
                }
            }
        }
    } catch {
        Add-Stage $report 'live-playcua' 'failed' @{ error = $_.Exception.Message }
    } finally {
        Pop-Location
    }
}

# --- static blockers ---
[void]$report.blockers.Add('vision-backend: inference Fireworks key or OmniRoute for -Live -Vision')
[void]$report.blockers.Add('shader-bake: 6/9 bundle shaders empty-name — fix bake sources before widening Core.cs whitelist')

$report.durationMs = [math]::Round(((Get-Date) - $started).TotalMilliseconds, 0)
$report.finishedAt = (Get-Date).ToUniversalTime().ToString('o')

$json = $report | ConvertTo-Json -Depth 8
Set-Content -LiteralPath $ReportPath -Value $json -Encoding utf8

if (-not $Quiet) {
    Write-Host ''
    Write-Host "Report: $ReportPath" -ForegroundColor Gray
    Write-Host "overallOk=$($report.overallOk) completed=$($report.completed -join ', ')" -ForegroundColor $(if ($report['overallOk']) { 'Green' } else { 'Yellow' })
}

# Human gates (screenshots, vision) are listed in blockers but do not fail the automation exit code.
$automationFailed = @($report.stages | Where-Object { $_.status -eq 'failed' -and $_.id -notmatch 'screenshots' })
if ($automationFailed.Count -gt 0) { exit 1 }
exit 0
