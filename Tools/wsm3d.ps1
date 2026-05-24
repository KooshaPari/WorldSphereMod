#Requires -Version 5.0
<#
.SYNOPSIS
  WorldSphereMod3D CLI — unified entry point for build, install, launch, and diagnostics.

.DESCRIPTION
  One-file command dispatcher for WSM3D development. Routes to subcommands:
    build, install, launch, kill, relaunch, log, profile, render-budget, screenshot, settings, toggle, status, doctor, submodule, journey, watch, help.

.EXAMPLE
  ./wsm3d.ps1 build
  ./wsm3d.ps1 install -Launch
  ./wsm3d.ps1 log -Follow
  ./wsm3d.ps1 profile -DryRun
  ./wsm3d.ps1 render-budget -DryRun
  ./wsm3d.ps1 settings get -Key VoxelEntities
  ./wsm3d.ps1 settings set -Key VoxelEntities -Value true
  ./wsm3d.ps1 toggle -Phase VoxelEntities
  ./wsm3d.ps1 relaunch
  ./wsm3d.ps1 status -Json
  ./wsm3d.ps1 journey verify -Id smoke-test-phase1
#>

$ErrorActionPreference = "Stop"

# === Setup ===
$script:ToolsDir = $PSScriptRoot
$script:RepoRoot = Split-Path -Parent $ToolsDir
$script:WorldBoxPath = if ($env:WORLDBOX_PATH) { $env:WORLDBOX_PATH } else { "C:/Program Files (x86)/Steam/steamapps/common/Worldbox" }
$script:ModInstallPath = Join-Path $WorldBoxPath "Mods/WorldSphereMod3D"
$script:PlayerLogPath = Join-Path $env:USERPROFILE "AppData/LocalLow/mkarpenko/WorldBox/Player.log"
# NML's Paths.ModsConfigPath resolves to a snake_case folder on Windows (mods_config),
# not the PascalCase "ModsConfig" you might guess. Verified by Get-ChildItem -Recurse
# -Filter WorldSphereMod.json under $env:USERPROFILE/AppData.
$script:ModSettingsDir = Join-Path $env:USERPROFILE "AppData/LocalLow/mkarpenko/WorldBox/mods_config"
$script:ModSettingsFile = Join-Path $ModSettingsDir "WorldSphereMod.json"
$script:BuiltDll = Join-Path $RepoRoot "bin/Release/net48/WorldSphereMod3D.dll"
$script:PhenotypeJourneyRepo = "C:/Users/koosh/Dino/tools/phenotype-journeys"
$script:PhenotypeJourneyCache = Join-Path $RepoRoot "tools/.cache/phenotype-journeys"
$script:BridgePort = 8766
$script:BridgeHealthUrl = "http://127.0.0.1:8766/health"
$script:OmniRouteBaseUrl = if ($env:OMNROUTE_BASE_URL) { $env:OMNROUTE_BASE_URL.TrimEnd('/') } else { "http://127.0.0.1:20128/v1" }
$script:GitSubmodulePaths = @("External/Compound-Spheres")
$script:LiveVerifyReportPath = Join-Path $RepoRoot "Tools/.reports/live-verify-latest.json"

# Phase defaults from SavedSettings.cs (also used by safe-min preset)
$script:PhaseDefaults = @{
    "VoxelEntities"       = $false
    "ProceduralBuildings" = $false
    "CrossedQuadFoliage"  = $true
    "MeshWater"           = $false
    "HighShadows"         = $false
    "SkeletalAnimation"   = $false
    "WorldspaceUI"        = $true
    "DayNightCycle"       = $false
    "PostFX"              = $false
    "ParticleEffects"     = $true
}

# Phase slug to camelCase mapping
$script:PhaseMap = @{
    "voxel_entities"      = "VoxelEntities"
    "procedural_buildings" = "ProceduralBuildings"
    "crossed_quad_foliage" = "CrossedQuadFoliage"
    "mesh_water"          = "MeshWater"
    "high_shadows"        = "HighShadows"
    "skeletal_animation"  = "SkeletalAnimation"
    "worldspace_ui"       = "WorldspaceUI"
    "day_night_cycle"     = "DayNightCycle"
    "post_fx"             = "PostFX"
    "particle_effects"    = "ParticleEffects"
}

# === Helpers ===
function Write-Error-Custom {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Normalize-PhaseName {
    param([string]$Phase)

    # If already camelCase, return as-is
    if ($PhaseMap.Values -contains $Phase) {
        return $Phase
    }

    # Try to map from snake_case
    $lower = $Phase.ToLower()
    if ($PhaseMap.ContainsKey($lower)) {
        return $PhaseMap[$lower]
    }

    # If it starts with uppercase, assume camelCase
    if ([char]::IsUpper($Phase[0])) {
        return $Phase
    }

    throw "Phase '$Phase' not recognized. Use camelCase (e.g., VoxelEntities) or snake_case (e.g., voxel_entities)."
}

function Get-SettingsJson {
    if (-not (Test-Path $ModSettingsFile)) {
        throw "Settings file not found at $ModSettingsFile. Run 'wsm3d install' first."
    }
    Get-Content $ModSettingsFile -Raw | ConvertFrom-Json
}

function Set-SettingsJson {
    param([object]$SettingsObj)
    if (-not (Test-Path $ModSettingsDir)) {
        New-Item -ItemType Directory -Force -Path $ModSettingsDir | Out-Null
    }
    $SettingsObj | ConvertTo-Json -Depth 10 | Set-Content -Path $ModSettingsFile -Encoding UTF8
}

function Get-LatestPlayerLog {
    $logDir = Split-Path -Parent $PlayerLogPath
    $latestLog = Get-ChildItem -Path $logDir -Filter "Player.log*" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $latestLog) {
        throw "Player.log not found under $logDir"
    }

    return $latestLog
}

function Get-PhenotypeJourneyCandidatePaths {
    return @(
        (Join-Path $script:PhenotypeJourneyRepo "target/release/phenotype-journey.exe"),
        (Join-Path $script:PhenotypeJourneyRepo "target/release/phenotype-journey"),
        (Join-Path $script:PhenotypeJourneyCache "target/release/phenotype-journey.exe"),
        (Join-Path $script:PhenotypeJourneyCache "target/release/phenotype-journey")
    )
}

function Find-PhenotypeJourneyBinary {
    $journeyCmd = Get-Command phenotype-journey -ErrorAction SilentlyContinue
    if ($journeyCmd) {
        return $journeyCmd.Source
    }

    foreach ($candidate in (Get-PhenotypeJourneyCandidatePaths)) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Get-PhenotypeJourneyBinary {
    $found = Find-PhenotypeJourneyBinary
    if ($found) {
        return $found
    }

    $buildRoots = @()
    if (Test-Path $script:PhenotypeJourneyRepo) {
        $buildRoots += $script:PhenotypeJourneyRepo
    }
    if (Test-Path $script:PhenotypeJourneyCache) {
        $buildRoots += $script:PhenotypeJourneyCache
    }

    foreach ($root in $buildRoots) {
        Write-Info "Building phenotype-journey from $root..."
        Push-Location $root
        try {
            & cargo build --release --bin phenotype-journey
            if ($LASTEXITCODE -ne 0) {
                throw "cargo build failed in $root"
            }
        } finally {
            Pop-Location
        }

        $found = Find-PhenotypeJourneyBinary
        if ($found) {
            return $found
        }
    }

    throw "phenotype-journey not found on PATH and no local source or cache build could produce a binary. See docs/journeys/README.md."
}

function Test-BridgeHealthy {
    param([string]$Url = $script:BridgeHealthUrl)

    try {
        $response = Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec 8
        return [bool]($response -and $response.ok -ne $false)
    } catch {
        return $false
    }
}

function Test-OmniRouteReachable {
    param([string]$BaseUrl = $script:OmniRouteBaseUrl)

    $modelsUrl = if ($BaseUrl -match "/v1/?$") {
        ($BaseUrl.TrimEnd("/")) + "/models"
    } else {
        $BaseUrl.TrimEnd("/") + "/v1/models"
    }

    try {
        $headers = @{}
        if ($env:OMNROUTE_API_KEY) {
            $headers["Authorization"] = "Bearer $($env:OMNROUTE_API_KEY)"
        }
        $null = Invoke-RestMethod -Uri $modelsUrl -Method Get -Headers $headers -TimeoutSec 8
        return $true
    } catch {
        return $false
    }
}

function Get-GitSubmoduleDoctorRows {
    $rows = @()

    Push-Location $RepoRoot
    try {
        $gitCmd = Get-Command git -ErrorAction SilentlyContinue
        if (-not $gitCmd) {
            return @([PSCustomObject]@{
                path = "(git)"
                status = "fail"
                detail = "git not found on PATH"
            })
        }

        $statusLines = @(& git submodule status --recursive 2>&1)
        if ($LASTEXITCODE -ne 0) {
            return @([PSCustomObject]@{
                path = "(submodules)"
                status = "fail"
                detail = ($statusLines -join "; ")
            })
        }

        if (-not $statusLines -or $statusLines.Count -eq 0) {
            foreach ($path in $script:GitSubmodulePaths) {
                $fullPath = Join-Path $RepoRoot $path
                if (-not (Test-Path -LiteralPath $fullPath)) {
                    $rows += [PSCustomObject]@{
                        path = $path
                        status = "fail"
                        detail = "expected submodule path missing"
                    }
                }
            }
            if ($rows.Count -eq 0) {
                $rows += [PSCustomObject]@{
                    path = "(none)"
                    status = "ok"
                    detail = "no submodules configured"
                }
            }
            return $rows
        }

        foreach ($line in $statusLines) {
            $text = [string]$line
            if ([string]::IsNullOrWhiteSpace($text)) { continue }

            $parsed = [regex]::Match($text, '^(?<flag>[ \-+U])(?<sha>[0-9a-f]+)\s+(?<path>\S+)')
            if (-not $parsed.Success) {
                $rows += [PSCustomObject]@{
                    path = $text.Trim()
                    status = "warn"
                    detail = "unparsed submodule status line"
                }
                continue
            }

            $path = $parsed.Groups["path"].Value
            $flag = $parsed.Groups["flag"].Value
            $state = switch ($flag) {
                " " { "ok" }
                "-" { "fail" }
                "+" { "warn" }
                "U" { "fail" }
                default { "warn" }
            }
            $detail = switch ($flag) {
                " " { "initialized at pinned commit" }
                "-" { "not initialized — run: wsm3d submodule init" }
                "+" { "checkout differs from index — run: git submodule update" }
                "U" { "merge conflict in submodule" }
                default { $text }
            }

            $rows += [PSCustomObject]@{
                path = $path
                status = $state
                detail = $detail
            }
        }
    } finally {
        Pop-Location
    }

    return $rows
}

function New-DoctorCheck {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Id,

        [Parameter(Mandatory=$true)]
        [ValidateSet("ok", "warn", "fail", "skip")]
        [string]$Status,

        [bool]$Required = $true,
        [bool]$Optional = $false,
        [string]$Message = "",
        [hashtable]$Details = @{}
    )

    return [ordered]@{
        id = $Id
        status = $Status
        required = $Required
        optional = $Optional
        message = $Message
        details = $Details
    }
}

