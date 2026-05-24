#requires -Version 5.1
<#
.SYNOPSIS
  Semi-deterministic live verification harness for WorldSphereMod3D.

.DESCRIPTION
  Pipeline stages:
    Stage 1: dotnet test (unit, integration, e2e) — fail fast
    Stage 2: phenotype-journey verify --mock (Tools/verify-journeys.ps1)
    Stage 3: [-Live] require bridge on :8766, wsm3d-playcua scenario(s), SSIM vs docs/journeys/phase-previews
    Stage 4: write Tools/.reports/live-verify-latest.json

.PARAMETER Live
  Enable Stage 3 (bridge, playcua, SSIM). Without -Live, Stage 3 is skipped.

.PARAMETER Vision
  Pass --vision-backend omniroute to wsm3d-playcua screenshot checks.

.PARAMETER Phase
  Restrict playcua scenarios and phase-previews SSIM to a single phase number (1-10).
#>
[CmdletBinding()]
param(
    [switch]$Live,
    [switch]$Vision,
    [int]$Phase = 0
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$reportDir = Join-Path $repoRoot "Tools/.reports"
$reportPath = Join-Path $reportDir "live-verify-latest.json"
$ssimThreshold = 0.95
$bridgePort = 8766
$bridgeUrl = "http://127.0.0.1:$bridgePort/health"

$startedAt = (Get-Date).ToUniversalTime().ToString("o")
$report = [ordered]@{
    startedAt  = $startedAt
    live       = [bool]$Live
    vision     = [bool]$Vision
    phase      = if ($Phase -gt 0) { $Phase } else { $null }
    stages     = @()
    overallOk  = $true
}

function Add-StageResult {
    param(
        [string]$Id,
        [string]$Status,
        [hashtable]$Details = @{},
        [double]$DurationMs = 0
    )

    $stage = [ordered]@{
        id          = $Id
        status      = $Status
        durationMs  = [math]::Round($DurationMs, 2)
        details     = $Details
    }
    $report.stages += $stage
    if ($Status -eq "failed") {
        $report.overallOk = $false
    }
}

function Get-StageDetails {
    param($Raw)

    if ($null -eq $Raw) {
        return @{}
    }
    if ($Raw -is [System.Collections.IDictionary]) {
        return $Raw
    }
    if ($Raw -is [System.Array]) {
        $dict = $Raw | Where-Object { $_ -is [System.Collections.IDictionary] } | Select-Object -Last 1
        if ($dict) {
            return $dict
        }
        return @{}
    }
    return @{ value = $Raw }
}

function Invoke-Stage {
    param(
        [string]$Id,
        [scriptblock]$Body
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $details = Get-StageDetails -Raw (& $Body)
        Add-StageResult -Id $Id -Status "passed" -Details $details -DurationMs $sw.Elapsed.TotalMilliseconds
        return $true
    } catch {
        Add-StageResult -Id $Id -Status "failed" -Details @{
            error = $_.Exception.Message
        } -DurationMs $sw.Elapsed.TotalMilliseconds
        return $false
    }
}

function Invoke-SkippedStage {
    param(
        [string]$Id,
        [string]$Reason
    )

    Add-StageResult -Id $Id -Status "skipped" -Details @{ reason = $Reason }
}

function Get-DotnetTestProjects {
    @(
        "tests/WorldSphereMod.Tests.Unit",
        "tests/WorldSphereMod.Tests.Integration",
        "tests/WorldSphereMod.Tests.E2E"
    ) | ForEach-Object {
        $full = Join-Path $repoRoot $_
        if (Test-Path -LiteralPath $full -PathType Container) {
            $full
        }
    }
}

function Test-BridgeHealthy {
    try {
        $response = Invoke-RestMethod -Uri $bridgeUrl -Method Get -TimeoutSec 8
        return [bool]($response -and $response.ok -ne $false)
    } catch {
        return $false
    }
}

function Resolve-PythonCommand {
    foreach ($name in @("python", "python3", "py")) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) {
            return $cmd.Source
        }
    }
    throw "Python not found on PATH (required for playcua and SSIM)."
}

function Resolve-Wsm3dCapture {
    $candidates = @(
        (Join-Path $repoRoot "Tools/wsm3d-capture/target/release/wsm3d-capture.exe"),
        (Join-Path $repoRoot "Tools/wsm3d-capture/target/release/wsm3d-capture"),
        (Join-Path $repoRoot "Tools/wsm3d-capture/target/debug/wsm3d-capture.exe"),
        (Join-Path $repoRoot "Tools/wsm3d-capture/target/debug/wsm3d-capture")
    )
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return $path
        }
    }
    return $null
}

