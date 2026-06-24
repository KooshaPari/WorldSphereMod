<#
.SYNOPSIS
  WSM3D L1 deterministic in-game pixel-verification harness.

.DESCRIPTION
  Boots the in-game bridge, drives a deterministic camera tour, captures
  one or more PNGs per P0 fix, runs Tools/pixel-verify.py against them,
  and aggregates a JSON report at <out-dir>/report.json.  Failures in
  any single step are isolated (captured into report.json); the run
  always finishes.

  Pure pwsh; no git operations; no production-code edits.

.PARAMETER OutDir
  Output directory (default: docs/journeys/scratch/l1-verify/<timestamp>).

.PARAMETER BridgeUrl
  Base URL of the in-game bridge (default: http://127.0.0.1:8766).

.PARAMETER SkipLaunch
  Skip kill/build/install/launch sequence; assume a bridge is already up.

.PARAMETER PythonExe
  Python interpreter to invoke pixel-verify.py with (default: python).

.PARAMETER TimeoutSec
  Per-step HTTP timeout (default: 30).

.PARAMETER StepTimeoutSec
  Overall hard timeout (default: 600).  The script exits 124 if exceeded.

.EXAMPLE
  pwsh Tools/l1-verify.ps1
  Boots the bridge, captures + verifies all 8 P0 checks, writes report.json.

.EXAMPLE
  pwsh Tools/l1-verify.ps1 -SkipLaunch
  Assumes a bridge is already up at 127.0.0.1:8766.
#>

[CmdletBinding()]
param(
    [string]$OutDir = "",
    [string]$BridgeUrl = "http://127.0.0.1:8766",
    [switch]$SkipLaunch,
    [string]$PythonExe = "python",
    [int]$TimeoutSec = 30,
    [int]$StepTimeoutSec = 600
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# ---------------------------------------------------------------------------
# Paths (hard-coded project layout; do not relocate)
# ---------------------------------------------------------------------------

$ScriptDir       = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot        = Split-Path -Parent $ScriptDir
$ToolsDir        = $ScriptDir
$InstallScript   = Join-Path $ToolsDir "install.ps1"
$Wsm3dScript     = Join-Path $ToolsDir "wsm3d.ps1"
$PixelVerifyPy   = Join-Path $ToolsDir "pixel-verify.py"
$WorldBoxExe     = "C:/Program Files (x86)/Steam/steamapps/common/Worldbox/worldbox.exe"
$ModDst          = "C:/Program Files (x86)/Steam/steamapps/common/Worldbox/worldbox_Data/StreamingAssets/Mods/WorldSphereMod"
$PlayerLog       = Join-Path $env:LOCALAPPDATA "..\LocalLow\mkarpenko\WorldBox\Player.log"
if (-not (Test-Path $PlayerLog)) {
    $PlayerLog = "$env:USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log"
}
$ScratchRoot     = Join-Path $RepoRoot "docs/journeys/scratch/l1-verify"

if (-not $OutDir) {
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutDir = Join-Path $ScratchRoot $timestamp
}

# ---------------------------------------------------------------------------
# Step 0: preconditions
# ---------------------------------------------------------------------------

function Test-Preconditions {
    if (-not (Test-Path $PixelVerifyPy)) {
        Write-Host "[l1-verify] FATAL: pixel-verify.py not found at $PixelVerifyPy" -ForegroundColor Red
        return $false
    }
    try {
        $pyVer = & $PythonExe --version 2>&1
        if ($LASTEXITCODE -ne 0) { throw "python not runnable" }
        Write-Host "[l1-verify] python = $pyVer"
    } catch {
        Write-Host "[l1-verify] FATAL: python not on PATH (set -PythonExe)" -ForegroundColor Red
        return $false
    }
    if (-not $SkipLaunch) {
        if (-not (Test-Path $InstallScript)) {
            Write-Host "[l1-verify] FATAL: install.ps1 not found at $InstallScript" -ForegroundColor Red
            return $false
        }
    }
    return $true
}

# ---------------------------------------------------------------------------
# Bridge HTTP helper (retry-once on 5xx, 30s default)
# ---------------------------------------------------------------------------

function Invoke-BridgeJson {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [hashtable]$Body = $null,
        [int]$Timeout = $TimeoutSec
    )
    $uri = "$BridgeUrl$Path"
    $attempt = 0
    while ($attempt -lt 2) {
        $attempt++
        try {
            $params = @{
                Uri         = $uri
                Method      = $Method
                TimeoutSec  = $Timeout
                ContentType = "application/json"
            }
            if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 4 -Compress) }
            $resp = Invoke-RestMethod @params -ErrorAction Stop
            return @{ ok = $true; data = $resp; status = 200; attempt = $attempt }
        } catch {
            $err = $_.Exception.Message
            $code = 0
            if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
            $is5xx = ($code -ge 500 -and $code -lt 600)
            if ($is5xx -and $attempt -lt 2) {
                Write-Host "[l1-verify]   $Method $Path -> $code, retrying once..." -ForegroundColor Yellow
                Start-Sleep -Seconds 2
                continue
            }
            return @{ ok = $false; error = $err; status = $code; attempt = $attempt }
        }
    }
}