function Get-PhenotypeJourneyManifestPath {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Id
    )

    $manifestPath = Join-Path $RepoRoot "docs/journeys/manifests/$Id/manifest.json"
    if (-not (Test-Path $manifestPath)) {
        throw "Manifest not found for journey '$Id' at $manifestPath"
    }

    return $manifestPath
}

function Resolve-JourneyVerifyTarget {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Value
    )

    if (Test-Path $Value) {
        return (Resolve-Path $Value).Path
    }

    return Get-PhenotypeJourneyManifestPath -Id $Value
}

function Get-PhenotypeJourneyIndex {
    $indexPath = Join-Path $RepoRoot "docs/journeys/manifests/index.json"
    if (Test-Path $indexPath) {
        return Get-Content $indexPath -Raw | ConvertFrom-Json
    }

    $manifestRoot = Join-Path $RepoRoot "docs/journeys/manifests"
    $entries = @()
    foreach ($manifest in Get-ChildItem -Path $manifestRoot -Recurse -Filter "manifest.json" -File -ErrorAction SilentlyContinue | Sort-Object FullName) {
        $entries += [PSCustomObject]@{
            id = Split-Path -Path (Split-Path -Parent $manifest.FullName) -Leaf
            intent = ""
            file = [System.IO.Path]::GetRelativePath($manifestRoot, $manifest.FullName)
        }
    }

    return $entries
}

function Convert-DurationToMilliseconds {
    param(
        [double]$Value,
        [string]$Unit
    )

    $normalized = if ($Unit) { $Unit.ToLowerInvariant() } else { "ms" }
    switch ($normalized) {
        "s" { return $Value * 1000.0 }
        "ms" { return $Value }
        "us" { return $Value / 1000.0 }
        "ns" { return $Value / 1000000.0 }
        default { return $Value }
    }
}

function Get-InitProfilerRows {
    param([string]$LogPath)

    $lines = Select-String -Path $LogPath -Pattern "\[WSM3D\].*InitProfiler" -ErrorAction SilentlyContinue
    $rows = @()

    foreach ($match in $lines) {
        $line = $match.Line
        $nameDurationMatch = [regex]::Match(
            $line,
            "name\s*=\s*(?<name>[^,;\]]+).*duration_(?<unit>s|ms)\s*=\s*(?<duration>[+-]?(?:\d+(?:\.\d+)?|\.\d+))"
        )
        if ($nameDurationMatch.Success) {
            $rows += [PSCustomObject]@{
                Operation = $nameDurationMatch.Groups["name"].Value.Trim()
                DurationMs = Convert-DurationToMilliseconds `
                    -Value ([double]$nameDurationMatch.Groups["duration"].Value) `
                    -Unit $nameDurationMatch.Groups["unit"].Value
                LineNumber = $match.LineNumber
            }
            continue
        }

        $labelDurationMatch = [regex]::Match(
            $line,
            "InitProfiler\s+(?<name>.+?)\s*=\s*(?<duration>[+-]?(?:\d+(?:\.\d+)?|\.\d+))\s*(?<unit>s|ms|us|ns)?"
        )
        if ($labelDurationMatch.Success) {
            $unit = $labelDurationMatch.Groups["unit"].Value
            if (-not $unit) { $unit = "ms" }
            $rows += [PSCustomObject]@{
                Operation = $labelDurationMatch.Groups["name"].Value.Trim()
                DurationMs = Convert-DurationToMilliseconds `
                    -Value ([double]$labelDurationMatch.Groups["duration"].Value) `
                    -Unit $unit
                LineNumber = $match.LineNumber
            }
        }
    }

    return @($rows)
}

function Get-FrameDrawCallRows {
    param([string]$LogPath)

    $lines = Select-String -Path $LogPath -Pattern "RuntimeStatsOverlay|FrameDrawCalls|DrawCalls" -ErrorAction SilentlyContinue
    $rows = @()

    foreach ($match in $lines) {
        $line = $match.Line
        $drawMatch = [regex]::Match($line, "(?:FrameDrawCalls|DrawCalls)\s*=\s*(?<draws>\d+)")
        if (-not $drawMatch.Success) {
            continue
        }

        $frameMsMatch = [regex]::Match($line, "FrameMs\s*=\s*(?<frameMs>[+-]?(?:\d+(?:\.\d+)?|\.\d+))")
        $instancesMatch = [regex]::Match($line, "Instances\s*=\s*(?<instances>\d+)")
        $rows += [PSCustomObject]@{
            LineNumber = $match.LineNumber
            FrameDrawCalls = [long]$drawMatch.Groups["draws"].Value
            FrameMs = if ($frameMsMatch.Success) { [double]$frameMsMatch.Groups["frameMs"].Value } else { $null }
            Instances = if ($instancesMatch.Success) { [long]$instancesMatch.Groups["instances"].Value } else { $null }
        }
    }

    return @($rows)
}

function Ensure-ScreenshotInterop {
    if ("WSM3D.ScreenCaptureNative" -as [type]) {
        return
    }

    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

namespace WSM3D {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public static class ScreenCaptureNative {
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT {
        public int X;
        public int Y;
    }
}
"@
}

function Get-CaptureBounds {
    param(
        [switch]$WindowOnly
    )

    Ensure-ScreenshotInterop

    if ($WindowOnly) {
        $proc = Get-Process worldbox -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $proc) {
            throw "WorldBox not running. Cannot capture window-only screenshot."
        }

        $handle = [IntPtr]$proc.MainWindowHandle
        if ($handle -eq [IntPtr]::Zero) {
            throw "WorldBox window handle not available yet."
        }

        if ([WSM3D.ScreenCaptureNative]::IsIconic($handle)) {
            throw "WorldBox is minimized. Restore the window before capturing."
        }

        $clientRect = New-Object WSM3D.RECT
        if (-not [WSM3D.ScreenCaptureNative]::GetClientRect($handle, [ref]$clientRect)) {
            throw "Unable to determine the WorldBox client bounds."
        }

        $origin = New-Object WSM3D.POINT
        if (-not [WSM3D.ScreenCaptureNative]::ClientToScreen($handle, [ref]$origin)) {
            throw "Unable to translate the WorldBox client origin."
        }

        if ($clientRect.Width -le 0 -or $clientRect.Height -le 0) {
            throw "WorldBox client bounds were empty."
        }

        return [PSCustomObject]@{
            X = $origin.X
            Y = $origin.Y
            Width = $clientRect.Width
            Height = $clientRect.Height
        }
    }

    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    return [PSCustomObject]@{
        X = $screen.X
        Y = $screen.Y
        Width = $screen.Width
        Height = $screen.Height
    }
}

function Parse-CaptureRegion {
    param([string]$Region)

    if (-not $Region) {
        return $null
    }

    $parts = @($Region -split '[,\s]+' | Where-Object { $_ -ne '' })
    if ($parts.Count -ne 4) {
        throw "Region must be four integers: x,y,width,height"
    }

    return [PSCustomObject]@{
        X = [int]$parts[0]
        Y = [int]$parts[1]
        Width = [int]$parts[2]
        Height = [int]$parts[3]
    }
}

# Slugs documented in docs/smoke-test-phase*.md (before/after/buildings).
$script:PhaseScreenshotSuggestedNames = @{
    1 = @("before", "after", "buildings")
    2 = @("before", "after", "buildings")
    3 = @("before", "after", "foliage")
    4 = @("before", "after", "water")
    5 = @("before", "after", "shadows-sky")
    6 = @("before", "after", "actors-rig")
    7 = @("before", "after", "nameplates")
    8 = @("before", "after", "sky-cycle")
    9 = @("before", "after", "effects")
    10 = @("before", "after", "lod-ladder")
}

