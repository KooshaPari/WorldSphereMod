<#
.SYNOPSIS
  Launch WorldBox for automated testing without Steam Guard interaction.

.DESCRIPTION
  Attempts to launch WorldBox via three strategies in order:
    1. Direct executable (no Steam overlay) — works if Steam is already
       running and the user has previously authenticated.
    2. Steam protocol URI (steam://rungameid/1206560) — delegates to
       the running Steam client.
    3. SteamCMD headless (app_run 1206560) — requires SteamCMD installed
       and cached credentials.

  The script waits until the WorldBox process is detected or a timeout
  expires, then verifies the bridge HTTP endpoint is reachable.

  Returns exit code 0 if WorldBox is running and bridge is alive,
  1 if launch failed, 2 if bridge never came up.

.PARAMETER WorldBoxPath
  Path to the WorldBox install directory.

.PARAMETER BridgePort
  Bridge HTTP port to wait for (default: 8766).

.PARAMETER BridgeTimeout
  Max seconds to wait for bridge /health (default: 120).

.PARAMETER ProcessTimeout
  Max seconds to wait for worldbox.exe process (default: 60).

.PARAMETER Strategy
  Force a specific launch strategy: "exe", "steam", "steamcmd", or "auto" (default).

.PARAMETER SteamCmdPath
  Path to steamcmd.exe for strategy 3.

.EXAMPLE
  ./Tools/headless-launch.ps1
  Launches WorldBox using the first working strategy.

.EXAMPLE
  ./Tools/headless-launch.ps1 -Strategy exe -BridgeTimeout 90
  Forces direct-exe launch with a 90s bridge wait.
#>

[CmdletBinding()]
param(
    [string]$WorldBoxPath = $(if ($env:WORLDBOX_PATH) { $env:WORLDBOX_PATH } else { "C:/Program Files (x86)/Steam/steamapps/common/Worldbox" }),
    [int]$BridgePort = 8766,
    [int]$BridgeTimeout = 120,
    [int]$ProcessTimeout = 60,
    [ValidateSet("auto", "exe", "steam", "steamcmd")]
    [string]$Strategy = "auto",
    [string]$SteamCmdPath = ""
)

$ErrorActionPreference = "Stop"

# ── Helpers ──────────────────────────────────────────────────────────────

function Write-Info  ([string]$msg) { Write-Host "[headless] $msg" -ForegroundColor Cyan }
function Write-Ok    ([string]$msg) { Write-Host "[headless] $msg" -ForegroundColor Green }
function Write-Warn  ([string]$msg) { Write-Host "[headless] $msg" -ForegroundColor Yellow }
function Write-Err   ([string]$msg) { Write-Host "[headless] $msg" -ForegroundColor Red }

function Get-WorldBoxProcesses {
    $names = @('worldbox', 'WorldBox')
    $found = @()
    foreach ($name in $names) {
        $found += @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    }
    return $found | Sort-Object -Property Id -Unique
}

function Wait-ForProcess {
    param([int]$MaxSeconds)
    $deadline = (Get-Date).AddSeconds($MaxSeconds)
    while ((Get-Date) -lt $deadline) {
        $procs = Get-WorldBoxProcesses
        if ($procs -and $procs.Count -gt 0) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Wait-ForBridge {
    param([int]$Port, [int]$MaxSeconds)
    $url = "http://127.0.0.1:$Port/health"
    $deadline = (Get-Date).AddSeconds($MaxSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $resp = Invoke-RestMethod -Uri $url -TimeoutSec 5 -ErrorAction Stop
            if ($resp.ok -eq $true) {
                return $resp
            }
        } catch {
            # Bridge not up yet
        }
        Start-Sleep -Seconds 3
    }
    return $null
}

function Test-SteamRunning {
    $steam = @(Get-Process -Name "steam" -ErrorAction SilentlyContinue)
    return $steam.Count -gt 0
}

# ── Launch strategies ────────────────────────────────────────────────────

function Invoke-LaunchExe {
    $exe = Join-Path $WorldBoxPath "worldbox.exe"
    if (-not (Test-Path -LiteralPath $exe)) {
        Write-Warn "worldbox.exe not found at $exe"
        return $false
    }

    Write-Info "Strategy: direct executable -> $exe"

    # Check if Steam is running (required for Steamworks init even with direct exe)
    if (-not (Test-SteamRunning)) {
        Write-Warn "Steam not running. Direct exe may fail Steamworks init."
        Write-Info "Attempting to start Steam silently..."
        $steamExe = "C:/Program Files (x86)/Steam/steam.exe"
        if (Test-Path -LiteralPath $steamExe) {
            Start-Process -FilePath $steamExe -ArgumentList "-silent" -WindowStyle Minimized
            Start-Sleep -Seconds 8
        }
    }

    Start-Process -FilePath $exe -WorkingDirectory $WorldBoxPath
    return $true
}

function Invoke-LaunchSteam {
    Write-Info "Strategy: Steam protocol URI (steam://rungameid/1206560)"

    if (-not (Test-SteamRunning)) {
        Write-Warn "Steam not running. Starting Steam first..."
        $steamExe = "C:/Program Files (x86)/Steam/steam.exe"
        if (Test-Path -LiteralPath $steamExe) {
            Start-Process -FilePath $steamExe -ArgumentList "-silent" -WindowStyle Minimized
            Start-Sleep -Seconds 10
        } else {
            Write-Warn "steam.exe not found at default path"
            return $false
        }
    }

    Start-Process "steam://rungameid/1206560"
    return $true
}

function Invoke-LaunchSteamCmd {
    $cmd = $SteamCmdPath
    if (-not $cmd) {
        # Check common locations
        $candidates = @(
            "C:/SteamCMD/steamcmd.exe",
            "C:/steamcmd/steamcmd.exe",
            "$env:USERPROFILE/steamcmd/steamcmd.exe",
            "$env:ProgramFiles/SteamCMD/steamcmd.exe"
        )
        foreach ($c in $candidates) {
            if (Test-Path -LiteralPath $c) { $cmd = $c; break }
        }
    }

    if (-not $cmd -or -not (Test-Path -LiteralPath $cmd)) {
        Write-Warn "SteamCMD not found. Install it or pass -SteamCmdPath."
        return $false
    }

    Write-Info "Strategy: SteamCMD headless -> $cmd"
    Write-Warn "SteamCMD requires cached credentials. If prompted, this will hang."

    # SteamCMD: login anonymous won't work for a paid game.
    # Use cached credentials (requires prior manual login).
    Start-Process -FilePath $cmd -ArgumentList "+login", "anonymous", "+app_run", "1206560", "+quit" -NoNewWindow
    return $true
}

# ── Main ─────────────────────────────────────────────────────────────────

# Already running?
$existingProcs = Get-WorldBoxProcesses
if ($existingProcs -and $existingProcs.Count -gt 0) {
    Write-Info "WorldBox already running (PID: $($existingProcs[0].Id)). Skipping launch."
} else {
    Write-Info "WorldBox not running. Launching..."

    $launched = $false
    $strategies = switch ($Strategy) {
        "exe"      { @("exe") }
        "steam"    { @("steam") }
        "steamcmd" { @("steamcmd") }
        default    { @("exe", "steam", "steamcmd") }
    }

    foreach ($s in $strategies) {
        Write-Info "Trying strategy: $s"
        $launched = switch ($s) {
            "exe"      { Invoke-LaunchExe }
            "steam"    { Invoke-LaunchSteam }
            "steamcmd" { Invoke-LaunchSteamCmd }
        }
        if ($launched) { break }
        Write-Warn "Strategy '$s' failed, trying next..."
    }

    if (-not $launched) {
        Write-Err "All launch strategies failed."
        exit 1
    }

    # Wait for process
    Write-Info "Waiting for WorldBox process (timeout: ${ProcessTimeout}s)..."
    if (-not (Wait-ForProcess -MaxSeconds $ProcessTimeout)) {
        Write-Err "WorldBox process did not appear within ${ProcessTimeout}s."
        exit 1
    }

    $proc = (Get-WorldBoxProcesses)[0]
    Write-Ok "WorldBox process detected (PID: $($proc.Id))."
}

# Wait for bridge
Write-Info "Waiting for bridge on port $BridgePort (timeout: ${BridgeTimeout}s)..."
$health = Wait-ForBridge -Port $BridgePort -MaxSeconds $BridgeTimeout
if (-not $health) {
    Write-Err "Bridge did not respond on port $BridgePort within ${BridgeTimeout}s."
    Write-Warn "WorldBox may be running but the mod bridge is not active."
    Write-Warn "Check Player.log: $env:USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log"
    exit 2
}

Write-Ok "Bridge alive: version=$($health.version) isWorld3D=$($health.isWorld3D)"
Write-Ok "WorldBox is ready for testing."
exit 0
