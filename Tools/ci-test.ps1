<#
.SYNOPSIS
  CI-compatible runtime test runner for WorldSphereMod3D.

.DESCRIPTION
  Orchestrates the full runtime test cycle:
    1. Launches WorldBox via headless-launch.ps1
    2. Waits for the bridge HTTP endpoint
    3. Runs runtime-tests.py against the live game
    4. Captures results (TAP or JUnit XML)
    5. Kills WorldBox
    6. Returns exit code (0 = all pass, 1 = any fail)

  Designed for local CI or self-hosted runners with Steam + WorldBox
  installed. Not suitable for cloud CI (no GPU / no Steam).

.PARAMETER WorldBoxPath
  Path to the WorldBox install directory.

.PARAMETER BridgePort
  Bridge HTTP port (default: 8766).

.PARAMETER BridgeTimeout
  Max seconds to wait for bridge after launch (default: 120).

.PARAMETER Format
  Output format: "tap" or "junit" (default: tap).

.PARAMETER OutputFile
  Write test results to this file. Default: Tools/.reports/runtime-tests-latest.<ext>

.PARAMETER SkipSpawn
  Skip the spawn_units test (saves ~10s).

.PARAMETER SkipScreenshot
  Skip the screenshot visual test.

.PARAMETER SkipLaunch
  Assume WorldBox is already running; skip launch step.

.PARAMETER SkipKill
  Do not kill WorldBox after tests.

.PARAMETER SpawnWait
  Seconds to wait after spawn_units (default: 10).

.PARAMETER PythonPath
  Path to python executable (default: "python").

.EXAMPLE
  ./Tools/ci-test.ps1
  Full cycle: launch, test, kill.

.EXAMPLE
  ./Tools/ci-test.ps1 -SkipLaunch -SkipKill -Format junit -OutputFile results.xml
  Run tests against already-running game, output JUnit XML.

.EXAMPLE
  ./Tools/ci-test.ps1 -SkipSpawn -SkipScreenshot
  Fast smoke test (skip slow tests).
#>

[CmdletBinding()]
param(
    [string]$WorldBoxPath = $(if ($env:WORLDBOX_PATH) { $env:WORLDBOX_PATH } else { "C:/Program Files (x86)/Steam/steamapps/common/Worldbox" }),
    [int]$BridgePort = 8766,
    [int]$BridgeTimeout = 120,
    [ValidateSet("tap", "junit")]
    [string]$Format = "tap",
    [string]$OutputFile = "",
    [switch]$SkipSpawn,
    [switch]$SkipScreenshot,
    [switch]$SkipLaunch,
    [switch]$SkipKill,
    [int]$SpawnWait = 10,
    [string]$PythonPath = "python"
)

$ErrorActionPreference = "Stop"
$script:ToolsDir = $PSScriptRoot
$script:RepoRoot = Split-Path -Parent $ToolsDir
$script:ReportsDir = Join-Path $ToolsDir ".reports"
$script:StartTime = Get-Date

# ── Helpers ──────────────────────────────────────────────────────────────

function Write-Phase ([string]$phase, [string]$msg) {
    Write-Host "[$phase] $msg" -ForegroundColor Cyan
}

function Write-Ok ([string]$msg) {
    Write-Host "[ci-test] $msg" -ForegroundColor Green
}

function Write-Err ([string]$msg) {
    Write-Host "[ci-test] $msg" -ForegroundColor Red
}

function Write-Warn ([string]$msg) {
    Write-Host "[ci-test] $msg" -ForegroundColor Yellow
}

function Get-WorldBoxProcesses {
    $names = @('worldbox', 'WorldBox')
    $found = @()
    foreach ($name in $names) {
        $found += @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    }
    return $found | Sort-Object -Property Id -Unique
}