function Get-PhaseScreenshotSuggestedNames {
    param([int]$Phase)

    if ($script:PhaseScreenshotSuggestedNames.ContainsKey($Phase)) {
        return @($script:PhaseScreenshotSuggestedNames[$Phase])
    }

    return @("before", "after")
}

function Get-PhaseScreenshotPath {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Phase,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($Phase -lt 1 -or $Phase -gt 10) {
        throw "Phase must be between 1 and 10."
    }

    if ($Name -notmatch '^[a-z0-9][a-z0-9._-]*$') {
        throw "Screenshot name must be a simple slug (e.g. before, after, buildings)."
    }

    $screenshotDir = Join-Path $RepoRoot "docs/screenshots"
    if (-not (Test-Path $screenshotDir)) {
        New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
    }

    return Join-Path $screenshotDir "phase-$Phase-$Name.png"
}

function Invoke-ScreenshotPhase {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Phase,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [switch]$WindowOnly
    )

    $path = Get-PhaseScreenshotPath -Phase $Phase -Name $Name
    Write-Info "Capturing phase $Phase smoke-test frame '$Name' -> $path"
    Invoke-Screenshot -Path $path -WindowOnly:$WindowOnly
}

# === Commands ===

function Invoke-Build {
    param(
        [ValidateSet("Release", "Debug")]
        [string]$Configuration = "Release"
    )

    Write-Info "Building WorldSphereMod.csproj -c $Configuration..."
    $env:WORLDBOX_PATH = $WorldBoxPath

    Push-Location $RepoRoot
    try {
        & dotnet build WorldSphereMod.csproj -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed."
        }
        Write-Success "Build completed."
    } finally {
        Pop-Location
    }
}

function Invoke-Install {
    param(
        [switch]$Launch,
        [switch]$NoBuild
    )

    Write-Info "Installing WorldSphereMod3D..."
    # $args is a PowerShell auto-variable; using it as a local clashed and
    # caused the splatted args to bind into install.ps1's first positional
    # ($WorldBoxPath). Use a distinct name + explicit splat.
    $installArgs = @()
    if ($NoBuild) { $installArgs += "-SkipBuild" }

    & (Join-Path $ToolsDir "install.ps1") @installArgs
    Write-Success "Installation complete."

    if ($Launch) {
        Invoke-Launch
    }
}

function Invoke-Launch {
    Write-Info "Launching WorldBox..."
    Start-Process "steam://rungameid/1206560"
    Write-Success "WorldBox launched."
}

function Invoke-Kill {
    $proc = Get-Process worldbox -ErrorAction SilentlyContinue
    if (-not $proc) {
        Write-Warn "WorldBox not running."
        return
    }

    Write-Warn "Killing WorldBox process..."
    Stop-Process -InputObject $proc -Force
    Write-Success "WorldBox killed."
}

function Invoke-Relaunch {
    param([switch]$NoBuild)

    Write-Info "Relaunching: kill + install + launch..."
    Invoke-Kill
    Start-Sleep -Seconds 2
    Invoke-Install -NoBuild:$NoBuild -Launch
}

function Invoke-Log {
    param(
        [int]$Tail = 50,
        [switch]$Follow,
        [string]$Grep
    )

    if (-not (Test-Path $PlayerLogPath)) {
        throw "Player.log not found at $PlayerLogPath"
    }

    $logContent = Get-Content $PlayerLogPath -Tail $Tail

    if ($Grep) {
        $logContent = $logContent | Select-String -Pattern $Grep
    }

    $logContent | ForEach-Object { Write-Host $_ }

    if ($Follow) {
        Write-Info "Following log (Ctrl+C to stop)..."
        Get-Content $PlayerLogPath -Wait -Tail 50 | ForEach-Object {
            if ($Grep) {
                if ($_ -match $Grep) { Write-Host $_ }
            } else {
                Write-Host $_
            }
        }
    }
}

function Invoke-Profile {
    param(
        [switch]$DryRun,
        [switch]$Json
    )

    if (-not $DryRun) {
        $wasRunning = Get-Process worldbox -ErrorAction SilentlyContinue
        if (-not $wasRunning) {
            Invoke-Launch
            Write-Info "Waiting 90s for init completion..."
            Start-Sleep -Seconds 90
            Invoke-Kill
        } else {
            Write-Info "WorldBox already running. Waiting 90s before parsing logs..."
            Start-Sleep -Seconds 90
        }
    }

    $logDir = Split-Path -Parent $PlayerLogPath
    $latestLog = Get-ChildItem -Path $logDir -Filter "Player.log*" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $latestLog) {
        throw "Player.log not found under $logDir"
    }

    $logPath = $latestLog.FullName
    $initLines = Select-String -Path $logPath -Pattern "\[WSM3D\].*InitProfiler" | Select-Object -ExpandProperty Line

    $bucketMap = @{}
    $entryRows = @()
    $totalDuration = 0.0

    foreach ($line in $initLines) {
        $nameMatch = [regex]::Match($line, "name\s*=\s*(?<name>[^\s,;\]]+)")
        $durMatch = [regex]::Match($line, "duration_s\s*=\s*(?<duration>[+-]?(?:\d+(?:\.\d+)?|\.\d+))")
        if (-not $nameMatch.Success -or -not $durMatch.Success) {
            continue
        }

        $name = $nameMatch.Groups["name"].Value
        $duration = [double]$durMatch.Groups["duration"].Value
        $entryRows += [PSCustomObject]@{
            name = $name
            duration_s = $duration
        }

        if (-not $bucketMap.ContainsKey($name)) {
            $bucketMap[$name] = @{
                count = 0
                sum_s = 0.0
            }
        }
        $bucket = $bucketMap[$name]
        $bucket.count += 1
        $bucket.sum_s += $duration
        $bucketMap[$name] = $bucket
        $totalDuration += $duration
    }

    $bucketRows = $bucketMap.GetEnumerator() | ForEach-Object {
        [PSCustomObject]@{
            Name = $_.Name
            Count = $_.Value.count
            Sum_s = $_.Value.sum_s
            Avg_s = if ($_.Value.count -gt 0) { $_.Value.sum_s / $_.Value.count } else { 0.0 }
        }
    } | Sort-Object -Property Sum_s -Descending

    if ($Json) {
        $jsonOut = [ordered]@{
            total_s = $totalDuration
            buckets = $bucketRows | ForEach-Object {
                [ordered]@{
                    name = $_.Name
                    count = $_.Count
                    sum_s = $_.Sum_s
                    avg_s = $_.Avg_s
                }
            }
            entries = $entryRows
        }
        Write-Host ($jsonOut | ConvertTo-Json -Depth 10)
        return
    }

    if ($bucketRows.Count -eq 0) {
        Write-Warn "No [WSM3D] InitProfiler lines found in $logPath"
        return
    }

    Write-Host ""
    Write-Host "InitProfiler (bucketed, slowest first)" -ForegroundColor Cyan
    $bucketRows | ForEach-Object {
        $_.Sum_s = [Math]::Round($_.Sum_s, 6)
        $_.Avg_s = [Math]::Round($_.Avg_s, 6)
    }
    $bucketRows | Format-Table @{Label="Name"; Expression="Name"; Width=35},
        @{Label="Count"; Expression="Count"; Width=8; Alignment="Right"},
        @{Label="Sum_s"; Expression="Sum_s"; Width=14; Alignment="Right"; FormatString="F6"},
        @{Label="Avg_s"; Expression="Avg_s"; Width=14; Alignment="Right"; FormatString="F6"}

    Write-Host ""
    Write-Host ("Overall total: " + [Math]::Round($totalDuration, 6) + " s")
}

