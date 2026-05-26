#!/usr/bin/env pwsh
# One-shot "do all" — offline gates, relaunch, PlayCUA run-all (with retries), screenshots, report.

param(
    [switch]$SkipRelaunch,
    [switch]$SkipLive,
    [int]$PlaycuaRetries = 2
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ReportPath = Join-Path $RepoRoot 'Tools/.reports/do-all-latest.json'

Push-Location $RepoRoot
try {
    $started = Get-Date
    $report = @{
        startedAt = $started.ToUniversalTime().ToString('o')
        stages    = [System.Collections.ArrayList]@()
        overallOk = $true
    }

    function Add-DoAllStage($Id, $Status, $Details = @{}) {
        [void]$report.stages.Add([ordered]@{ id = $Id; status = $Status; details = $Details })
        if ($Status -eq 'failed') { $report.overallOk = $false }
    }

    Write-Host '=== do-all: audit-tick (offline + live) ===' -ForegroundColor Cyan
    # Offline tests first (no game required)
    pwsh (Join-Path $RepoRoot 'Tools/wsm-live-verify.ps1') 2>&1 | Out-Null

    function Wait-BridgeReady {
        param([int]$MaxMinutes = 5)
        $deadline = (Get-Date).AddMinutes($MaxMinutes)
        $health = $null
        while ((Get-Date) -lt $deadline) {
            try {
                $health = Invoke-RestMethod -Uri 'http://127.0.0.1:8766/health' -TimeoutSec 4
                if ($health.bridgeAlive) { return $health }
            } catch {}
            Start-Sleep -Seconds 6
        }
        return $health
    }

    function Wait-World3D {
        param([int]$MaxSeconds = 180)
        $deadline = (Get-Date).AddSeconds($MaxSeconds)
        while ((Get-Date) -lt $deadline) {
            try {
                $h = Invoke-RestMethod -Uri 'http://127.0.0.1:8766/health' -TimeoutSec 4
                if ($h.isWorld3D) { return $h }
            } catch {}
            Start-Sleep -Seconds 5
        }
        try { return Invoke-RestMethod -Uri 'http://127.0.0.1:8766/health' -TimeoutSec 4 } catch { return $null }
    }

    if (-not $SkipRelaunch) {
        Write-Host '=== do-all: relaunch ===' -ForegroundColor Cyan
        pwsh (Join-Path $RepoRoot 'Tools/wsm3d.ps1') relaunch -NoBuild | Out-Host
        Write-Host '=== do-all: wait bridge (up to 5m) ===' -ForegroundColor Cyan
        $health = Wait-BridgeReady -MaxMinutes 5
        if (-not $health -or -not $health.bridgeAlive) {
            Add-DoAllStage 'bridge-wait' 'failed' @{ reason = 'bridge not up after relaunch' }
            throw 'Bridge did not become reachable after relaunch'
        }
        Add-DoAllStage 'bridge-wait' 'passed' @{ isWorld3D = [bool]$health.isWorld3D }

        if (-not $health.isWorld3D) {
            Write-Host '=== do-all: bootstrap save2 (bridge-save-load-smoke) ===' -ForegroundColor Cyan
            $bootstrap = Join-Path $RepoRoot 'Tools/wsm3d-playcua/sample-scenarios/bridge-save-load-smoke.yaml'
            python (Join-Path $RepoRoot 'Tools/wsm3d-playcua/main.py') $bootstrap --vision-backend off 2>&1 | Out-Host
            if ($LASTEXITCODE -ne 0) {
                Write-Host 'bootstrap failed — continuing after wait' -ForegroundColor Yellow
            }
            $health = Wait-World3D -MaxSeconds 180
        }
    } else {
        $health = Wait-BridgeReady -MaxMinutes 2
        if (-not $health -or -not $health.bridgeAlive) {
            Add-DoAllStage 'bridge-wait' 'failed' @{ reason = 'bridge down' }
            throw 'Bridge not reachable (use relaunch or start WorldBox)'
        }
        if (-not $health.isWorld3D) {
            $bootstrap = Join-Path $RepoRoot 'Tools/wsm3d-playcua/sample-scenarios/bridge-save-load-smoke.yaml'
            python (Join-Path $RepoRoot 'Tools/wsm3d-playcua/main.py') $bootstrap --vision-backend off 2>&1 | Out-Null
            $health = Wait-World3D -MaxSeconds 180
        }
    }

    if (-not $SkipLive) {
        Write-Host '=== do-all: journey mock ===' -ForegroundColor Cyan
        pwsh (Join-Path $RepoRoot 'Tools/verify-journeys.ps1') | Out-Host
        if ($LASTEXITCODE -ne 0) { Add-DoAllStage 'journey-mock' 'failed' @{ exitCode = $LASTEXITCODE } }
        else { Add-DoAllStage 'journey-mock' 'passed' @{} }

        Write-Host '=== do-all: playcua run-all (retries=$PlaycuaRetries) ===' -ForegroundColor Cyan
        $attempt = 0
        $runOk = $false
        while ($attempt -lt $PlaycuaRetries -and -not $runOk) {
            $attempt++
            if ($attempt -gt 1) {
                Write-Host "playcua retry $attempt/$PlaycuaRetries — relaunch between attempts" -ForegroundColor Yellow
                pwsh (Join-Path $RepoRoot 'Tools/wsm3d.ps1') relaunch -NoBuild | Out-Null
                $null = Wait-BridgeReady -MaxMinutes 5
                Start-Sleep -Seconds 30
                $null = Wait-World3D -MaxSeconds 120
            }
            pwsh (Join-Path $RepoRoot 'Tools/wsm3d.ps1') playcua run-all -VisionBackend off 2>&1 | Out-Host
            if ($LASTEXITCODE -eq 0) { $runOk = $true }
        }
        if ($runOk) {
            Add-DoAllStage 'playcua-run-all' 'passed' @{ attempts = $attempt }
            pwsh (Join-Path $RepoRoot 'Tools/sync-playcua-screenshots.ps1') | Out-Host
            pwsh (Join-Path $RepoRoot 'Tools/wsm-live-verify.ps1') -Live -SkipOffline | Out-Host
            if ($LASTEXITCODE -ne 0) {
                Add-DoAllStage 'live-verify-live' 'failed' @{ exitCode = $LASTEXITCODE }
            } else {
                Add-DoAllStage 'live-verify-live' 'passed' @{}
            }
        } else {
            Add-DoAllStage 'playcua-run-all' 'failed' @{ attempts = $attempt }
        }
    }

    Write-Host '=== do-all: full offline verify ===' -ForegroundColor Cyan
    pwsh (Join-Path $RepoRoot 'Tools/wsm-live-verify.ps1') | Out-Host
    if ($LASTEXITCODE -ne 0) { Add-DoAllStage 'live-verify-offline' 'failed' @{} }
    else { Add-DoAllStage 'live-verify-offline' 'passed' @{} }

    & (Join-Path $RepoRoot 'Tools/wsm3d-audit-tick.ps1') -Quiet | Out-Null

    $report.durationMs = [math]::Round(((Get-Date) - $started).TotalMilliseconds, 0)
    $report.finishedAt = (Get-Date).ToUniversalTime().ToString('o')
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8

    Write-Host ''
    Write-Host "do-all report: $ReportPath" -ForegroundColor Gray
    Write-Host "overallOk=$($report.overallOk)" -ForegroundColor $(if ($report.overallOk) { 'Green' } else { 'Yellow' })
    if (-not $report.overallOk) { exit 1 }
} finally {
    Pop-Location
}
