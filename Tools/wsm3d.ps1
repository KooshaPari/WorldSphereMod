#Requires -Version 5.0
<#
.SYNOPSIS
  WorldSphereMod3D CLI — unified entry point for build, install, launch, and diagnostics.

.DESCRIPTION
  One-file command dispatcher for WSM3D development. Routes to subcommands:
    build, install, launch, kill, relaunch, log, profile, screenshot, settings, toggle, status, journey, watch, help.

.EXAMPLE
  ./wsm3d.ps1 build
  ./wsm3d.ps1 install -Launch
  ./wsm3d.ps1 log -Follow
  ./wsm3d.ps1 profile -DryRun
  ./wsm3d.ps1 settings get -Key VoxelEntities
  ./wsm3d.ps1 settings set -Key VoxelEntities -Value true
  ./wsm3d.ps1 toggle -Phase VoxelEntities
  ./wsm3d.ps1 relaunch
  ./wsm3d.ps1 status -Json
  ./wsm3d.ps1 journey list
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

function Invoke-Screenshot {
    param(
        [string]$Path,
        [switch]$WindowOnly
    )

    Write-Info "Capturing screenshot..."
    Add-Type -AssemblyName System.Drawing

    $bounds = if ($WindowOnly) {
        $gameWnd = Get-Process worldbox -ErrorAction SilentlyContinue
        if (-not $gameWnd) {
            throw "WorldBox not running. Cannot capture window-only screenshot."
        }
        # Simple bounding box for now; P/Invoke windowing comes later
        throw "WindowOnly not yet implemented. Use full screen capture."
    } else {
        [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    }

    $bmp = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
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
        $Value
    )

    $settings = Get-SettingsJson

    if (-not ($settings | Get-Member -Name $Key)) {
        throw "Key '$Key' not found in settings."
    }

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

function Invoke-PhasesList {
    param([switch]$Json)

    $settings = Get-SettingsJson

    # Hardcoded defaults from SavedSettings.cs
    $defaults = @{
        "VoxelEntities"      = $false
        "ProceduralBuildings" = $false
        "CrossedQuadFoliage"  = $true
        "MeshWater"          = $false
        "HighShadows"        = $false
        "SkeletalAnimation"  = $false
        "WorldspaceUI"       = $true
        "DayNightCycle"      = $false
        "PostFX"             = $false
        "ParticleEffects"    = $true
    }

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

    if ($Json) {
        Write-Host ($status | ConvertTo-Json)
    } else {
        Write-Host ""
        Write-Host "Status Report" -ForegroundColor Cyan
        Write-Host "=============" -ForegroundColor Cyan
        Write-Host "  Last Commit      : $($status['LastCommit'])"
        Write-Host "  DLL (Release)    : $($status['DllModified']) ($($status['DllSize']) bytes)"
        Write-Host "  Mod Install      : $($status['ModInstalled'])"
        Write-Host "  Game Process     : $($status['GameProcess'])"
        Write-Host "  Player.log       : $($status['LogModified']) ($($status['LogSize']) bytes)"
        Write-Host ""
    }
}

function Invoke-JourneyList {
    Write-Info "Listing journeys (via phenotype-journey)..."
    $journeyCmd = Get-Command phenotype-journey -ErrorAction SilentlyContinue
    if (-not $journeyCmd) {
        throw "phenotype-journey not on PATH. See docs/journeys/README.md for setup."
    }
    & phenotype-journey list
}

function Invoke-JourneyRun {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Id
    )

    Write-Info "Running journey $Id (via phenotype-journey)..."
    $journeyCmd = Get-Command phenotype-journey -ErrorAction SilentlyContinue
    if (-not $journeyCmd) {
        throw "phenotype-journey not on PATH. See docs/journeys/README.md for setup."
    }
    & phenotype-journey run -id $Id
}

function Invoke-JourneyVerify {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Id
    )

    Write-Info "Verifying journey $Id (via phenotype-journey)..."
    $journeyCmd = Get-Command phenotype-journey -ErrorAction SilentlyContinue
    if (-not $journeyCmd) {
        throw "phenotype-journey not on PATH. See docs/journeys/README.md for setup."
    }
    & phenotype-journey verify -id $Id
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

  screenshot [-Path <file>] [-WindowOnly]
      Capture full screen to PNG. Use -Path to specify output. -WindowOnly not yet implemented.

  settings get [-Key <field>]
      Print all settings or one field as JSON. Field names are camelCase (e.g., VoxelEntities).

  settings set -Key <field> -Value <bool|number>
      Patch one setting. Value is parsed to match the field type.

  toggle -Phase <name>
      Flip a phase flag on/off. Name can be camelCase (VoxelEntities) or snake_case (voxel_entities).

  status [-Json]
      Print build state, game running, log mtime. Use -Json for machine-readable output.

  phases list [-Json]
      List all 10 phase flags with current and default values. Use -Json for machine-readable output.

  journey list
      List available journeys (delegates to phenotype-journey CLI).

  journey run -Id <id>
      Run a journey by ID (delegates to phenotype-journey CLI).

  journey verify -Id <id>
      Verify a journey by ID (delegates to phenotype-journey CLI).

  watch [-Launch] [-Filter <pattern>]
      Watch WorldSphereMod/Code/ for changes (default filter: *.cs). On file change
      (debounced 1500ms), run install. Optional -Launch starts the game once at
      startup if not already running. -Filter allows narrower watch (e.g., *.shader).
      Press Ctrl+C to exit.

  hooks install
      Install the repo-tracked git pre-commit hooks. Sets git config core.hooksPath .githooks.
      Run once after clone.

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
  wsm3d settings get
  wsm3d settings set -Key VoxelEntities -Value true
  wsm3d toggle -Phase voxel_entities
  wsm3d status -Json
  wsm3d journey list

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

        "screenshot" {
            $params = @{}
            if ($commandArgs -contains "-Path") {
                $params["Path"] = $commandArgs[$commandArgs.IndexOf("-Path") + 1]
            }
            if ($commandArgs -contains "-WindowOnly") {
                $params["WindowOnly"] = $true
            }
            Invoke-Screenshot @params
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

                default {
                    Write-Error-Custom "Unknown phases subcommand: $subCmd"
                    Show-Help
                    exit 1
                }
            }
        }

        "journey" {
            if ($commandArgs.Count -eq 0) {
                Write-Error-Custom "journey requires 'list', 'run', or 'verify' subcommand"
                Show-Help
                exit 1
            }
            $subCmd = $commandArgs[0]
            $subArgs = @(if ($commandArgs.Count -gt 1) { $commandArgs[1..($commandArgs.Count - 1)] })

            switch -Exact ($subCmd) {
                "list" {
                    Invoke-JourneyList
                }

                "run" {
                    $params = @{}
                    if ($subArgs -contains "-Id") {
                        $params["Id"] = $subArgs[$subArgs.IndexOf("-Id") + 1]
                    }
                    Invoke-JourneyRun @params
                }

                "verify" {
                    $params = @{}
                    if ($subArgs -contains "-Id") {
                        $params["Id"] = $subArgs[$subArgs.IndexOf("-Id") + 1]
                    }
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