function Invoke-RenderBudget {
    param(
        [switch]$DryRun,
        [switch]$Json
    )

    if (-not $DryRun) {
        $wasRunning = Get-Process worldbox -ErrorAction SilentlyContinue
        if (-not $wasRunning) {
            Invoke-Launch
            Write-Info "Waiting 90s for render-budget samples..."
            Start-Sleep -Seconds 90
            Invoke-Kill
        } else {
            Write-Info "WorldBox already running. Waiting 90s before parsing logs..."
            Start-Sleep -Seconds 90
        }
    }

    $latestLog = Get-LatestPlayerLog
    $logPath = $latestLog.FullName
    $initRows = @(Get-InitProfilerRows -LogPath $logPath | Sort-Object -Property DurationMs -Descending)
    $totalMs = ($initRows | Measure-Object -Property DurationMs -Sum).Sum
    if ($null -eq $totalMs) { $totalMs = 0.0 }

    $cumulative = 0.0
    $operationRows = @()
    foreach ($row in $initRows) {
        $pct = if ($totalMs -gt 0) { ($row.DurationMs / $totalMs) * 100.0 } else { 0.0 }
        $cumulative += $pct
        $operationRows += [PSCustomObject]@{
            Operation = $row.Operation
            TimeMs = $row.DurationMs
            InitPct = $pct
            CumulativePct = $cumulative
        }
    }

    $drawRows = @(Get-FrameDrawCallRows -LogPath $logPath)
    $drawSummary = $null
    if ($drawRows.Count -gt 0) {
        $drawStats = $drawRows | Measure-Object -Property FrameDrawCalls -Minimum -Maximum -Average
        $drawSummary = [ordered]@{
            samples = $drawRows.Count
            latest = $drawRows[-1].FrameDrawCalls
            min = [long]$drawStats.Minimum
            max = [long]$drawStats.Maximum
            avg = [Math]::Round($drawStats.Average, 2)
        }
    }

    if ($Json) {
        $jsonOut = [ordered]@{
            log_path = $logPath
            init_total_ms = $totalMs
            operations = @($operationRows | ForEach-Object {
                [ordered]@{
                    operation = $_.Operation
                    time_ms = $_.TimeMs
                    init_pct = $_.InitPct
                    cumulative_pct = $_.CumulativePct
                }
            })
            frame_draw_calls = [ordered]@{
                summary = $drawSummary
                history = @($drawRows | ForEach-Object {
                    [ordered]@{
                        line_number = $_.LineNumber
                        frame_draw_calls = $_.FrameDrawCalls
                        frame_ms = $_.FrameMs
                        instances = $_.Instances
                    }
                })
            }
        }
        Write-Host ($jsonOut | ConvertTo-Json -Depth 10)
        return
    }

    if ($operationRows.Count -eq 0) {
        Write-Warn "No [WSM3D] InitProfiler lines found in $logPath"
    } else {
        Write-Host ""
        Write-Host "Render budget (slowest first)" -ForegroundColor Cyan
        $displayRows = $operationRows | ForEach-Object {
            [PSCustomObject]@{
                Operation = $_.Operation
                TimeMs = [Math]::Round($_.TimeMs, 3)
                InitPct = [Math]::Round($_.InitPct, 2)
                CumulativePct = [Math]::Round($_.CumulativePct, 2)
            }
        }
        $displayRows | Format-Table @{Label="Operation"; Expression="Operation"; Width=42},
            @{Label="TimeMs"; Expression="TimeMs"; Width=12; Alignment="Right"; FormatString="F3"},
            @{Label="InitPct"; Expression="InitPct"; Width=10; Alignment="Right"; FormatString="F2"},
            @{Label="CumPct"; Expression="CumulativePct"; Width=10; Alignment="Right"; FormatString="F2"}

        Write-Host ("Init total: " + [Math]::Round($totalMs, 3) + " ms")
    }

    Write-Host ""
    if ($drawSummary) {
        Write-Host "FrameDrawCalls history" -ForegroundColor Cyan
        [PSCustomObject]$drawSummary | Format-Table @{Label="Samples"; Expression="samples"; Width=10; Alignment="Right"},
            @{Label="Latest"; Expression="latest"; Width=10; Alignment="Right"},
            @{Label="Min"; Expression="min"; Width=10; Alignment="Right"},
            @{Label="Max"; Expression="max"; Width=10; Alignment="Right"},
            @{Label="Avg"; Expression="avg"; Width=10; Alignment="Right"; FormatString="F2"}
    } else {
        Write-Warn "No FrameDrawCalls/DrawCalls samples found in RuntimeStatsOverlay logs."
    }
}

function Invoke-Screenshot {
    param(
        [string]$Path,
        [switch]$WindowOnly,
        [string]$Region
    )

    Write-Info "Capturing screenshot..."
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms

    $bounds = Get-CaptureBounds -WindowOnly:$WindowOnly
    $regionBounds = Parse-CaptureRegion -Region $Region
    if ($regionBounds) {
        $bounds = $regionBounds
    }

    $bmp = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $graphics.CopyFromScreen(
        (New-Object System.Drawing.Point($bounds.X, $bounds.Y)),
        [System.Drawing.Point]::Empty,
        (New-Object System.Drawing.Size($bounds.Width, $bounds.Height))
    )
    $graphics.Dispose()

    if (-not $Path) {
        $Path = Join-Path $RepoRoot "screenshots/screenshot_$(Get-Date -Format 'yyyyMMdd_HHmmss').png"
        $screenshotDir = Split-Path -Parent $Path
        if (-not (Test-Path $screenshotDir)) {
            New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
        }
    }

    $bmp.Save($Path)
    $bmp.Dispose()
    Write-Success "Screenshot saved to $Path"
}

function Invoke-SettingsGet {
    param([string]$Key)

    $settings = Get-SettingsJson

    if ($Key) {
        if (-not ($settings | Get-Member -Name $Key)) {
            throw "Key '$Key' not found in settings."
        }
        $value = $settings.$Key
        Write-Host ($value | ConvertTo-Json -Depth 10)
    } else {
        Write-Host ($settings | ConvertTo-Json -Depth 10)
    }
}

function Invoke-SettingsSet {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Key,

        [Parameter(Mandatory=$true)]
        $Value,

        [switch]$Force
    )

    $settings = Get-SettingsJson

    if (-not ($settings | Get-Member -Name $Key)) {
        throw "Key '$Key' not found in settings."
    }

    Assert-SettingsWritable -Force:$Force

    # Coerce value to match the original type
    $orig = $settings.$Key
    $settings.$Key = if ($orig -is [bool]) {
        [bool]::Parse($Value)
    } elseif ($orig -is [int]) {
        [int]::Parse($Value)
    } elseif ($orig -is [float]) {
        [float]::Parse($Value)
    } else {
        $Value
    }

    Set-SettingsJson $settings
    Write-Success "Set $Key = $($settings.$Key)"
}

function Invoke-Toggle {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Phase
    )

    $phaseName = Normalize-PhaseName $Phase
    $settings = Get-SettingsJson

    if (-not ($settings | Get-Member -Name $phaseName)) {
        throw "Phase '$phaseName' not found in settings."
    }

    $settings.$phaseName = -not $settings.$phaseName
    Set-SettingsJson $settings
    Write-Success "Toggled $phaseName = $($settings.$phaseName)"
}

function Assert-SettingsWritable {
    param([switch]$Force)

    if (Get-Process worldbox -ErrorAction SilentlyContinue) {
        if (-not $Force) {
            Write-Warn "WorldBox is running. Refusing to write settings because the game may overwrite this change on the next save."
            throw "Re-run with -Force if you intentionally want to patch settings while WorldBox is running."
        }
    }
}

function Invoke-PhasesList {
    param([switch]$Json)

    $settings = Get-SettingsJson
    $defaults = $script:PhaseDefaults

    if ($Json) {
        $phases = @()
        foreach ($phaseName in $defaults.Keys) {
            $phases += @{
                "name"    = $phaseName
                "current" = [bool]$settings.$phaseName
                "default" = $defaults[$phaseName]
            }
        }
        $output = @{
            "phases" = $phases
        }
        Write-Host ($output | ConvertTo-Json)
    } else {
        Write-Host ""
        Write-Host "Phase Status" -ForegroundColor Cyan
        Write-Host ("Phase" + (" " * 20) + "Current   Default") -ForegroundColor Cyan
        Write-Host ("-" * 53) -ForegroundColor Cyan
        foreach ($phaseName in $defaults.Keys) {
            $current = $settings.$phaseName
            $default = $defaults[$phaseName]
            $pad = 25 - $phaseName.Length
            Write-Host "$phaseName$(' ' * $pad)$current$(' ' * 5)$default"
        }
        Write-Host ""
    }
}

function Invoke-PhasesEnableAll {
    param([switch]$Force)

    Assert-SettingsWritable -Force:$Force
    $settings = Get-SettingsJson

    foreach ($phaseName in $script:PhaseDefaults.Keys) {
        if ($settings | Get-Member -Name $phaseName) {
            $settings.$phaseName = $true
        }
    }

    Set-SettingsJson $settings
    Write-Success "Enabled all $($script:PhaseDefaults.Count) phase flags."
}

function Invoke-PhasesPreset {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Preset,

        [switch]$Force
    )

    switch ($Preset.ToLowerInvariant()) {
        "safe-min" {
            Assert-SettingsWritable -Force:$Force
            $settings = Get-SettingsJson

            foreach ($phaseName in $script:PhaseDefaults.Keys) {
                if ($settings | Get-Member -Name $phaseName) {
                    $settings.$phaseName = $script:PhaseDefaults[$phaseName]
                }
            }

            Set-SettingsJson $settings
            Write-Success "Applied preset '$Preset' (SavedSettings factory defaults)."
        }

        default {
            throw "Unknown phases preset '$Preset'. Supported: safe-min"
        }
    }
}

function Get-LiveVerifyReportSummary {
    if (-not (Test-Path -LiteralPath $script:LiveVerifyReportPath)) {
        return $null
    }

    try {
        $report = Get-Content -LiteralPath $script:LiveVerifyReportPath -Raw | ConvertFrom-Json
    } catch {
        return $null
    }

    $dotnetStage = @($report.stages | Where-Object { $_.id -eq "dotnet-tests" } | Select-Object -First 1)
    $testCounts = $null
    if ($dotnetStage.Count -gt 0 -and $dotnetStage[0].details) {
        $details = $dotnetStage[0].details
        if ($details.PSObject.Properties.Name -contains "testCounts") {
            $testCounts = $details.testCounts
        }
    }

    return [ordered]@{
        finishedAt = [string]$report.finishedAt
        overallOk  = [bool]$report.overallOk
        testCounts = $testCounts
    }
}