function Get-BridgeHealth {
    $r = Invoke-BridgeJson -Method GET -Path "/health" -Timeout 5
    if ($r.ok) {
        # Normalize to hashtable so downstream [hashtable]-typed params accept it
        $h = @{}
        if ($r.data) {
            foreach ($p in $r.data.PSObject.Properties) { $h[$p.Name] = $p.Value }
        }
        return $h
    }
    return $null
}

# ---------------------------------------------------------------------------
# Step 1: bring bridge up
# ---------------------------------------------------------------------------

function Wait-BridgeHealthy {
    param([int]$MaxSeconds = 90)
    $deadline = (Get-Date).AddSeconds($MaxSeconds)
    while ((Get-Date) -lt $deadline) {
        $h = Get-BridgeHealth
        if ($h -and $h.ok) { return $h }
        Start-Sleep -Seconds 2
    }
    return $null
}

function Ensure-BridgeUp {
    Write-Host "[l1-verify] Step 1: ensure bridge is up at $BridgeUrl" -ForegroundColor Cyan
    $h = Get-BridgeHealth
    if ($h -and $h.ok) {
        Write-Host "[l1-verify]   bridge already up (v$($h.version))" -ForegroundColor Green
        return $h
    }
    if ($SkipLaunch) {
        Write-Host "[l1-verify]   -SkipLaunch set, bridge not reachable; aborting" -ForegroundColor Red
        return $null
    }
    # Kill any running worldbox
    $procs = Get-Process -Name worldbox, WorldBox -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "[l1-verify]   killing $($procs.Count) existing worldbox process(es)" -ForegroundColor Yellow
        $procs | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline) {
            if (-not (Get-Process -Name worldbox, WorldBox -ErrorAction SilentlyContinue)) { break }
            Start-Sleep -Seconds 1
        }
    }
    # Wipe NML compiled cache to avoid stale-DLL trap
    $nmCache = "$env:APPDATA/../LocalLow/mkarpenko/WorldBox/NML_cache"
    if (Test-Path $nmCache) {
        try {
            Remove-Item -Recurse -Force $nmCache -ErrorAction SilentlyContinue
            Write-Host "[l1-verify]   cleared NML cache: $nmCache"
        } catch { Write-Host "[l1-verify]   NML cache clear warning: $_" -ForegroundColor Yellow }
    }
    # Install
    Write-Host "[l1-verify]   running install.ps1 (skip-build for speed)..." -ForegroundColor Cyan
    try {
        & pwsh -NoProfile -File $InstallScript -SkipBuild -ErrorAction Stop
        if ($LASTEXITCODE -ne 0) { throw "install.ps1 exited $LASTEXITCODE" }
    } catch {
        Write-Host "[l1-verify]   install.ps1 FAILED: $_" -ForegroundColor Red
        return $null
    }
    # Launch
    if (-not (Test-Path $WorldBoxExe)) {
        Write-Host "[l1-verify]   worldbox.exe not found at $WorldBoxExe" -ForegroundColor Red
        return $null
    }
    Write-Host "[l1-verify]   launching worldbox.exe..." -ForegroundColor Cyan
    Start-Process -FilePath $WorldBoxExe -WorkingDirectory (Split-Path -Parent $WorldBoxExe) -ErrorAction SilentlyContinue | Out-Null
    Write-Host "[l1-verify]   waiting for bridge /health (up to 90s)..."
    $h = Wait-BridgeHealthy -MaxSeconds 90
    if (-not $h) {
        Write-Host "[l1-verify]   bridge never came up; tailing Player.log tail" -ForegroundColor Red
        if (Test-Path $PlayerLog) {
            Get-Content $PlayerLog -Tail 40 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }
        }
        return $null
    }
    Write-Host "[l1-verify]   bridge is up (v$($h.version), isWorld3D=$($h.isWorld3D))" -ForegroundColor Green
    return $h
}