function Invoke-KillWorldBox {
    $procs = Get-WorldBoxProcesses
    if (-not $procs -or $procs.Count -eq 0) {
        Write-Warn "WorldBox not running (nothing to kill)."
        return
    }

    Write-Phase "cleanup" "Killing $($procs.Count) WorldBox process(es)..."
    foreach ($proc in $procs) {
        try {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        } catch { }
    }

    # Wait for exit
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-WorldBoxProcesses)) {
            Write-Ok "WorldBox terminated."
            return
        }
        # Force kill again
        foreach ($proc in Get-WorldBoxProcesses) {
            try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
        }
        Start-Sleep -Seconds 2
    }
    Write-Warn "WorldBox may still be running after kill timeout."
}

# ── Default output file ─────────────────────────────────────────────────

if (-not $OutputFile) {
    if (-not (Test-Path -LiteralPath $ReportsDir)) {
        New-Item -ItemType Directory -Force -Path $ReportsDir | Out-Null
    }
    $ext = if ($Format -eq "junit") { "xml" } else { "tap" }
    $OutputFile = Join-Path $ReportsDir "runtime-tests-latest.$ext"
}

# Ensure output directory exists
$outputDir = Split-Path -Parent $OutputFile
if ($outputDir -and -not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

# ── Phase 1: Launch ─────────────────────────────────────────────────────

$testExitCode = 1

try {
    if ($SkipLaunch) {
        Write-Phase "launch" "Skipped (-SkipLaunch). Assuming WorldBox is running."
        # Quick bridge check
        try {
            $healthCheck = Invoke-RestMethod -Uri "http://127.0.0.1:$BridgePort/health" -TimeoutSec 5 -ErrorAction Stop
            if ($healthCheck.ok) {
                Write-Ok "Bridge confirmed alive (version=$($healthCheck.version))."
            } else {
                Write-Err "Bridge responded but ok=false. Proceeding anyway."
            }
        } catch {
            Write-Warn "Bridge not immediately reachable; runtime-tests.py will retry."
        }
    } else {
        Write-Phase "launch" "Launching WorldBox via headless-launch.ps1..."
        $launchScript = Join-Path $ToolsDir "headless-launch.ps1"
        & pwsh $launchScript -WorldBoxPath $WorldBoxPath -BridgePort $BridgePort -BridgeTimeout $BridgeTimeout
        $launchExit = $LASTEXITCODE
        if ($launchExit -ne 0) {
            Write-Err "headless-launch.ps1 failed (exit $launchExit)."
            exit 1
        }
    }

    # ── Phase 2: Run tests ──────────────────────────────────────────────────

    Write-Phase "test" "Running runtime-tests.py (format=$Format)..."

    $testScript = Join-Path $ToolsDir "runtime-tests.py"
    $testArgs = @(
        $testScript,
        "--port", $BridgePort,
        "--format", $Format,
        "--output", $OutputFile,
        "--timeout", "30"   # Bridge should already be up; short timeout
    )
    if ($SkipSpawn) { $testArgs += "--skip-spawn" }
    if ($SkipScreenshot) { $testArgs += "--skip-screenshot" }
    if ($SpawnWait -ne 10) { $testArgs += @("--spawn-wait", $SpawnWait) }

    & $PythonPath @testArgs
    $testExitCode = $LASTEXITCODE

    # ── Phase 3: Report ─────────────────────────────────────────────────────

    $elapsed = ((Get-Date) - $StartTime).TotalSeconds
    Write-Host ""

    if (Test-Path -LiteralPath $OutputFile) {
        Write-Phase "results" "Output: $OutputFile"
        # Print first 50 lines of results
        Get-Content -LiteralPath $OutputFile -TotalCount 50 | ForEach-Object { Write-Host "  $_" }
    }

    Write-Host ""
    if ($testExitCode -eq 0) {
        Write-Ok "ALL TESTS PASSED ($("{0:N1}" -f $elapsed)s elapsed)"
    } else {
        Write-Err "SOME TESTS FAILED (exit=$testExitCode, $("{0:N1}" -f $elapsed)s elapsed)"
    }

} finally {
    # ── Phase 4: Cleanup ────────────────────────────────────────────────────

    if (-not $SkipKill) {
        Invoke-KillWorldBox
    } else {
        Write-Phase "cleanup" "Skipped (-SkipKill). WorldBox left running."
    }
}

exit $testExitCode