function Invoke-Status {
    param([switch]$Json)

    # Collect status info
    $status = @{}

    # Repo state
    $lastCommit = try {
        Push-Location $RepoRoot
        $result = & git rev-parse --short HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $result) { $result } else { "unknown" }
    } catch {
        "unknown"
    } finally {
        Pop-Location
    }
    $status["LastCommit"] = $lastCommit

    # Built DLL
    if (Test-Path $BuiltDll) {
        $dllItem = Get-Item $BuiltDll
        $status["DllModified"] = $dllItem.LastWriteTime.ToString("o")
        $status["DllSize"] = $dllItem.Length
    } else {
        $status["DllModified"] = "not built"
        $status["DllSize"] = 0
    }

    # Installed mod
    if (Test-Path $ModInstallPath) {
        $modItem = Get-Item $ModInstallPath
        $status["ModInstalled"] = $modItem.LastWriteTime.ToString("o")
    } else {
        $status["ModInstalled"] = "not installed"
    }

    # Game process
    $gameRunning = if (Get-Process worldbox -ErrorAction SilentlyContinue) { "running" } else { "stopped" }
    $status["GameProcess"] = $gameRunning

    # Log file
    if (Test-Path $PlayerLogPath) {
        $logItem = Get-Item $PlayerLogPath
        $status["LogSize"] = $logItem.Length
        $status["LogModified"] = $logItem.LastWriteTime.ToString("o")
    } else {
        $status["LogSize"] = 0
        $status["LogModified"] = "not found"
    }

    $liveVerify = Get-LiveVerifyReportSummary
    if ($liveVerify) {
        $status["LiveVerify"] = $liveVerify
    }

    if ($Json) {
        Write-Host ($status | ConvertTo-Json -Depth 6)
    } else {
        Write-Host ""
        Write-Host "Status Report" -ForegroundColor Cyan
        Write-Host "=============" -ForegroundColor Cyan
        Write-Host "  Last Commit      : $($status['LastCommit'])"
        Write-Host "  DLL (Release)    : $($status['DllModified']) ($($status['DllSize']) bytes)"
        Write-Host "  Mod Install      : $($status['ModInstalled'])"
        Write-Host "  Game Process     : $($status['GameProcess'])"
        Write-Host "  Player.log       : $($status['LogModified']) ($($status['LogSize']) bytes)"
        if ($liveVerify) {
            $lvLabel = if ($liveVerify.overallOk) { "ok" } else { "failed" }
            $lvWhen = if ($liveVerify.finishedAt) { $liveVerify.finishedAt } else { "unknown" }
            $lvLine = "  Live Verify      : $lvLabel @ $lvWhen"
            if ($liveVerify.testCounts) {
                $tc = $liveVerify.testCounts
                $lvLine += " ($($tc.total) tests: $($tc.passed) passed, $($tc.failed) failed, $($tc.skipped) skipped)"
            }
            Write-Host $lvLine
        }
        Write-Host ""
    }
}

function Invoke-Doctor {
    param([switch]$Json)

    $checks = @()

    $worldboxFromEnv = [bool]$env:WORLDBOX_PATH
    $worldboxData = Join-Path $WorldBoxPath "worldbox_Data"
    if (Test-Path -LiteralPath $worldboxData) {
        $checks += New-DoctorCheck -Id "worldbox_path" -Status "ok" -Required $true `
            -Message "WorldBox install found" `
            -Details @{
                path = $WorldBoxPath
                from_env = $worldboxFromEnv
                worldbox_data = $worldboxData
            }
    } else {
        $checks += New-DoctorCheck -Id "worldbox_path" -Status "fail" -Required $true `
            -Message "WorldBox not found (missing worldbox_Data)" `
            -Details @{
                path = $WorldBoxPath
                from_env = $worldboxFromEnv
                remediation = "Set WORLDBOX_PATH to your Steam WorldBox folder or install the game."
            }
    }

    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCmd) {
        $checks += New-DoctorCheck -Id "dotnet_sdk" -Status "fail" -Required $true `
            -Message "dotnet CLI not on PATH" `
            -Details @{ remediation = "Install .NET SDK 8+ and ensure dotnet is on PATH." }
    } else {
        $sdkVersion = $null
        $sdkError = $null
        try {
            $sdkVersion = (& dotnet --version 2>&1 | Select-Object -First 1)
            if ($LASTEXITCODE -ne 0) { throw "dotnet --version exit $LASTEXITCODE" }
        } catch {
            $sdkError = $_.Exception.Message
        }

        if ($sdkVersion -and $sdkVersion -match '^\d+\.\d+') {
            $checks += New-DoctorCheck -Id "dotnet_sdk" -Status "ok" -Required $true `
                -Message "dotnet SDK $sdkVersion" `
                -Details @{ version = $sdkVersion; path = $dotnetCmd.Source }
        } else {
            $checks += New-DoctorCheck -Id "dotnet_sdk" -Status "fail" -Required $true `
                -Message "dotnet --version failed" `
                -Details @{ error = $sdkError; path = $dotnetCmd.Source }
        }
    }

    try {
        $pythonPath = Resolve-PythonCommand
        $pythonVersion = try { (& $pythonPath --version 2>&1 | Select-Object -First 1) } catch { "(version unknown)" }
        $checks += New-DoctorCheck -Id "python" -Status "ok" -Required $true `
            -Message "Python available" `
            -Details @{ path = $pythonPath; version = [string]$pythonVersion }
    } catch {
        $checks += New-DoctorCheck -Id "python" -Status "fail" -Required $true `
            -Message $_.Exception.Message `
            -Details @{ remediation = "Install Python 3 and ensure python, python3, or py is on PATH." }
    }

    $submoduleRows = @(Get-GitSubmoduleDoctorRows)
    $subFail = @($submoduleRows | Where-Object { $_.status -eq "fail" })
    $subWarn = @($submoduleRows | Where-Object { $_.status -eq "warn" })
    if ($subFail.Count -gt 0) {
        $checks += New-DoctorCheck -Id "git_submodules" -Status "fail" -Required $true `
            -Message "$($subFail.Count) submodule(s) need attention" `
            -Details @{
                submodules = @($submoduleRows | ForEach-Object {
                    [ordered]@{ path = $_.path; status = $_.status; detail = $_.detail }
                })
                remediation = "Run: wsm3d submodule init"
            }
    } elseif ($subWarn.Count -gt 0) {
        $checks += New-DoctorCheck -Id "git_submodules" -Status "warn" -Required $true `
            -Message "$($subWarn.Count) submodule(s) out of sync with index" `
            -Details @{ submodules = @($submoduleRows | ForEach-Object {
                [ordered]@{ path = $_.path; status = $_.status; detail = $_.detail }
            }) }
    } else {
        $checks += New-DoctorCheck -Id "git_submodules" -Status "ok" -Required $true `
            -Message "git submodules initialized" `
            -Details @{ submodules = @($submoduleRows | ForEach-Object {
                [ordered]@{ path = $_.path; status = $_.status; detail = $_.detail }
            }) }
    }

    $journeyBinary = Find-PhenotypeJourneyBinary
    if ($journeyBinary) {
        $checks += New-DoctorCheck -Id "phenotype_journey" -Status "ok" -Required $false `
            -Message "phenotype-journey found" `
            -Details @{ path = $journeyBinary }
    } else {
        $checks += New-DoctorCheck -Id "phenotype_journey" -Status "warn" -Required $false `
            -Message "phenotype-journey not found (journey verify will build or fail)" `
            -Details @{
                remediation = "Add phenotype-journey to PATH or build from docs/journeys/README.md."
                search_roots = @($script:PhenotypeJourneyRepo, $script:PhenotypeJourneyCache)
            }
    }

    if (Test-BridgeHealthy) {
        $checks += New-DoctorCheck -Id "bridge_rpc" -Status "ok" -Required $false `
            -Message "BridgeRPC healthy on port $($script:BridgePort)" `
            -Details @{ url = $script:BridgeHealthUrl }
    } else {
        $checks += New-DoctorCheck -Id "bridge_rpc" -Status "warn" -Required $false `
            -Message "BridgeRPC not reachable on port $($script:BridgePort)" `
            -Details @{
                url = $script:BridgeHealthUrl
                remediation = "Launch WorldBox with the mod installed, or start wsm3d-mcp HTTP on :8766."
            }
    }

    if (Test-OmniRouteReachable) {
        $checks += New-DoctorCheck -Id "omniroute" -Status "ok" -Required $false -Optional $true `
            -Message "OmniRoute reachable" `
            -Details @{ base_url = $script:OmniRouteBaseUrl }
    } else {
        $checks += New-DoctorCheck -Id "omniroute" -Status "skip" -Required $false -Optional $true `
            -Message "OmniRoute not reachable (optional for vision)" `
            -Details @{
                base_url = $script:OmniRouteBaseUrl
                remediation = "Start OmniRoute locally or set OMNROUTE_BASE_URL; required only for -VisionBackend omniroute."
            }
    }

    $requiredFailed = @($checks | Where-Object { $_.required -and $_.status -eq "fail" })
    $requiredWarn = @($checks | Where-Object { $_.required -and $_.status -eq "warn" })
    $optionalWarn = @($checks | Where-Object { -not $_.required -and $_.status -in @("warn", "fail") })
    $overallOk = ($requiredFailed.Count -eq 0 -and $requiredWarn.Count -eq 0)

    $report = [ordered]@{
        ok = $overallOk
        generated_at = (Get-Date).ToUniversalTime().ToString("o")
        repo_root = $RepoRoot
        worldbox_path = $WorldBoxPath
        checks = $checks
        summary = [ordered]@{
            required_failed = $requiredFailed.Count
            required_warn = $requiredWarn.Count
            optional_warn = $optionalWarn.Count
        }
    }

    Write-Host ""
    Write-Host "WSM3D Doctor" -ForegroundColor Cyan
    Write-Host "============" -ForegroundColor Cyan

    foreach ($check in $checks) {
        $icon = switch ($check.status) {
            "ok" { "[OK]" }
            "warn" { "[WARN]" }
            "fail" { "[FAIL]" }
            "skip" { "[SKIP]" }
            default { "[?]" }
        }
        $color = switch ($check.status) {
            "ok" { "Green" }
            "warn" { "Yellow" }
            "fail" { "Red" }
            "skip" { "DarkGray" }
            default { "White" }
        }
        $suffix = if ($check.optional) { " (optional)" } elseif (-not $check.required) { " (recommended)" } else { "" }
        Write-Host ("  {0} {1}{2} — {3}" -f $icon, $check.id, $suffix, $check.message) -ForegroundColor $color
    }

    Write-Host ""
    if ($overallOk) {
        if ($optionalWarn.Count -gt 0) {
            Write-Warn "Required checks passed with $($optionalWarn.Count) optional warning(s)."
        } else {
            Write-Success "All required checks passed."
        }
    } else {
        Write-Error-Custom "Doctor failed: $($requiredFailed.Count) required check(s) failed, $($requiredWarn.Count) required warning(s)."
        foreach ($failed in $requiredFailed) {
            $remediation = $failed.details.remediation
            if ($remediation) {
                Write-Host "  → $($failed.id): $remediation" -ForegroundColor Yellow
            }
        }
    }

    if ($Json) {
        Write-Host ""
        Write-Host ($report | ConvertTo-Json -Depth 10)
    }

    if (-not $overallOk) {
        exit 1
    }
}