function Invoke-WindowCapture {
    param([string]$OutputPath)

    $parent = Split-Path -Parent $OutputPath
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $captureBin = Resolve-Wsm3dCapture
    if ($captureBin) {
        & $captureBin "worldbox" $OutputPath
        if ($LASTEXITCODE -ne 0) {
            throw "wsm3d-capture failed with exit code $LASTEXITCODE"
        }
        return "wsm3d-capture"
    }

    $wsm3d = Join-Path $repoRoot "Tools/wsm3d.ps1"
    if (-not (Test-Path -LiteralPath $wsm3d)) {
        throw "Neither wsm3d-capture nor Tools/wsm3d.ps1 is available."
    }

    $shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { "pwsh" } else { "powershell.exe" }
    & $shell -NoLogo -NoProfile -File $wsm3d screenshot -Path $OutputPath -WindowOnly
    if ($LASTEXITCODE -ne 0) {
        throw "wsm3d.ps1 screenshot failed with exit code $LASTEXITCODE"
    }
    return "wsm3d.ps1"
}

function Get-PlaycuaScenarios {
    $scenarioRoot = Join-Path $repoRoot "Tools/wsm3d-playcua/sample-scenarios"
    if (-not (Test-Path -LiteralPath $scenarioRoot)) {
        return @()
    }

    $files = Get-ChildItem -Path $scenarioRoot -Filter "*.yaml" -File | Sort-Object Name
    if ($Phase -le 0) {
        return $files
    }

    return $files | Where-Object { $_.BaseName -match "^phase-$Phase-" }
}

function Get-PhasePreviewDirectories {
    $previewRoot = Join-Path $repoRoot "docs/journeys/phase-previews"
    if (-not (Test-Path -LiteralPath $previewRoot)) {
        return @()
    }

    $dirs = Get-ChildItem -Path $previewRoot -Directory |
        Where-Object { $_.Name -match "^phase-\d+" } |
        Sort-Object Name

    if ($Phase -le 0) {
        return $dirs
    }

    return $dirs | Where-Object { $_.Name -match "^phase-$Phase-" }
}

function Invoke-SsimCompare {
    param(
        [string]$ExpectedPath,
        [string]$ActualPath
    )

    $python = Resolve-PythonCommand
    $compareScript = Join-Path $repoRoot "Tools/wsm-ssim-compare.py"
    $json = & $python $compareScript --expected $ExpectedPath --actual $ActualPath --threshold $ssimThreshold
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1) {
        throw "wsm-ssim-compare failed with exit code $LASTEXITCODE : $json"
    }

    $parsed = $json | ConvertFrom-Json
    return @{
        ok        = [bool]$parsed.ok
        ssim      = [double]$parsed.ssim
        threshold = [double]$parsed.threshold
        expected  = [string]$parsed.expected
        actual    = [string]$parsed.actual
    }
}

function Write-ReportAndExit {
    param([int]$ExitCode)

    if (-not (Test-Path -LiteralPath $reportDir)) {
        New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
    }

    $report.finishedAt = (Get-Date).ToUniversalTime().ToString("o")
    $json = $report | ConvertTo-Json -Depth 12
    Set-Content -LiteralPath $reportPath -Value $json -Encoding utf8
    Write-Host "Report: $reportPath"
    exit $ExitCode
}

# Stage 1: dotnet test (unit, integration, e2e) — fail fast
$stage1Ok = Invoke-Stage -Id "dotnet-tests" -Body {
    $projects = Get-DotnetTestProjects
    if (-not $projects -or $projects.Count -eq 0) {
        throw "No dotnet test projects found under tests/."
    }

    $runs = @()
    foreach ($project in $projects) {
        Write-Host ("dotnet test " + (Split-Path -Leaf $project) + " ...")
        Push-Location $repoRoot
        try {
            & dotnet test $project -c Release --nologo | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet test failed for $project (exit $LASTEXITCODE)"
            }
        } finally {
            Pop-Location
        }
        $runs += (Split-Path -Leaf $project)
    }

    return @{ projects = $runs }
}

if (-not $stage1Ok) {
    Write-ReportAndExit 1
}

# Stage 2: phenotype-journey verify --mock (via Tools/verify-journeys.ps1)
$verifyScript = Join-Path $repoRoot "Tools/verify-journeys.ps1"
$stage2Ok = Invoke-Stage -Id "journey-mock-verify" -Body {
    if (-not (Test-Path -LiteralPath $verifyScript)) {
        throw "Missing Tools/verify-journeys.ps1"
    }

    & powershell.exe -NoLogo -NoProfile -File $verifyScript | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "verify-journeys.ps1 failed with exit code $LASTEXITCODE"
    }

    return @{ script = "Tools/verify-journeys.ps1"; mode = "mock" }
}

if (-not $stage2Ok) {
    Write-ReportAndExit 1
}

