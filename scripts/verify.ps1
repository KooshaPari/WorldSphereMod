#requires -Version 5.1
<#
.SYNOPSIS
    WorldSphere3D + Compound-Spheres-3D audit verification script.

.DESCRIPTION
    Confirms that local C: and E: repos are in sync with the GitHub backup
    repos and runs the full test suite (unless -SkipTests is passed).

    Designed to be invoked from a PowerShell host with:
        powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1

    Or with options:
        powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1 -SkipTests
        powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1 -SkipRemote

.PARAMETER SkipTests
    Skip running the 3 test suites (faster parity check only).

.PARAMETER SkipRemote
    Skip fetching remote SHAs from GitHub (offline use).

.EXAMPLE
    pwsh scripts\verify.ps1
    # Full SHA parity check + 3 test suites

.EXAMPLE
    pwsh scripts\verify.ps1 -SkipTests
    # Just check local + remote SHA parity

.NOTES
    Author : Forge (KooshaPari audit)
    Repo   : https://github.com/KooshaPari/WorldSphereMod
    Audit  : C:\Users\koosh\_wsm3d_audit\AUDIT_REPORT.md
#>

[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$SkipRemote
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

# --- Configuration ----------------------------------------------------------

$RootDrive     = 'C:\Users\koosh'
$WS3DC         = Join-Path $RootDrive 'Dev\WorldSphereMod'
$WS3DE         = 'E:\dev\WorldSphereMod'
$CS3DPath      = 'External\Compound-Spheres'

$WS3DBackup    = 'KooshaPari/WorldSphereMod'
$CS3DBackup    = 'KooshaPari/Compound-Spheres-3D-Backup'

$TestsUnit     = Join-Path $WS3DC 'tests\WorldSphereMod.Tests.Unit'
$TestsE2E      = Join-Path $WS3DC 'tests\WorldSphereMod.Tests.E2E'
$TestsCS3D     = Join-Path $WS3DC "$CS3DPath\Tests\CompoundSpheres.Tests"

# --- Output helpers ---------------------------------------------------------

function Ok($msg)   { Write-Host ("  [OK]   " + $msg) -ForegroundColor Green }
function Warn($msg) { Write-Host ("  [WARN] " + $msg) -ForegroundColor Yellow }
function Fail($msg) { Write-Host ("  [FAIL] " + $msg) -ForegroundColor Red }
function Step($msg) { Write-Host ("`n=== " + $msg + " ===") -ForegroundColor Cyan }

function LocalSha($path, $ref) {
    if (-not (Test-Path $path)) { return $null }
    $sha = git -C $path rev-parse $ref 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return $sha.Trim()
}

function GhApiCapture($path) {
    # gh api writes JSON to stdout. PowerShell's native pipeline loses multi-line
    # stdout from native exes in some hosts, and the .NET ProcessStartInfo
    # pattern doesn't reliably inherit gh auth state. The proven fix is to use
    # Add-Type C# helper which calls ProcessStartInfo in a way that preserves
    # the inherited gh auth token (verified by direct testing).
    if (-not ('GhRunner' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
public class GhRunner {
    public static string Run(string args) {
        var psi = new ProcessStartInfo();
        psi.FileName = "gh";
        psi.Arguments = args;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        var p = Process.Start(psi);
        string so = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return so;
    }
}
'@
    }
    $raw = [GhRunner]::Run("api $path")
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    return $raw
}

function ExtractSha($json) {
    if ([string]::IsNullOrWhiteSpace($json)) { return $null }
    # SHA is a 40-char hex string in JSON; the first match is the commit SHA.
    $pattern = '"sha"\s*:\s*"([a-fA-F0-9]{40})"'
    $match = [regex]::Match($json, $pattern)
    if ($match.Success) { return $match.Groups[1].Value }
    return $null
}

function GhSha($path) {
    $json = GhApiCapture $path
    return ExtractSha $json
}

function CompareShas($label, $a, $b) {
    if ($null -eq $a -or $null -eq $b) {
        Warn "$label : one or both SHAs missing (local=$a remote=$b)"
        return $false
    }
    if ($a -eq $b) {
        Ok "$label : $a"
        return $true
    }
    Fail "$label : MISMATCH local=$a remote=$b"
    return $false
}

# --- Step 1: local SHAs -----------------------------------------------------

Step "Step 1: Local SHA inventory"

$cMain   = LocalSha $WS3DC 'main'
$eMain   = LocalSha $WS3DE 'main'
$cCS3D   = LocalSha "$WS3DC\$CS3DPath" 'wsm3d/main'
$eCS3D   = LocalSha "$WS3DE\$CS3DPath" 'wsm3d/main'

Write-Host ("  C: WS3D main                 : {0}" -f $cMain)
Write-Host ("  E: WS3D main                 : {0}" -f $eMain)
Write-Host ("  C: CS3D wsm3d/main           : {0}" -f $cCS3D)
Write-Host ("  E: CS3D wsm3d/main           : {0}" -f $eCS3D)

# --- Step 2: remote SHAs ----------------------------------------------------

$ghWS3DMain    = $null
$ghWS3Dbackup  = $null
$ghCS3DMain    = $null

if (-not $SkipRemote) {
    Step "Step 2: Remote SHA inventory (GitHub)"
    $ghWS3DMain   = GhSha "repos/$WS3DBackup/branches/main"
    $ghWS3Dbackup = GhSha "repos/$WS3DBackup/branches/backup/e-main-snapshot"
    $ghCS3DMain   = GhSha "repos/$CS3DBackup/branches/wsm3d/main"

    Write-Host ("  GitHub WS3D main             : {0}" -f $ghWS3DMain)
    Write-Host ("  GitHub WS3D e-main-snapshot  : {0}" -f $ghWS3Dbackup)
    Write-Host ("  GitHub CS3D wsm3d/main       : {0}" -f $ghCS3DMain)
} else {
    Step "Step 2: Skipped (SkipRemote)"
}

# --- Step 3: parity checks --------------------------------------------------

Step "Step 3: SHA parity checks"

$parityAll = $true

if (-not $SkipRemote) {
    $parityAll = (CompareShas "WS3D C: main vs backup main"            $cMain   $ghWS3DMain)   -and $parityAll
    $parityAll = (CompareShas "WS3D E: main vs backup e-main-snapshot" $eMain   $ghWS3Dbackup) -and $parityAll
    $parityAll = (CompareShas "CS3D C: wsm3d/main vs backup main"      $cCS3D   $ghCS3DMain)   -and $parityAll
    $parityAll = (CompareShas "CS3D E: wsm3d/main vs backup main"      $eCS3D   $ghCS3DMain)   -and $parityAll
} else {
    Warn "Skipped remote comparisons (SkipRemote)"
}

# CS3D C: and E: should match each other locally (shared submodule repo).
$parityAll = (CompareShas "CS3D C: wsm3d/main == E: wsm3d/main (local)" $cCS3D $eCS3D) -and $parityAll

# --- Step 4: working tree state ---------------------------------------------

Step "Step 4: Working tree state"

function WorkingTree($path, $label) {
    if (-not (Test-Path $path)) {
        Warn "$label : path not found"
        return
    }
    $status = git -C $path status --porcelain 2>$null
    if ([string]::IsNullOrWhiteSpace($status)) {
        Ok "$label : clean"
    } else {
        Warn "$label : dirty ($((($status -split "`n").Count)) files)"
        $status | ForEach-Object { Write-Host "         $_" }
    }
}

WorkingTree $WS3DC                  "C: WS3D"
WorkingTree $WS3DE                  "E: WS3D"
WorkingTree "$WS3DC\$CS3DPath"      "C: CS3D"
WorkingTree "$WS3DE\$CS3DPath"      "E: CS3D"

# --- Step 5: stash counts (should be unchanged from audit) ------------------

Step "Step 5: Stash counts (preserved, not dropped)"

function StashCount($path, $label) {
    if (-not (Test-Path $path)) {
        Warn "$label : path not found"
        return
    }
    $count = (git -C $path stash list 2>$null | Measure-Object -Line).Lines
    Write-Host ("  {0,-22} : {1} stash(es)" -f $label, $count)
}

StashCount $WS3DC "C: WS3D stashes"
StashCount $WS3DE "E: WS3D stashes"

# --- Step 6: worktree counts (should be 1 each) -----------------------------

Step "Step 6: Worktree counts (should be 1 each)"

function WorktreeCount($path, $label) {
    if (-not (Test-Path $path)) {
        Warn "$label : path not found"
        return
    }
    $count = (git -C $path worktree list 2>$null | Measure-Object -Line).Lines
    $msg = "$label : $count worktree(s)"
    if ($count -eq 1) { Ok $msg } else { Warn $msg }
}

WorktreeCount $WS3DC                  "C: WS3D"
WorktreeCount $WS3DE                  "E: WS3D"
WorktreeCount "$WS3DC\$CS3DPath"      "C: CS3D"
WorktreeCount "$WS3DE\$CS3DPath"      "E: CS3D"

# --- Step 7: test suites ---------------------------------------------------

if ($SkipTests) {
    Step "Step 7: Skipped (SkipTests)"
} else {
    Step "Step 7: Test suite runs"

    function RunTest($name, $path) {
        if (-not (Test-Path $path)) {
            Warn "$name : path not found ($path)"
            return $false
        }
        Write-Host ("  Running {0}..." -f $name)
        Push-Location $path
        try {
            $output = dotnet test --no-build 2>&1 | Out-String
            if ($LASTEXITCODE -eq 0) {
                $passLine = ($output -split "`n" | Where-Object { $_ -match 'Passed!|Passed:' } | Select-Object -First 1)
                if ($passLine) { Ok "$name : $passLine.Trim()" } else { Ok "$name : passed" }
                return $true
            } else {
                $failLine = ($output -split "`n" | Where-Object { $_ -match 'Failed!|Failed:|FAILED' } | Select-Object -First 1)
                Fail "$name : $failLine"
                return $false
            }
        } finally {
            Pop-Location
        }
    }

    $testResult = $true
    $testResult = (RunTest "CompoundSpheres.Tests"     $TestsCS3D) -and $testResult
    $testResult = (RunTest "WorldSphereMod.Tests.Unit" $TestsUnit) -and $testResult
    $testResult = (RunTest "WorldSphereMod.Tests.E2E"  $TestsE2E)  -and $testResult

    if (-not $testResult) { $parityAll = $false }
}

# --- Summary ---------------------------------------------------------------

Step "Summary"

if ($parityAll) {
    Write-Host "  Result: ALL CHECKS PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host "  Result: ONE OR MORE CHECKS FAILED" -ForegroundColor Red
    exit 1
}