function Invoke-JourneyVerify {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Manifest,
        [switch]$Live
    )

    $modeLabel = if ($Live) { "live" } else { "mock" }
    Write-Info "Verifying journey $Manifest ($modeLabel mode)..."
    $journeyBinary = Get-PhenotypeJourneyBinary
    $manifestPath = Resolve-JourneyVerifyTarget -Value $Manifest
    if ($Live) {
        & $journeyBinary verify $manifestPath --live
    } else {
        & $journeyBinary verify $manifestPath --mock
    }
}

function Invoke-JourneyCapture {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Id,
        [switch]$NonInteractive
    )

    $manifestDir = Join-Path $RepoRoot "docs/journeys/manifests/$Id"
    $manifestFile = Join-Path $manifestDir "manifest.json"
    $indexFile = Join-Path $RepoRoot "docs/journeys/manifests/index.json"

    # Validate manifest exists
    if (-not (Test-Path $manifestFile)) {
        Write-Error-Custom "Manifest not found at $manifestFile"

        # List available IDs
        if (Test-Path $indexFile) {
            $index = Get-Content $indexFile -Raw | ConvertFrom-Json
            $availableIds = $index | ForEach-Object { $_.id }
            Write-Host ""
            Write-Host "Available journey IDs:" -ForegroundColor Yellow
            $availableIds | ForEach-Object { Write-Host "  - $_" }
        }
        exit 2
    }

    # Parse manifest
    $manifest = Get-Content $manifestFile -Raw | ConvertFrom-Json
    $steps = $manifest.steps
    $keyframeCount = $manifest.keyframe_count

    # Warn if step count mismatch
    if ($steps.Count -ne $keyframeCount) {
        Write-Warn "Manifest has keyframe_count=$keyframeCount but steps array has $($steps.Count) entries. Continuing anyway."
    }

    Write-Info "Capturing journey $Id ($($steps.Count) steps)..."
    Write-Host ""

    # Iterate through steps
    $steps | ForEach-Object {
        $step = $_
        $stepIndex = $step.index
        $slug = $step.slug
        $intent = $step.intent
        $screenshotPath = $step.screenshot_path

        # Print step info
        Write-Host "Step $stepIndex : $slug" -ForegroundColor Cyan
        Write-Host "  Intent: $intent" -ForegroundColor Gray

        # Wait for user or sleep
        if ($NonInteractive) {
            Write-Info "Sleeping 3s (non-interactive mode)..."
            Start-Sleep -Seconds 3
        } else {
            Write-Host "  Press Enter to capture..." -NoNewline
            Read-Host | Out-Null
        }

        # Capture screenshot
        $framePath = Join-Path $manifestDir $screenshotPath
        Invoke-Screenshot -Path $framePath | Out-Null

        # Confirm and show file size
        if (Test-Path $framePath) {
            $fileSize = (Get-Item $framePath).Length
            Write-Success "  Saved $screenshotPath ($fileSize bytes)"
        } else {
            Write-Error-Custom "  Failed to save $screenshotPath"
            exit 1
        }

        Write-Host ""
    }

    # Summary
    Write-Success "Journey $Id captured. Run 'wsm3d journey verify -Id $Id' to validate."
}

function Invoke-Watch {
    param(
        [switch]$Launch,
        [string]$Filter = "*.cs"
    )

    $codeDir = Join-Path $RepoRoot "WorldSphereMod/Code"
    if (-not (Test-Path $codeDir)) {
        throw "Code directory not found at $codeDir"
    }

    Write-Info "Watching $codeDir for changes (filter: $Filter)..."
    if ($Launch) {
        $gameRunning = Get-Process worldbox -ErrorAction SilentlyContinue
        if (-not $gameRunning) {
            Write-Info "Launching WorldBox..."
            Invoke-Launch
            Start-Sleep -Seconds 5
        } else {
            Write-Warn "WorldBox already running."
        }
    }

    $watcher = New-Object System.IO.FileSystemWatcher
    $watcher.Path = $codeDir
    $watcher.Filter = $Filter
    $watcher.IncludeSubdirectories = $true
    $watcher.EnableRaisingEvents = $false

    $script:lastFireMs = 0
    $script:debounceMs = 1500
    $debounceTimer = New-Object System.Timers.Timer
    $debounceTimer.Interval = 500
    $debounceTimer.AutoReset = $false

    $onTimerElapsed = {
        $now = [DateTime]::UtcNow.Ticks / 10000
        if ($now - $script:lastFireMs -ge $script:debounceMs) {
            $debounceTimer.Stop()

            Write-Host "`n" -NoNewline
            Write-Info "Change detected, installing..."
            try {
                Invoke-Install -NoBuild:$false
                $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
                Write-Success "✓ installed at $ts; click Reload in NML to see changes"
            } catch {
                Write-Error-Custom "Install failed: $($_.Exception.Message)"
            }

            $script:lastFireMs = 0
            $watcher.EnableRaisingEvents = $true
        }
    }

    $onFileChange = {
        $script:lastFireMs = [DateTime]::UtcNow.Ticks / 10000
        $watcher.EnableRaisingEvents = $false
        $debounceTimer.Stop()
        $debounceTimer.Start()
    }

    Register-ObjectEvent -InputObject $watcher -EventName "Changed" -Action $onFileChange | Out-Null
    Register-ObjectEvent -InputObject $watcher -EventName "Created" -Action $onFileChange | Out-Null
    Register-ObjectEvent -InputObject $watcher -EventName "Renamed" -Action $onFileChange | Out-Null
    Register-ObjectEvent -InputObject $watcher -EventName "Deleted" -Action $onFileChange | Out-Null
    Register-ObjectEvent -InputObject $debounceTimer -EventName "Elapsed" -Action $onTimerElapsed | Out-Null

    Write-Success "Watching (Ctrl+C to stop)..."
    $watcher.EnableRaisingEvents = $true
    $debounceTimer.Start()

    try {
        while ($true) {
            Start-Sleep -Seconds 1
        }
    } finally {
        Write-Info "Cleaning up..."
        $watcher.EnableRaisingEvents = $false
        $debounceTimer.Stop()
        Get-EventSubscriber | Where-Object { $_.SourceObject -eq $watcher -or $_.SourceObject -eq $debounceTimer } | Unregister-Event
        $watcher.Dispose()
        $debounceTimer.Dispose()
        Write-Success "Watch stopped."
    }
}

function Resolve-PythonCommand {
    foreach ($name in @("python", "python3", "py")) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) {
            return $cmd.Source
        }
    }

    throw "Python not found on PATH (required for playcua)."
}

function Invoke-PlaycuaRunAll {
    param(
        [ValidateSet("omniroute", "anthropic", "off")]
        [string]$VisionBackend
    )

    $python = Resolve-PythonCommand
    $playcuaMain = Join-Path $RepoRoot "Tools/wsm3d-playcua/main.py"
    if (-not (Test-Path -LiteralPath $playcuaMain)) {
        throw "Missing Tools/wsm3d-playcua/main.py"
    }

    $scenarioRoot = Join-Path $RepoRoot "Tools/wsm3d-playcua/sample-scenarios"
    if (-not (Test-Path -LiteralPath $scenarioRoot)) {
        throw "PlayCUA sample-scenarios directory not found at $scenarioRoot"
    }

    $scenarios = @(Get-ChildItem -Path $scenarioRoot -Filter "*.yaml" -File | Sort-Object Name)
    if ($scenarios.Count -eq 0) {
        throw "No YAML scenarios found under $scenarioRoot"
    }

    $artifactRoot = Join-Path $RepoRoot "Tools/wsm3d-playcua/.reports/run-all-artifacts"
    if (-not (Test-Path -LiteralPath $artifactRoot)) {
        New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
    }

    $failed = @()
    foreach ($scenario in $scenarios) {
        $scenarioReport = Join-Path $artifactRoot ("playcua-" + $scenario.BaseName + ".json")
        $pyArgs = @(
            $playcuaMain,
            $scenario.FullName,
            "--report", $scenarioReport
        )
        if ($VisionBackend) {
            $pyArgs += @("--vision-backend", $VisionBackend)
        }

        Write-Info "playcua run-all: $($scenario.Name) ..."
        & $python @pyArgs
        if ($LASTEXITCODE -ne 0) {
            $failed += $scenario.Name
        }
    }

    if ($failed.Count -gt 0) {
        throw "playcua run-all failed for: $($failed -join ', ')"
    }

    Write-Success "playcua run-all passed $($scenarios.Count) scenario(s)."
}