# Stage 3: [-Live] bridge :8766, wsm3d-playcua, SSIM vs docs/journeys/phase-previews
if (-not $Live) {
    Invoke-SkippedStage -Id "live-playcua-ssim" -Reason "Pass -Live to require bridge, run playcua, and SSIM-compare phase previews."
} else {
    $liveSw = [System.Diagnostics.Stopwatch]::StartNew()
    $liveDetails = @{
        bridgePort     = $bridgePort
        playcuaRuns    = @()
        ssimComparisons = @()
    }
    $liveFailed = $false
    $liveError = $null

    try {
        if (-not (Test-BridgeHealthy)) {
            throw "Bridge health check failed at $bridgeUrl (start MCP/bridge on port $bridgePort)."
        }
        $liveDetails.bridgeHealthy = $true

        $python = Resolve-PythonCommand
        $playcuaMain = Join-Path $repoRoot "Tools/wsm3d-playcua/main.py"
        if (-not (Test-Path -LiteralPath $playcuaMain)) {
            throw "Missing Tools/wsm3d-playcua/main.py"
        }

        $scenarios = Get-PlaycuaScenarios
        if (-not $scenarios -or $scenarios.Count -eq 0) {
            throw "No playcua YAML scenarios found for the requested phase filter."
        }

        $artifactRoot = Join-Path $repoRoot "Tools/wsm3d-playcua/.reports/live-verify-artifacts"
        if (-not (Test-Path -LiteralPath $artifactRoot)) {
            New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
        }

        foreach ($scenario in $scenarios) {
            $scenarioReport = Join-Path $artifactRoot ("playcua-" + $scenario.BaseName + ".json")
            $args = @(
                $playcuaMain,
                $scenario.FullName,
                "--port", "$bridgePort",
                "--report", $scenarioReport
            )
            if ($Vision) {
                $args += @("--vision-backend", "omniroute")
            }

            Write-Host ("playcua " + $scenario.Name + " ...")
            & $python @args
            $exit = $LASTEXITCODE
            $run = [ordered]@{
                scenario = $scenario.Name
                exitCode = $exit
                report   = $scenarioReport
            }
            if (Test-Path -LiteralPath $scenarioReport) {
                $run.reportJson = (Get-Content -LiteralPath $scenarioReport -Raw | ConvertFrom-Json)
            }
            $liveDetails.playcuaRuns += $run
            if ($exit -ne 0) {
                throw "playcua scenario failed: $($scenario.Name) (exit $exit)"
            }
        }

        $captureRoot = Join-Path $artifactRoot "ssim-captures"
        if (-not (Test-Path -LiteralPath $captureRoot)) {
            New-Item -ItemType Directory -Force -Path $captureRoot | Out-Null
        }

        foreach ($phaseDir in Get-PhasePreviewDirectories) {
            $afterFixture = Join-Path $phaseDir.FullName "after.png"
            $beforeFixture = Join-Path $phaseDir.FullName "before.png"
            if (-not (Test-Path -LiteralPath $afterFixture)) {
                $liveDetails.ssimComparisons += [ordered]@{
                    phase  = $phaseDir.Name
                    status = "skipped_no_fixture"
                }
                continue
            }

            $capturePath = Join-Path $captureRoot ($phaseDir.Name + "-live.png")
            $captureTool = $null
            try {
                $captureTool = Invoke-WindowCapture -OutputPath $capturePath
            } catch {
                $liveDetails.ssimComparisons += [ordered]@{
                    phase   = $phaseDir.Name
                    fixture = "after.png"
                    status  = "skipped_capture_failed"
                    error   = $_.Exception.Message
                }
                continue
            }

            if (-not (Test-Path -LiteralPath $capturePath -PathType Leaf)) {
                $liveDetails.ssimComparisons += [ordered]@{
                    phase   = $phaseDir.Name
                    fixture = "after.png"
                    status  = "skipped_capture_failed"
                    error   = "Capture path missing after capture tool reported success."
                }
                continue
            }

            $compare = Invoke-SsimCompare -ExpectedPath $afterFixture -ActualPath $capturePath
            $entry = [ordered]@{
                phase       = $phaseDir.Name
                fixture     = "after.png"
                captureTool = $captureTool
                capture     = $capturePath
                compare     = $compare
                status      = if ($compare.ok) { "passed" } else { "failed" }
            }
            $liveDetails.ssimComparisons += $entry
            if (-not $compare.ok) {
                throw ("SSIM below threshold for " + $phaseDir.Name + ": " + $compare.ssim)
            }

            if (Test-Path -LiteralPath $beforeFixture) {
                $liveDetails.ssimComparisons += [ordered]@{
                    phase   = $phaseDir.Name
                    fixture = "before.png"
                    status  = "skipped_requires_baseline_state"
                    note    = "before.png exists; harness compares after.png post-scenario only."
                }
            }
        }

        Add-StageResult -Id "live-playcua-ssim" -Status "passed" -Details $liveDetails -DurationMs $liveSw.Elapsed.TotalMilliseconds
    } catch {
        $liveFailed = $true
        $liveError = $_.Exception.Message
        if ($liveError) {
            $liveDetails.error = $liveError
        }
        Add-StageResult -Id "live-playcua-ssim" -Status "failed" -Details $liveDetails -DurationMs $liveSw.Elapsed.TotalMilliseconds
    }

    if ($liveFailed) {
        Write-ReportAndExit 1
    }
}

# Stage 4: write Tools/.reports/live-verify-latest.json
Write-ReportAndExit $(if ($report.overallOk) { 0 } else { 1 })