# ---------------------------------------------------------------------------
# Step 2: create a fresh world (best-effort; tolerate failures)
# ---------------------------------------------------------------------------

function Initialize-World {
    param([hashtable]$Health)
    Write-Host "[l1-verify] Step 2: drive fresh world" -ForegroundColor Cyan
    $result = @{ seed = "default"; size = 316; isWorld3D = $Health.isWorld3D }
    # Try /actions/new_world, fall back to /actions/regenerate
    $r = Invoke-BridgeJson -Method POST -Path "/actions/new_world" -Timeout 20
    if (-not $r.ok) {
        $r = Invoke-BridgeJson -Method POST -Path "/actions/regenerate" -Timeout 20
    }
    if ($r.ok) {
        Write-Host "[l1-verify]   new_world/regenerate ok" -ForegroundColor Green
    } else {
        Write-Host "[l1-verify]   world init soft-fail: $($r.error)" -ForegroundColor Yellow
    }
    # Sample /world/state for actual seed
    $ws = Invoke-BridgeJson -Method GET -Path "/world/state" -Timeout 10
    if ($ws.ok -and $ws.data) {
        if ($ws.data.seed) { $result.seed = $ws.data.seed }
        if ($ws.data.worldSize) { $result.size = $ws.data.worldSize }
    }
    # Wait for world to be ready
    Start-Sleep -Seconds 3
    return $result
}

# ---------------------------------------------------------------------------
# Step 3: drive camera + capture for one P0 entry
# ---------------------------------------------------------------------------

function Capture-P0 {
    param(
        [Parameter(Mandatory)][string]$P0Id,
        [Parameter(Mandatory)][hashtable]$Spec,
        [Parameter(Mandatory)][string]$OutRoot
    )
    $entry = @{
        p0 = $P0Id
        title = $Spec.title
        files = @()
        checks = @{}
        verdict = "inconclusive"
        errors = @()
    }
    $p0Dir = Join-Path $OutRoot $P0Id
    New-Item -ItemType Directory -Force -Path $p0Dir -ErrorAction SilentlyContinue | Out-Null

    # Pre-capture hook
    if ($Spec.pre_actions) {
        foreach ($a in $Spec.pre_actions) {
            $r = Invoke-BridgeJson -Method POST -Path $a.path -Body $a.body -Timeout 20
            if (-not $r.ok) { $entry.errors += "pre $($a.path): $($r.error)" }
            Start-Sleep -Milliseconds ($a.delay_ms -as [int] -or 200)
        }
    }

    # Camera setup (sequence of actions)
    if ($Spec.camera) {
        foreach ($c in $Spec.camera) {
            $body = @{ x = $c.x; y = $c.y; zoom = $c.zoom } | ConvertTo-Json -Compress
            $r = Invoke-BridgeJson -Method POST -Path "/actions/camera" -Body @{ x = $c.x; y = $c.y; zoom = $c.zoom }
            if (-not $r.ok) { $entry.errors += "camera: $($r.error)" }
            Start-Sleep -Milliseconds ($c.delay_ms -as [int] -or 600)
        }
    }

    # Captures (one or more PNGs)
    foreach ($cap in $Spec.captures) {
        $fileName = $cap.file
        $filePath = Join-Path $p0Dir $fileName
        $r = Invoke-BridgeJson -Method POST -Path "/actions/screenshot" -Body @{ path = $filePath; mode = $cap.mode }
        if ($r.ok) {
            $entry.files += $filePath
        } else {
            $entry.errors += "screenshot $fileName`: $($r.error)"
        }
        Start-Sleep -Milliseconds ($cap.delay_ms -as [int] -or 400)
    }

    if ($entry.files.Count -eq 0) {
        $entry.verdict = "inconclusive"
        $entry.errors += "no files captured"
        return $entry
    }

    # Run check
    $checkName = $Spec.check
    $checkArgs = @{}
    if ($checkName -eq "lod_flash_diff") {
        if ($entry.files.Count -lt 2) {
            $entry.errors += "lod_flash_diff needs 2 files; got $($entry.files.Count)"
            $entry.verdict = "inconclusive"
            return $entry
        }
        $pyArgs = @(
            $PixelVerifyPy, "check", $checkName,
            "--png", $entry.files[0],
            "--png-b", $entry.files[1]
        )
    } else {
        $pyArgs = @($PixelVerifyPy, "check", $checkName, "--png", $entry.files[0])
    }
    if ($Spec.check_args) {
        foreach ($k in $Spec.check_args.Keys) { $pyArgs += "--$k"; $pyArgs += [string]$Spec.check_args[$k] }
    }
    Write-Host "[l1-verify]   check $checkName on $P0Id..." -ForegroundColor DarkCyan
    try {
        $proc = Start-Process -FilePath $PythonExe -ArgumentList $pyArgs -NoNewWindow -RedirectStandardOutput "$p0Dir/check.stdout.json" -RedirectStandardError "$p0Dir/check.stderr.log" -Wait -PassThru
        $stdoutText = if (Test-Path "$p0Dir/check.stdout.json") { Get-Content "$p0Dir/check.stdout.json" -Raw -ErrorAction SilentlyContinue } else { "" }
        $stderrText = if (Test-Path "$p0Dir/check.stderr.log") { Get-Content "$p0Dir/check.stderr.log" -Raw -ErrorAction SilentlyContinue } else { "" }
        $entry.checks = @{
            exit_code = $proc.ExitCode
            stdout = $stdoutText
            stderr = $stderrText
        }
        if ($stdoutText) {
            try { $entry.checks.parsed = $stdoutText | ConvertFrom-Json -ErrorAction Stop } catch { }
        }
        if ($proc.ExitCode -eq 0) {
            $entry.verdict = "pass"
        } elseif ($proc.ExitCode -eq 1) {
            $entry.verdict = "fail"
        } elseif ($proc.ExitCode -eq 2) {
            $entry.verdict = "inconclusive"
        } else {
            $entry.verdict = "inconclusive"
            $entry.errors += "pixel-verify exit=$($proc.ExitCode)"
        }
    } catch {
        $entry.verdict = "inconclusive"
        $entry.errors += "pixel-verify threw: $_"
    }
    return $entry
}