function Invoke-SubmoduleInit {
    $gitCmd = Get-Command git -ErrorAction SilentlyContinue
    if (-not $gitCmd) {
        throw "git not found on PATH"
    }

    Push-Location $RepoRoot
    try {
        foreach ($path in $script:GitSubmodulePaths) {
            Write-Info "Initializing submodule $path..."
            & git submodule update --init --recursive $path
            if ($LASTEXITCODE -ne 0) {
                throw "git submodule update --init --recursive failed for $path (exit $LASTEXITCODE)"
            }
        }
        Write-Success "Submodule(s) initialized: $($script:GitSubmodulePaths -join ', ')"
    } finally {
        Pop-Location
    }
}

function Invoke-HooksInstall {
    Write-Info "Installing git pre-commit hook..."

    try {
        Push-Location $script:RepoRoot

        # Set the hooksPath
        & git config core.hooksPath .githooks
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to set git core.hooksPath"
        }

        # Check if .githooks/pre-commit exists
        $preCommitPath = Join-Path $script:RepoRoot ".githooks/pre-commit"
        if (-not (Test-Path $preCommitPath)) {
            throw ".githooks/pre-commit not found. Repository may be missing git hooks."
        }

        # Make it executable (git update-index on Windows is more reliable)
        & git update-index --chmod=+x .githooks/pre-commit
        if ($LASTEXITCODE -ne 0) {
            Write-Warn "Warning: Could not update executable bit on .githooks/pre-commit"
        }

        Write-Success "Hooks installed successfully."
        Write-Info ""
        Write-Info "Test the hook with:"
        Write-Host "  `$null = New-Item -ItemType File -Force tmp_test.txt -Value 'x'" -ForegroundColor Gray
        Write-Host "  git add tmp_test.txt" -ForegroundColor Gray
        Write-Host "  & .\.githooks\pre-commit.ps1" -ForegroundColor Gray
        Write-Host "  git reset HEAD tmp_test.txt" -ForegroundColor Gray
        Write-Host "  Remove-Item tmp_test.txt" -ForegroundColor Gray

    } catch {
        throw "Failed to install hooks: $($_.Exception.Message)"
    } finally {
        Pop-Location
    }
}

