#!/usr/bin/env pwsh
# One-shot "do all" — offline gates, relaunch, PlayCUA run-all (with retries), screenshots, report.

param(
    [switch]$SkipRelaunch,
    [switch]$SkipLive,
    [switch]$Vision,
    [ValidateSet('fireworks', 'omniroute', 'anthropic', 'off')]
    [string]$VisionBackend = 'omniroute',
    [int]$PlaycuaRetries = 3
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ReportPath = Join-Path $RepoRoot 'Tools/.reports/do-all-latest.json'

. (Join-Path $RepoRoot 'Tools/wsm3d.ps1')

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

    function Get-DoAllSummaryLine {
        param($Report)
        $mins = if ($Report.durationMs) { [math]::Round($Report.durationMs / 60000, 1) } else { $null }
        $playcua = $Report.stages | Where-Object { $_.id -eq 'playcua-run-all' } | Select-Object -First 1
        $playcuaBit = if ($playcua) {
            $attempts = $playcua.details.attempts
            if ($attempts) { "playcua=$($playcua.status)@${attempts}x" } else { "playcua=$($playcua.status)" }
        } else { 'playcua=skipped' }
        $failed = @($Report.stages | Where-Object { $_.status -eq 'failed' } | ForEach-Object { $_.id })
        $degraded = @($Report.stages | Where-Object { $_.status -eq 'degraded' } | ForEach-Object { $_.id })
        $failedBit = if ($failed.Count -gt 0) { "failed=$($failed -join ',')" } else { 'failed=none' }
        if ($degraded.Count -gt 0) { $failedBit += "; degraded=$($degraded -join ',')" }
        $durationBit = if ($null -ne $mins) { "durationMin=$mins" } else { '' }
        @("do-all overallOk=$($Report.overallOk)", $playcuaBit, $failedBit, $durationBit) -join ' | '
    }

    function Import-DoAllVisionEnv {
        foreach ($fileName in @('omniroute-vision.env', 'fireworks-vision.env')) {
            $envFile = Join-Path $RepoRoot "Tools/$fileName"
            if (Test-Path -LiteralPath $envFile) {
                Get-Content -LiteralPath $envFile | ForEach-Object {
                    if ($_ -match '^\s*([^#][^=]+)=(.*)$') {
                        Set-Item -Path "env:$($matches[1].Trim())" -Value $matches[2].Trim()
                    }
                }
            }
        }
    }

    function Import-DoAllVisionEnv {
        Import-DoAllVisionEnvFile "omniroute-vision.env"
        Import-DoAllVisionEnvFile "fireworks-vision.env"
        if (-not $env:FIREWORKS_API_KEY) {
            $userFw = [Environment]::GetEnvironmentVariable("FIREWORKS_API_KEY", "User")
            if ($userFw) { $env:FIREWORKS_API_KEY = $userFw }
        }
    }

    function Get-DefaultPlaycuaVisionBackend {
        $explicit = if ($env:PLAYCUA_VISION_BACKEND) { $env:PLAYCUA_VISION_BACKEND.Trim().ToLowerInvariant() } else { "" }
        if ($explicit -in @("fireworks", "omniroute", "anthropic", "off")) { return $explicit }
        if ($env:FIREWORKS_API_KEY) { return "fireworks" }
        if ($env:OMNROUTE_API_KEY) { return "omniroute" }
        if ($env:ANTHROPIC_API_KEY) { return "anthropic" }
        return "off"
    }

    Import-DoAllVisionEnv
    if ($PSBoundParameters.ContainsKey('VisionBackend')) {
        $env:PLAYCUA_VISION_BACKEND = $VisionBackend
    }
    $visionBackendResolved = if ($Vision) { Get-DefaultPlaycuaVisionBackend } else { 'off' }
    $omnirouteProbeOk = $true

    Write-Host '=== do-all: audit-tick (offline + live) ===' -ForegroundColor Cyan
    # Offline tests first (no game required)
    pwsh (Join-Path $RepoRoot 'Tools/wsm-live-verify.ps1') | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Add-DoAllStage 'initial-offline-verify' 'failed' @{ exitCode = $LASTEXITCODE }
        throw 'Initial offline verify failed'
    }
    Add-DoAllStage 'initial-offline-verify' 'passed' @{}

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

    if (-not $SkipRelaunch) {
        Write-Host '=== do-all: relaunch ===' -ForegroundColor Cyan
        pwsh (Join-Path $RepoRoot 'Tools/wsm3d.ps1') relaunch -NoBuild | Out-Host
        Write-Host '=== do-all: wait bridge (up to 8m) ===' -ForegroundColor Cyan
        $health = Wait-BridgeReady -MaxMinutes 8
        if (-not $health -or -not $health.bridgeAlive) {
            Add-DoAllStage 'bridge-wait' 'failed' @{ reason = 'bridge not up after relaunch (8m)' }
            $report.durationMs = [math]::Round(((Get-Date) - $started).TotalMilliseconds, 0)
            $report.finishedAt = (Get-Date).ToUniversalTime().ToString('o')
            $report.summaryLine = Get-DoAllSummaryLine -Report $report
            $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8
            throw 'Bridge did not become reachable after relaunch (waited 8m)'
        }
        Add-DoAllStage 'bridge-wait' 'passed' @{ isWorld3D = [bool]$health.isWorld3D }

            if (-not $health.isWorld3D) {
                Write-Host '=== do-all: bootstrap save2 (bridge-save-load-smoke) ===' -ForegroundColor Cyan
                $null = Ensure-BridgeWorld3DBootstrapped -BootstrapVisionBackend off
                $health = Get-BridgeHealth
            }
        } else {
            $health = Wait-BridgeReady -MaxMinutes 2
            if (-not $health -or -not $health.bridgeAlive) {
                Add-DoAllStage 'bridge-wait' 'failed' @{ reason = 'bridge down' }
                throw 'Bridge not reachable (use relaunch or start WorldBox)'
            }
            if (-not $health.isWorld3D) {
                $null = Ensure-BridgeWorld3DBootstrapped -BootstrapVisionBackend off
                $health = Get-BridgeHealth
            }
        }
    } else {
        $health = Wait-BridgeReady -MaxMinutes 2
        if (-not $health -or -not $health.bridgeAlive) {
            Add-DoAllStage 'bridge-wait' 'failed' @{ reason = 'bridge down' }
            $report.durationMs = [math]::Round(((Get-Date) - $started).TotalMilliseconds, 0)
            $report.finishedAt = (Get-Date).ToUniversalTime().ToString('o')
            $report.summaryLine = Get-DoAllSummaryLine -Report $report
            $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8
            throw 'Bridge not reachable (use relaunch or start WorldBox)'
        }
        if (-not $health.isWorld3D) {
            $bootstrap = Join-Path $RepoRoot 'Tools/wsm3d-playcua/sample-scenarios/bridge-save-load-smoke.yaml'
            python (Join-Path $RepoRoot 'Tools/wsm3d-playcua/main.py') $bootstrap --vision-backend off 2>&1 | Out-Null
            $health = Wait-World3D -MaxSeconds 180
        }
    }

    $offlineVerifyDone = $false
    if (-not $SkipLive) {
        if ($visionBackendResolved -eq 'omniroute' -and $env:OMNROUTE_BASE_URL -and $env:OMNROUTE_API_KEY) {
            Write-Host "=== do-all: omniroute probe ($($env:OMNROUTE_BASE_URL)) ===" -ForegroundColor Cyan
            try {
                Test-OmniRoutePeerReachable -BaseUrl $env:OMNROUTE_BASE_URL.TrimEnd('/')
                $base = $env:OMNROUTE_BASE_URL.TrimEnd('/')
                $modelCount = $null
                try {
                    $models = Invoke-RestMethod -Uri "$base/models" -Headers @{ Authorization = "Bearer $env:OMNROUTE_API_KEY" } -TimeoutSec 30
                    $modelCount = @($models.data).Count
                } catch {
                    Write-Host "omniroute /models slow or unavailable (continuing with chat probe): $($_.Exception.Message)" -ForegroundColor DarkYellow
                }
                $modelId = if ($env:OMNROUTE_VISION_COMBO) { $env:OMNROUTE_VISION_COMBO }
                elseif ($env:OMNROUTE_VISION_MODEL) { $env:OMNROUTE_VISION_MODEL }
                else { 'wsm3d-vision-frontier' }
                $txt = $null
                if ($null -ne $modelCount -and $modelCount -gt 0) {
                    try {
                        $txt = Invoke-OmniRouteChatProbe -BaseUrl $base -ModelId $modelId
                        $omnirouteProbeOk = $true
                    } catch {
                        $omnirouteProbeOk = $false
                        Write-Host "omniroute /models OK ($modelCount) but chat/completions failed from desk: $($_.Exception.Message)" -ForegroundColor Yellow
                        Write-Host 'CC on the laptop uses localhost; expose chat over Tailscale (e.g. tailscale serve 20128) or run PlayCUA vision off.' -ForegroundColor DarkYellow
                    }
                    if ($omnirouteProbeOk) {
                        Add-DoAllStage 'omniroute-probe' 'passed' @{ models = $modelCount; model = $modelId; reply = $txt; chatProbe = 'ok' }
                        Write-Host "omniroute probe OK ($modelId, models=$modelCount): $txt" -ForegroundColor Green
                    } else {
                        Add-DoAllStage 'omniroute-probe' 'degraded' @{
                            models    = $modelCount
                            model     = $modelId
                            chatProbe = 'failed'
                            error     = 'chat/completions unreachable from desk (models list only)'
                        }
                    }
                } else {
                    $txt = Invoke-OmniRouteChatProbe -BaseUrl $base -ModelId $modelId -TimeoutSec 25
                    Add-DoAllStage 'omniroute-probe' 'passed' @{ models = $modelCount; model = $modelId; reply = $txt }
                    Write-Host "omniroute vision probe OK ($modelId): $txt" -ForegroundColor Green
                }
            } catch {
                $omnirouteProbeOk = $false
                Add-DoAllStage 'omniroute-probe' 'degraded' @{ error = $_.Exception.Message }
                Write-Host "omniroute probe failed: $($_.Exception.Message)" -ForegroundColor Yellow
                Write-Host 'playcua will run with vision off until laptop OmniRoute responds (restart OmniRoute on kooshas-laptop).' -ForegroundColor DarkYellow
            }
        }

        Write-Host '=== do-all: journey mock ===' -ForegroundColor Cyan
        pwsh (Join-Path $RepoRoot 'Tools/verify-journeys.ps1') | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Add-DoAllStage 'journey-mock' 'failed' @{ exitCode = $LASTEXITCODE }
            throw "Journey mock verification failed (exit code $LASTEXITCODE)"
        }
        else { Add-DoAllStage 'journey-mock' 'passed' @{} }

        Write-Host "=== do-all: playcua run-all (retries=$PlaycuaRetries) ===" -ForegroundColor Cyan
        $attempt = 0
        $runOk = $false
        while ($attempt -lt $PlaycuaRetries -and -not $runOk) {
            $attempt++
            if ($attempt -gt 1) {
                Write-Host "playcua retry $attempt/$PlaycuaRetries — relaunch between attempts" -ForegroundColor Yellow
                pwsh (Join-Path $RepoRoot 'Tools/wsm3d.ps1') relaunch -NoBuild | Out-Null
                $retryHealth = Wait-BridgeReady -MaxMinutes 5
                if ($retryHealth -and $retryHealth.bridgeAlive -and -not $retryHealth.isWorld3D) {
                    $bootstrap = Join-Path $RepoRoot 'Tools/wsm3d-playcua/sample-scenarios/bridge-save-load-smoke.yaml'
                    python (Join-Path $RepoRoot 'Tools/wsm3d-playcua/main.py') $bootstrap --vision-backend off 2>&1 | Out-Null
                }
                Start-Sleep -Seconds 30
                $null = Wait-World3D -MaxSeconds 120
            }
            $vb = if ($visionBackendResolved -eq 'omniroute') {
                if ($omnirouteProbeOk) { 'omniroute' } else { 'off' }
            } else { $visionBackendResolved }
            Write-Host "playcua run-all VisionBackend=$vb" -ForegroundColor Gray
            $playcuaExit = 0
            try {
                pwsh (Join-Path $RepoRoot 'Tools/wsm3d.ps1') playcua run-all -VisionBackend $vb 2>&1 | Out-Host
                $playcuaExit = $LASTEXITCODE
            } catch {
                $playcuaExit = 1
                Write-Host "playcua run-all error: $($_.Exception.Message)" -ForegroundColor Yellow
            }
            if ($playcuaExit -eq 0) { $runOk = $true }
        }
        if ($runOk) {
            Add-DoAllStage 'playcua-run-all' 'passed' @{
                attempts       = $attempt
                visionBackend  = $vb
                visionDegraded = [bool]($Vision -and $vb -eq 'off')
            }
            pwsh (Join-Path $RepoRoot 'Tools/sync-playcua-screenshots.ps1') | Out-Host
            $liveArgs = @('-Live', '-SkipOffline')
            if ($vb -ne 'off') { $liveArgs += '-Vision' }
            pwsh (Join-Path $RepoRoot 'Tools/wsm-live-verify.ps1') @liveArgs 2>&1 | Out-Host
            if ($LASTEXITCODE -ne 0) {
                Add-DoAllStage 'live-verify-live' 'failed' @{ exitCode = $LASTEXITCODE }
            } else {
                Add-DoAllStage 'live-verify-live' 'passed' @{}
            }
        } else {
            Add-DoAllStage 'playcua-run-all' 'failed' @{ attempts = $attempt }
            Write-Host '=== do-all: playcua failed — wsm3d doctor (tail) ===' -ForegroundColor Red
            try {
                pwsh (Join-Path $RepoRoot 'Tools/wsm3d.ps1') doctor 2>&1 |
                    ForEach-Object { "$_" } |
                    Select-Object -Last 20 |
                    Out-Host
            } catch {
                Write-Host "doctor skipped: $($_.Exception.Message)" -ForegroundColor DarkYellow
            }
            Write-Host '=== do-all: playcua failed — full offline verify (gates still run) ===' -ForegroundColor Cyan
            pwsh (Join-Path $RepoRoot 'Tools/wsm-live-verify.ps1') | Out-Host
            if ($LASTEXITCODE -ne 0) { Add-DoAllStage 'live-verify-offline' 'failed' @{} }
            else { Add-DoAllStage 'live-verify-offline' 'passed' @{} }
            $offlineVerifyDone = $true
        }
    }

    if (-not $offlineVerifyDone) {
        Write-Host '=== do-all: full offline verify ===' -ForegroundColor Cyan
        pwsh (Join-Path $RepoRoot 'Tools/wsm-live-verify.ps1') | Out-Host
        if ($LASTEXITCODE -ne 0) { Add-DoAllStage 'live-verify-offline' 'failed' @{} }
        else { Add-DoAllStage 'live-verify-offline' 'passed' @{} }
    }

    $auditArgs = @('-Quiet')
    if ($SkipLive) { $auditArgs += '-SkipLive' }
    & (Join-Path $RepoRoot 'Tools/wsm3d-audit-tick.ps1') @auditArgs | Out-Null

    $report.durationMs = [math]::Round(((Get-Date) - $started).TotalMilliseconds, 0)
    $report.finishedAt = (Get-Date).ToUniversalTime().ToString('o')
    $report.summaryLine = Get-DoAllSummaryLine -Report $report
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8

    Write-Host ''
    Write-Host "do-all report: $ReportPath" -ForegroundColor Gray
    Write-Host "overallOk=$($report.overallOk)" -ForegroundColor $(if ($report.overallOk) { 'Green' } else { 'Yellow' })
    if ($report.overallOk) {
        Write-Host $report.summaryLine -ForegroundColor Green
    }
    if (-not $report.overallOk) { exit 1 }
} catch {
    $report.durationMs = [math]::Round(((Get-Date) - $started).TotalMilliseconds, 0)
    $report.finishedAt = (Get-Date).ToUniversalTime().ToString('o')
    $report.summaryLine = Get-DoAllSummaryLine -Report $report
    $report.error = $_.Exception.Message
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    Write-Host "do-all failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "do-all report: $ReportPath" -ForegroundColor Gray
    exit 1
} finally {
    Pop-Location
}