# ---------------------------------------------------------------------------
# P0 spec table
# ---------------------------------------------------------------------------

$P0Specs = @{
    "P0-1" = @{
        title = "Actor silhouette complexity (2D billboard vs 3D voxel mesh)"
        camera = @(@{ x = 0; y = 0; zoom = 15; delay_ms = 1500 })
        captures = @(
            @{ file = "default.png"; mode = "camera"; delay_ms = 400 },
            @{ file = "frame2.png";  mode = "camera"; delay_ms = 400 }
        )
        check = "actor_silhouette_complexity"
    }
    "P0-2" = @{
        title = "Trees/rocks/foliage/buildings oversized sprites"
        camera = @(@{ x = 0; y = 0; zoom = 12; delay_ms = 1500 })
        captures = @(@{ file = "trees.png"; mode = "camera"; delay_ms = 500 })
        check = "actor_silhouette_complexity"
    }
    "P0-3" = @{
        title = "LOD flash: frame-to-frame diff under threshold"
        camera = @(@{ x = 0; y = 0; zoom = 15; delay_ms = 1500 })
        captures = @(
            @{ file = "flash1.png"; mode = "camera"; delay_ms = 200 },
            @{ file = "flash2.png"; mode = "camera"; delay_ms = 200 }
        )
        check = "lod_flash_diff"
    }
    "P0-4" = @{
        title = "Mod fails to load (bridge /health + render_stats reachable)"
        camera = $null
        captures = @()
        check = "actor_silhouette_complexity"  # never run; verdict from bridge state
    }
    "P0-5" = @{
        title = "Brush icons + kingdom zones visible at close + far zoom"
        camera = @(
            @{ x = 100; y = 100; zoom = 25; delay_ms = 1500 }
        )
        pre_actions = @(
            @{ path = "/actions/select_tool"; body = @{ id = "zone_kingdom" }; delay_ms = 400 }
        )
        captures = @(
            @{ file = "brush-close.png"; mode = "camera"; delay_ms = 400 }
        )
        check = "brush_visibility_alpha"
    }
    "P0-5b" = @{
        title = "Brush + kingdom zones at far zoom (out)"
        camera = @(
            @{ x = 100; y = 100; zoom = 8; delay_ms = 1500 }
        )
        pre_actions = @(
            @{ path = "/actions/select_tool"; body = @{ id = "zone_kingdom" }; delay_ms = 400 }
        )
        captures = @(
            @{ file = "brush-out.png"; mode = "camera"; delay_ms = 400 }
        )
        check = "brush_visibility_alpha"
    }
    "P0-6" = @{
        title = "Settings UI panel (top-right quad)"
        camera = @(@{ x = 0; y = 0; zoom = 18; delay_ms = 1500 })
        pre_actions = @(
            @{ path = "/actions/close_dialog"; body = @{}; delay_ms = 400 }
        )
        captures = @(@{ file = "menu.png"; mode = "screen"; delay_ms = 500 })
        check = "ui_panel_ratio"
    }
    "P0-7" = @{
        title = "Terrain biome color variance (basic RGB not gray)"
        camera = @(@{ x = 200; y = 200; zoom = 12; delay_ms = 1500 })
        captures = @(@{ file = "terrain.png"; mode = "camera"; delay_ms = 500 })
        check = "biome_color_variance"
    }
    "P0-8" = @{
        title = "Water sunken: blue uniform Y (low variance)"
        camera = @(@{ x = 200; y = 200; zoom = 5; delay_ms = 1500 })
        captures = @(@{ file = "water.png"; mode = "camera"; delay_ms = 500 })
        check = "water_uniform_y_blue"
    }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if (-not (Test-Preconditions)) { exit 2 }

New-Item -ItemType Directory -Force -Path $OutDir -ErrorAction SilentlyContinue | Out-Null
Write-Host "[l1-verify] out = $OutDir"

$globalReport = @{
    started_at = (Get-Date).ToString("o")
    bridge_url = $BridgeUrl
    bridge_version = $null
    isWorld3D = $false
    world_seed = "default"
    world_size = 316
    captures = @()
    summary = @{ pass = 0; fail = 0; inconclusive = 0; all_pass = $false }
    errors = @()
    launch_error = $null
}

# 10-min wall-clock ceiling
$globalDeadline = (Get-Date).AddSeconds($StepTimeoutSec)

try {
    $health = Ensure-BridgeUp
    if (-not $health) {
        $globalReport.launch_error = "bridge never came up at $BridgeUrl"
        $globalReport.errors += $globalReport.launch_error
    } else {
        $globalReport.bridge_version = $health.version
        $globalReport.isWorld3D = [bool]$health.isWorld3D
        $ws = Initialize-World -Health $health
        $globalReport.world_seed = $ws.seed
        $globalReport.world_size = $ws.size
        $globalReport.isWorld3D = [bool]$ws.isWorld3D

        # P0-4 is special: derived from bridge/render_stats, not a screenshot
        $rs = Invoke-BridgeJson -Method GET -Path "/diag/render_stats" -Timeout 10
        $p04 = @{
            p0 = "P0-4"
            title = "Mod loaded, bridge + render_stats reachable"
            files = @()
            checks = @{
                bridge_alive = [bool]$health.ok
                bridge_version = $health.version
                render_stats_ok = [bool]($rs.ok)
            }
            verdict = "inconclusive"
            errors = @()
        }
        if ($rs.ok -and $rs.data) {
            $p04.checks.render_stats = $rs.data
        }
        if ($p04.checks.bridge_alive -and $p04.checks.render_stats_ok) {
            $p04.verdict = "pass"
        } else {
            $p04.verdict = "fail"
            $p04.errors += "bridge alive=$($p04.checks.bridge_alive) render_stats_ok=$($p04.checks.render_stats_ok)"
        }
        $globalReport.captures += $p04

        # Run all other P0s in order
        foreach ($key in @("P0-1","P0-2","P0-3","P0-5","P0-5b","P0-6","P0-7","P0-8")) {
            if ((Get-Date) -ge $globalDeadline) {
                $globalReport.errors += "global deadline reached before $key"
                break
            }
            $spec = $P0Specs[$key]
            $entry = Capture-P0 -P0Id $key -Spec $spec -OutRoot $OutDir
            $globalReport.captures += $entry
        }
    }
} catch {
    $globalReport.errors += "fatal: $($_.Exception.Message)"
} finally {
    # Tally
    foreach ($c in $globalReport.captures) {
        switch ($c.verdict) {
            "pass"         { $globalReport.summary.pass++ }
            "fail"         { $globalReport.summary.fail++ }
            "inconclusive" { $globalReport.summary.inconclusive++ }
            default        { $globalReport.summary.inconclusive++ }
        }
    }
    $globalReport.summary.all_pass = ($globalReport.summary.fail -eq 0 -and $globalReport.summary.inconclusive -eq 0 -and $globalReport.summary.pass -gt 0)
    $globalReport.finished_at = (Get-Date).ToString("o")

    $reportPath = Join-Path $OutDir "report.json"
    $globalReport | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host "[l1-verify] report: $reportPath"
    Write-Host "[l1-verify] summary: pass=$($globalReport.summary.pass) fail=$($globalReport.summary.fail) inconclusive=$($globalReport.summary.inconclusive)"
}

exit 0