function Show-Help {
    Write-Host @"
WorldSphereMod3D CLI — Command Surface

Usage: wsm3d <command> [options]

Commands:
  build [-Configuration Release|Debug]
      Build WorldSphereMod.csproj via dotnet. Sets WORLDBOX_PATH env var.

  install [-Launch] [-NoBuild]
      Install sources to the mod folder. Optionally build first, then launch.

  launch
      Start WorldBox via Steam (steam://rungameid/1206560).

  kill
      Force-stop the WorldBox process. Destructive — requires confirmation.

  relaunch [-NoBuild]
      Kill, install, launch. Full cycle (skips build if -NoBuild).

  log [-Tail N] [-Follow] [-Grep <pattern>]
      Read Player.log. Default tail=50. Use -Follow to stream. -Grep filters lines.

  profile [-DryRun] [-Json]
      Start WorldBox if needed, wait 90s for InitProfiler, then kill and parse the latest Player.log
      for [WSM3D] InitProfiler name=duration_s pairs. Prints bucketed totals, sorted slowest first.
      -DryRun parses the latest log without launching/killing WorldBox.

  render-budget [-DryRun] [-Json]
      Parse the latest Player.log for [WSM3D] InitProfiler lines and RuntimeStatsOverlay
      FrameDrawCalls/DrawCalls history. Prints operation time, Init percent, cumulative percent,
      and sorts slowest first. -DryRun parses without launching. -Json emits machine-readable data.

  screenshot [-Path <file>] [-WindowOnly] [-Region <x,y,width,height>]
      Capture the desktop or WorldBox window to PNG. Use -Path to specify output.
      -WindowOnly captures the WorldBox window bounds.
      -Region accepts x,y,width,height and crops the capture to that rectangle.

  screenshot phase <n> -Name <slug> [-WindowOnly]
      Capture a smoke-test comparison frame to docs/screenshots/phase-N-<slug>.png.
      Phases 1-10 document before/after/closeup slugs in docs/smoke-test-phase*.md.
      Other phases accept any simple slug (e.g. before, after) matching phase-N-*.png.

  settings get [-Key <field>]
      Print all settings or one field as JSON. Field names are camelCase (e.g., VoxelEntities).

  settings set -Key <field> -Value <bool|number>
      Patch one setting. Value is parsed to match the field type.
      Refuses to write while WorldBox is running unless -Force is supplied.

  toggle -Phase <name>
      Flip a phase flag on/off. Name can be camelCase (VoxelEntities) or snake_case (voxel_entities).

  status [-Json]
      Print build state, game running, log mtime, and last live-verify test counts when
      Tools/.reports/live-verify-latest.json exists. Use -Json for machine-readable output.

  doctor [-Json]
      Environment diagnostics: WORLDBOX_PATH, dotnet SDK, python, git submodules,
      phenotype-journey binary, BridgeRPC :8766, optional OmniRoute :20128.
      Prints a human summary; add -Json for a machine-readable report.

  phases list [-Json]
      List all 10 phase flags with current and default values. Use -Json for machine-readable output.

  phases enable-all [-Force]
      Turn every phase flag on (smoke tests / full-stack repros). Refuses while WorldBox is running unless -Force.

  phases preset safe-min [-Force]
      Reset all phase flags to SavedSettings factory defaults (minimal stable baseline).

  journey capture -Id <id> [-NonInteractive]
      Capture screenshots for a journey manifest by ID.

  journey verify -Id <id>|<manifest-path> [-Live]
      Verify a manifest with phenotype-journey. Defaults to mock mode (--mock).

  playcua run-all [-VisionBackend omniroute|anthropic|off]
      Run every Tools/wsm3d-playcua/sample-scenarios/*.yaml via python main.py.
      Requires bridge on 127.0.0.1:8766. Writes per-scenario JSON under
      Tools/wsm3d-playcua/.reports/run-all-artifacts/. Omit -VisionBackend to use
      main.py defaults (OMNROUTE_API_KEY / ANTHROPIC_API_KEY env).

  watch [-Launch] [-Filter <pattern>]
      Watch WorldSphereMod/Code/ for changes (default filter: *.cs). On file change
      (debounced 1500ms), run install. Optional -Launch starts the game once at
      startup if not already running. -Filter allows narrower watch (e.g., *.shader).
      Press Ctrl+C to exit.

  hooks install
      Install the repo-tracked git pre-commit hooks. Sets git config core.hooksPath .githooks.
      Run once after clone.

  submodule init
      Initialize git submodules (External/Compound-Spheres) via
      git submodule update --init --recursive. Use after clone when doctor reports
      git_submodules fail.

  help
      Show this help text.

Environment:
  WORLDBOX_PATH  Override the default WorldBox install path.
                 Default: C:/Program Files (x86)/Steam/steamapps/common/Worldbox

Examples:
  wsm3d build
  wsm3d install -Launch
  wsm3d relaunch
  wsm3d log -Follow -Grep "VoxelEntities"
  wsm3d render-budget -DryRun
  wsm3d render-budget -DryRun -Json
  wsm3d settings get
  wsm3d settings set -Key VoxelEntities -Value true
  wsm3d settings set -Key VoxelEntities -Value true -Force
  wsm3d screenshot -Path .\smoke.png -WindowOnly
  wsm3d screenshot -Path .\crop.png -Region 100,100,800,600
  wsm3d screenshot phase 1 -Name before -WindowOnly
  wsm3d screenshot phase 2 -Name buildings -WindowOnly
  wsm3d journey capture -Id sample-journey -NonInteractive
  wsm3d toggle -Phase voxel_entities
  wsm3d phases enable-all
  wsm3d phases preset safe-min
  wsm3d status -Json
  wsm3d doctor
  wsm3d doctor -Json
  wsm3d submodule init
  wsm3d   journey verify -Id sample-journey
  wsm3d journey verify docs/journeys/manifests/sample-journey/manifest.json -Live
  wsm3d playcua run-all -VisionBackend omniroute

"@ -ForegroundColor White
}

# === Dispatcher ===
$command = if ($args.Count -gt 0) { $args[0] } else { "" }
$commandArgs = @(if ($args.Count -gt 1) { $args[1..($args.Count - 1)] })

try {
    switch -Exact ($command) {
        "build" {
            $params = @{}
            if ($commandArgs -contains "-Configuration") {
                $params["Configuration"] = $commandArgs[$commandArgs.IndexOf("-Configuration") + 1]
            }
            Invoke-Build @params
        }

        "install" {
            $params = @{
                Launch = $commandArgs -contains "-Launch"
                NoBuild = $commandArgs -contains "-NoBuild"
            }
            Invoke-Install @params
        }

        "launch" {
            Invoke-Launch
        }

        "kill" {
            Invoke-Kill
        }

        "relaunch" {
            $params = @{
                NoBuild = $commandArgs -contains "-NoBuild"
            }
            Invoke-Relaunch @params
        }

        "log" {
            $params = @{ Tail = 50 }
            $params["Follow"] = $commandArgs -contains "-Follow"

            if ($commandArgs -contains "-Tail") {
                $params["Tail"] = [int]$commandArgs[$commandArgs.IndexOf("-Tail") + 1]
            }
            if ($commandArgs -contains "-Grep") {
                $params["Grep"] = $commandArgs[$commandArgs.IndexOf("-Grep") + 1]
            }

            Invoke-Log @params
        }

        "profile" {
            $params = @{
                DryRun = $commandArgs -contains "-DryRun"
                Json = $commandArgs -contains "-Json"
            }
            Invoke-Profile @params
        }

        "render-budget" {
            $params = @{
                DryRun = $commandArgs -contains "-DryRun"
                Json = $commandArgs -contains "-Json"
            }
            Invoke-RenderBudget @params
        }

        "screenshot" {
            if ($commandArgs.Count -gt 0 -and $commandArgs[0] -eq "phase") {
                $subArgs = @(if ($commandArgs.Count -gt 1) { $commandArgs[1..($commandArgs.Count - 1)] })
                if ($subArgs.Count -eq 0) {
                    Write-Error-Custom "screenshot phase requires a phase number (1-10) and -Name <slug>"
                    Show-Help
                    exit 1
                }

                $phaseNum = 0
                if (-not [int]::TryParse($subArgs[0], [ref]$phaseNum)) {
                    throw "Invalid phase number '$($subArgs[0])'. Use 1-10."
                }

                $name = $null
                if ($subArgs -contains "-Name") {
                    $name = $subArgs[$subArgs.IndexOf("-Name") + 1]
                } else {
                    foreach ($arg in $subArgs[1..($subArgs.Count - 1)]) {
                        if ($arg -notlike "-*") {
                            $name = $arg
                            break
                        }
                    }
                }

                if (-not $name) {
                    Write-Error-Custom "screenshot phase requires -Name <slug> (e.g. before, after, buildings)"
                    Show-Help
                    exit 1
                }

                $params = @{
                    Phase = $phaseNum
                    Name = $name
                    WindowOnly = $subArgs -contains "-WindowOnly"
                }
                Invoke-ScreenshotPhase @params
            } else {
                $params = @{}
                if ($commandArgs -contains "-Path") {
                    $params["Path"] = $commandArgs[$commandArgs.IndexOf("-Path") + 1]
                }
                if ($commandArgs -contains "-WindowOnly") {
                    $params["WindowOnly"] = $true
                }
                if ($commandArgs -contains "-Region") {
                    $params["Region"] = $commandArgs[$commandArgs.IndexOf("-Region") + 1]
                }
                Invoke-Screenshot @params
            }
        }

        "settings" {
            if ($commandArgs.Count -eq 0) {
                Write-Error-Custom "settings requires 'get' or 'set' subcommand"
                Show-Help
                exit 1
            }
            $subCmd = $commandArgs[0]
            $subArgs = @(if ($commandArgs.Count -gt 1) { $commandArgs[1..($commandArgs.Count - 1)] })

            switch -Exact ($subCmd) {
                "get" {
                    $params = @{}
                    if ($subArgs -contains "-Key") {
                        $params["Key"] = $subArgs[$subArgs.IndexOf("-Key") + 1]
                    }
                    Invoke-SettingsGet @params
                }

                "set" {
                    $params = @{}
                    if ($subArgs -contains "-Key") {
                        $params["Key"] = $subArgs[$subArgs.IndexOf("-Key") + 1]
                    }
                    if ($subArgs -contains "-Value") {
                        $params["Value"] = $subArgs[$subArgs.IndexOf("-Value") + 1]
                    }
                    if ($subArgs -contains "-Force") {
                        $params["Force"] = $true
                    }
                    Invoke-SettingsSet @params
                }

                default {
                    Write-Error-Custom "Unknown settings subcommand: $subCmd"
                    Show-Help
                    exit 1
                }
            }
        }

        "toggle" {
            $params = @{}
            if ($commandArgs -contains "-Phase") {
                $params["Phase"] = $commandArgs[$commandArgs.IndexOf("-Phase") + 1]
            }
            Invoke-Toggle @params
        }

        "status" {
            $params = @{
                Json = $commandArgs -contains "-Json"
            }
            Invoke-Status @params
        }

        "doctor" {
            $params = @{
                Json = $commandArgs -contains "-Json"
            }
            Invoke-Doctor @params
        }

        "phases" {
            if ($commandArgs.Count -eq 0) {
                Write-Error-Custom "phases requires 'list' subcommand"
                Show-Help
                exit 1
            }
            $subCmd = $commandArgs[0]
            $subArgs = @(if ($commandArgs.Count -gt 1) { $commandArgs[1..($commandArgs.Count - 1)] })

            switch -Exact ($subCmd) {
                "list" {
                    $params = @{
                        Json = $subArgs -contains "-Json"
                    }
                    Invoke-PhasesList @params
                }

                "enable-all" {
                    $params = @{
                        Force = $subArgs -contains "-Force"
                    }
                    Invoke-PhasesEnableAll @params
                }

                "preset" {
                    if ($subArgs.Count -eq 0 -or $subArgs[0].StartsWith("-")) {
                        Write-Error-Custom "phases preset requires a preset name (e.g. safe-min)"
                        Show-Help
                        exit 1
                    }
                    $params = @{
                        Preset = $subArgs[0]
                        Force = $subArgs -contains "-Force"
                    }
                    Invoke-PhasesPreset @params
                }

                default {
                    Write-Error-Custom "Unknown phases subcommand: $subCmd"
                    Show-Help
                    exit 1
                }
            }
        }

        "journey" {
            if ($commandArgs.Count -eq 0) {
                Write-Error-Custom "journey requires 'capture' or 'verify' subcommand"
                Show-Help
                exit 1
            }
            $subCmd = $commandArgs[0]
            $subArgs = @(if ($commandArgs.Count -gt 1) { $commandArgs[1..($commandArgs.Count - 1)] })

            switch -Exact ($subCmd) {
                "capture" {
                    $params = @{}
                    if ($subArgs -contains "-Id") {
                        $params["Id"] = $subArgs[$subArgs.IndexOf("-Id") + 1]
                    } else {
                        Write-Error-Custom "journey capture requires a manifest ID (-Id)"
                        Show-Help
                        exit 1
                    }
                    $params["NonInteractive"] = $subArgs -contains "-NonInteractive"
                    Invoke-JourneyCapture @params
                }

                "verify" {
                    $params = @{}
                    if ($subArgs -contains "-Id") {
                        $params["Manifest"] = $subArgs[$subArgs.IndexOf("-Id") + 1]
                    } elseif ($subArgs.Count -gt 0 -and -not $subArgs[0].StartsWith("-")) {
                        $params["Manifest"] = $subArgs[0]
                    } else {
                        Write-Error-Custom "journey verify requires a manifest ID (-Id) or manifest path"
                        Show-Help
                        exit 1
                    }
                    $params["Live"] = $subArgs -contains "-Live"
                    Invoke-JourneyVerify @params
                }

                default {
                    Write-Error-Custom "Unknown journey subcommand: $subCmd"
                    Show-Help
                    exit 1
                }
            }
        }

        "watch" {
            $params = @{
                Launch = $commandArgs -contains "-Launch"
                Filter = "*.cs"
            }
            if ($commandArgs -contains "-Filter") {
                $params["Filter"] = $commandArgs[$commandArgs.IndexOf("-Filter") + 1]
            }
            Invoke-Watch @params
        }

        "playcua" {
            if ($commandArgs.Count -eq 0) {
                Write-Error-Custom "playcua requires 'run-all' subcommand"
                Show-Help
                exit 1
            }
            $subCmd = $commandArgs[0]
            $subArgs = @(if ($commandArgs.Count -gt 1) { $commandArgs[1..($commandArgs.Count - 1)] })

            switch -Exact ($subCmd) {
                "run-all" {
                    $params = @{}
                    if ($subArgs -contains "-VisionBackend") {
                        $params["VisionBackend"] = $subArgs[$subArgs.IndexOf("-VisionBackend") + 1]
                    }
                    Invoke-PlaycuaRunAll @params
                }

                default {
                    Write-Error-Custom "Unknown playcua subcommand: $subCmd"
                    Show-Help
                    exit 1
                }
            }
        }

        "hooks" {
            if ($commandArgs.Count -eq 0) {
                Write-Error-Custom "hooks requires 'install' subcommand"
                Show-Help
                exit 1
            }
            $subCmd = $commandArgs[0]
            $subArgs = @(if ($commandArgs.Count -gt 1) { $commandArgs[1..($commandArgs.Count - 1)] })

            switch -Exact ($subCmd) {
                "install" {
                    Invoke-HooksInstall
                }

                default {
                    Write-Error-Custom "Unknown hooks subcommand: $subCmd"
                    Show-Help
                    exit 1
                }
            }
        }

        "submodule" {
            if ($commandArgs.Count -eq 0) {
                Write-Error-Custom "submodule requires 'init' subcommand"
                Show-Help
                exit 1
            }
            $subCmd = $commandArgs[0]

            switch -Exact ($subCmd) {
                "init" {
                    Invoke-SubmoduleInit
                }

                default {
                    Write-Error-Custom "Unknown submodule subcommand: $subCmd"
                    Show-Help
                    exit 1
                }
            }
        }

        "help" {
            Show-Help
        }

        "" {
            Show-Help
        }

        default {
            Write-Error-Custom "Unknown command: $command"
            Show-Help
            exit 1
        }
    }
} catch {
    Write-Error-Custom $_.Exception.Message
    exit 1
}
