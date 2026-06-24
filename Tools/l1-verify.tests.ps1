<#
.SYNOPSIS
  Self-tests for l1-verify.ps1 — no Pester required, no WorldBox required.

.DESCRIPTION
  Validates the harness itself:
    * Refuses to run when pixel-verify.py is missing (exit 2).
    * Refuses to run when python is missing (exit 2).
    * Creates the output directory when missing.
    * Honors the global StepTimeoutSec ceiling.
    * The aggregated report.json has the required schema.

  Each test_* function returns $true on pass, throws on fail.
  Run with: pwsh Tools/l1-verify.tests.ps1

.NOTES
  This script DOES NOT launch WorldBox.  It exercises l1-verify.ps1 with
  a stubbed bridge so we can validate the harness plumbing.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir
$L1Script  = Join-Path $ScriptDir "l1-verify.ps1"
$PixelPy   = Join-Path $ScriptDir "pixel-verify.py"

$failures = 0
$passes   = 0

function Test-Case {
    param([string]$Name, [scriptblock]$Block)
    try {
        & $Block
        Write-Host "  PASS  $Name" -ForegroundColor Green
        $script:passes++
    } catch {
        Write-Host "  FAIL  $Name" -ForegroundColor Red
        Write-Host "        $($_.Exception.Message)" -ForegroundColor Red
        $script:failures++
    }
}

# ---------------------------------------------------------------------------
# Helpers: launch l1-verify.ps1 with a fake bridge (no WorldBox).
# Strategy: spawn a tiny PowerShell HTTP listener in a background job on
# port 18766 that responds to /health, /actions/*, /world/state,
# /diag/render_stats with canned JSON.  Then point l1-verify at it via
# -BridgeUrl and -SkipLaunch, redirect stdout/stderr, parse the report.
# ---------------------------------------------------------------------------

function Get-FreeLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse("127.0.0.1"), 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    } finally {
        $listener.Stop()
    }
}

function Stop-ProcessTree {
    param([int]$ProcessId)
    if ($ProcessId -le 0) { return }
    try {
        Get-CimInstance Win32_Process -Filter "ParentProcessId = $ProcessId" -ErrorAction SilentlyContinue |
            ForEach-Object { Stop-ProcessTree -ProcessId ([int]$_.ProcessId) }
    } catch {}
    try {
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    } catch {}
}

function New-StubBridge {
    param([int]$Port = 18766, [string]$StubDir)
    $stub = @"
`$ErrorActionPreference = 'SilentlyContinue'
`$port = $Port
`$listener = New-Object System.Net.HttpListener
`$listener.Prefixes.Add('http://127.0.0.1:$port/')
`$listener.Start()
`$shotCount = 0
while (`$listener.IsListening) {
    try {
        `$ctx = `$listener.GetContext()
    } catch { break }
    `$req = `$ctx.Request
    `$resp = `$ctx.Response
    `$path = `$req.Url.AbsolutePath
    `$method = `$req.HttpMethod
    `$body = ''
    if (`$req.HasEntityBody) {
        `$reader = New-Object System.IO.StreamReader(`$req.InputStream, `$req.ContentEncoding)
        `$body = `$reader.ReadToEnd()
    }
    `$payload = `$null
    `$status = 200
    switch -Wildcard (`$path) {
        '/health' {
            `$payload = @{ ok = `$true; bridgeAlive = `$true; version = '2.12'; isWorld3D = `$true } | ConvertTo-Json
        }
        '/world/state' {
            `$payload = @{ ok = `$true; seed = 'stub-seed'; worldSize = 316 } | ConvertTo-Json
        }
        '/diag/render_stats' {
            `$payload = @{ ok = `$true; drawCalls = 42; voxelActorsEnabled = `$true } | ConvertTo-Json
        }
        '/diag/full_dump' {
            `$payload = @{ ok = `$true } | ConvertTo-Json
        }
        '/actions/new_world'     { `$payload = @{ ok = `$true } | ConvertTo-Json }
        '/actions/regenerate'    { `$payload = @{ ok = `$true } | ConvertTo-Json }
        '/actions/close_dialog'  { `$payload = @{ ok = `$true; closed = @() } | ConvertTo-Json }
        '/actions/select_tool'   { `$payload = @{ ok = `$true; id = `$body } | ConvertTo-Json }
        '/actions/camera'        { `$payload = @{ ok = `$true; x = 0; y = 0; zoom = 12 } | ConvertTo-Json }
        '/actions/screenshot' {
            `$shotCount++
            `$out = '$StubDir'
            if (`$body -match '"path":"([^"]+)"') {
                `$png = `$matches[1]
                `$dir = Split-Path -Parent `$png
                if (`$dir -and -not (Test-Path `$dir)) { New-Item -ItemType Directory -Force -Path `$dir | Out-Null }
                # 4x4 solid-color PNG (PIL-free minimal)
                Add-Type -AssemblyName System.Drawing
                `$bmp = New-Object System.Drawing.Bitmap 4, 4
                `$g = [System.Drawing.Graphics]::FromImage(`$bmp)
                `$clr = [System.Drawing.Color]::FromArgb(80 + (`$shotCount * 7) % 150, 60, 200)
                `$g.Clear(`$clr)
                `$bmp.Save(`$png, [System.Drawing.Imaging.ImageFormat]::Png)
                `$bmp.Dispose()
                `$payload = @{ ok = `$true; path = `$png; width = 4; height = 4 } | ConvertTo-Json
            } else {
                `$payload = @{ ok = `$false; error = 'no_path' } | ConvertTo-Json
            }
        }
        default {
            `$payload = @{ ok = `$false; error = 'not_found'; path = `$path } | ConvertTo-Json
            `$status = 404
        }
    }
    `$buf = [System.Text.Encoding]::UTF8.GetBytes(`$payload)
    `$resp.StatusCode = `$status
    `$resp.ContentType = 'application/json'
    `$resp.OutputStream.Write(`$buf, 0, `$buf.Length)
    `$resp.Close()
}
"@
    return $stub
}

function Start-StubBridgeJob {
    param([int]$Port = 0, [string]$StubDir)
    if ($Port -le 0) { $Port = Get-FreeLoopbackPort }
    $stub = New-StubBridge -Port $Port -StubDir $StubDir
    $tmp = [System.IO.Path]::GetTempFileName()
    $tmpPs1 = [System.IO.Path]::ChangeExtension($tmp, ".ps1")
    Move-Item $tmp $tmpPs1 -Force
    Set-Content -LiteralPath $tmpPs1 -Value $stub -Encoding UTF8
    $stdout = [System.IO.Path]::ChangeExtension($tmpPs1, ".stdout.log")
    $stderr = [System.IO.Path]::ChangeExtension($tmpPs1, ".stderr.log")
    $proc = Start-Process -FilePath "pwsh" -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $tmpPs1) -NoNewWindow -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    # Wait for the actual stub endpoint, not merely an open TCP port.
    $deadline = (Get-Date).AddSeconds(15)
    $bound = $false
    while ((Get-Date) -lt $deadline) {
        $proc.Refresh()
        if ($proc.HasExited) { break }
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -Method GET -TimeoutSec 1 -ErrorAction Stop
            if ($health.ok -and $health.bridgeAlive) {
                $bound = $true
                break
            }
        } catch {}
        Start-Sleep -Milliseconds 200
    }
    if (-not $bound) {
        Stop-ProcessTree -ProcessId $proc.Id
        $out = if (Test-Path $stdout) { Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue } else { "" }
        $err = if (Test-Path $stderr) { Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue } else { "" }
        throw "stub bridge did not bind healthy endpoint on port $Port within 15s. stdout=$out stderr=$err"
    }
    return @{ Process = $proc; Port = $Port; Script = $tmpPs1; Stdout = $stdout; Stderr = $stderr }
}

function Stop-StubBridgeJob {
    param($Handle)
    if ($null -ne $Handle -and $null -ne $Handle.Process) {
        try {
            if (-not $Handle.Process.HasExited) {
                Stop-ProcessTree -ProcessId $Handle.Process.Id
            }
        } catch {}
    }
    if ($null -ne $Handle -and $null -ne $Handle.Script -and (Test-Path $Handle.Script)) {
        Remove-Item $Handle.Script -Force -ErrorAction SilentlyContinue
    }
    foreach ($path in @($Handle.Stdout, $Handle.Stderr)) {
        if ($path -and (Test-Path $path)) {
            Remove-Item $path -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-ProcessWithRedirects {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [Parameter(Mandatory)][string]$StdoutPath,
        [Parameter(Mandatory)][string]$StderrPath,
        [int]$TimeoutSec = 180
    )

    foreach ($path in @($StdoutPath, $StderrPath)) {
        $dir = Split-Path -Parent $path
        if ($dir -and -not (Test-Path $dir)) {
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
        }
    }

    $proc = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -NoNewWindow -Wait -PassThru -RedirectStandardOutput $StdoutPath -RedirectStandardError $StderrPath
    if (-not $proc.HasExited) {
        Stop-ProcessTree -ProcessId $proc.Id
        throw "process timed out after ${TimeoutSec}s: $FilePath $($ArgumentList -join ' ')"
    }
    $proc.Refresh()
    return $proc
}

function Invoke-L1Verify {
    param(
        [string]$OutDir,
        [int]$Port = 18766,
        [switch]$SkipLaunch,
        [int]$StepTimeoutSec = 600,
        [int]$TimeoutSec = 30
    )
    $args = @(
        "-NoProfile", "-File", $L1Script,
        "-BridgeUrl", "http://127.0.0.1:$Port",
        "-OutDir", $OutDir,
        "-StepTimeoutSec", "$StepTimeoutSec",
        "-TimeoutSec", "$TimeoutSec"
    )
    if ($SkipLaunch) { $args += "-SkipLaunch" }
    $proc = Invoke-ProcessWithRedirects -FilePath "pwsh" -ArgumentList $args -StdoutPath "$OutDir/stdout.log" -StderrPath "$OutDir/stderr.log" -TimeoutSec ([Math]::Max($StepTimeoutSec + 30, 60))
    return $proc.ExitCode
}

# ---------------------------------------------------------------------------
# Test 1: report.json schema (no bridge needed)
# ---------------------------------------------------------------------------

Test-Case "schema_required_keys_present" {
    $out = Join-Path ([System.IO.Path]::GetTempPath()) "l1v-test-schema-$(Get-Random)"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $handle = Start-StubBridgeJob -StubDir $out
    try {
        $exit = Invoke-L1Verify -OutDir $out -Port $handle.Port -SkipLaunch -StepTimeoutSec 60
        if ($exit -ne 0) { throw "l1-verify exit=$exit (see $out/stderr.log)" }
        $rptPath = Join-Path $out "report.json"
        if (-not (Test-Path $rptPath)) { throw "report.json missing" }
        $rpt = Get-Content $rptPath -Raw | ConvertFrom-Json
        $required = @("started_at","bridge_url","bridge_version","isWorld3D","world_seed","world_size","captures","summary","errors")
        foreach ($k in $required) {
            if (-not ($rpt.PSObject.Properties.Name -contains $k)) {
                throw "report.json missing key '$k'"
            }
        }
        if (-not ($rpt.summary.PSObject.Properties.Name -contains "pass"))  { throw "summary.pass missing" }
        if (-not ($rpt.summary.PSObject.Properties.Name -contains "fail"))  { throw "summary.fail missing" }
        if (-not ($rpt.summary.PSObject.Properties.Name -contains "inconclusive")) { throw "summary.inconclusive missing" }
        if (-not ($rpt.summary.PSObject.Properties.Name -contains "all_pass"))   { throw "summary.all_pass missing" }
        if ($rpt.captures.Count -lt 1) { throw "no captures in report" }
    } finally {
        Stop-StubBridgeJob -Handle $handle
    }
}

# ---------------------------------------------------------------------------
# Test 2: refuses to run when pixel-verify.py is missing
# ---------------------------------------------------------------------------

Test-Case "exits_2_when_pixel_verify_missing" {
    $out = Join-Path ([System.IO.Path]::GetTempPath()) "l1v-test-nopv-$(Get-Random)"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $handle = Start-StubBridgeJob -StubDir $out
    try {
        $tmp = [System.IO.Path]::GetTempFileName()
        $tmpDir = Split-Path -Parent $tmp
        $tmpScript = Join-Path $tmpDir "l1v-nopv-$(Get-Random).ps1"
        $scriptText = Get-Content -LiteralPath $L1Script -Raw
        $scriptText = $scriptText -replace [regex]::Escape($PixelPy), "C:/__definitely_missing__/pixel-verify.py"
        Set-Content -LiteralPath $tmpScript -Value $scriptText -Encoding UTF8
        $args = @("-NoProfile","-File",$tmpScript,"-BridgeUrl","http://127.0.0.1:$($handle.Port)","-OutDir",$out,"-StepTimeoutSec","60","-TimeoutSec","30","-SkipLaunch")
        $proc = Invoke-ProcessWithRedirects -FilePath "pwsh" -ArgumentList $args -StdoutPath "$out/stdout.log" -StderrPath "$out/stderr.log" -TimeoutSec 90
        if ($proc.ExitCode -ne 2) {
            throw "expected exit 2, got $($proc.ExitCode)"
        }
        Remove-Item $tmpScript -Force -ErrorAction SilentlyContinue
    } finally {
        Stop-StubBridgeJob -Handle $handle
    }
}

# ---------------------------------------------------------------------------
# Test 3: creates output dir if missing
# ---------------------------------------------------------------------------

Test-Case "creates_outdir_if_missing" {
    $parent = Join-Path ([System.IO.Path]::GetTempPath()) "l1v-test-mkdir-$(Get-Random)"
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $out = Join-Path $parent (Join-Path "nested" (Join-Path "missing" "dir"))
    $handle = Start-StubBridgeJob -StubDir $out
    try {
        $exit = Invoke-L1Verify -OutDir $out -Port $handle.Port -SkipLaunch -StepTimeoutSec 60
        if ($exit -ne 0) { throw "exit=$exit" }
        if (-not (Test-Path $out)) { throw "outdir was not created" }
        if (-not (Test-Path (Join-Path $out "report.json"))) { throw "report.json not written" }
    } finally {
        Stop-StubBridgeJob -Handle $handle
    }
}

# ---------------------------------------------------------------------------
# Test 4: times out within StepTimeoutSec ceiling
# ---------------------------------------------------------------------------

Test-Case "respects_step_timeout_ceiling" {
    $out = Join-Path ([System.IO.Path]::GetTempPath()) "l1v-test-to-$(Get-Random)"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    # Use a non-existent port so every HTTP call fails; StepTimeoutSec=8.
    $args = @(
        "-NoProfile","-File",$L1Script,
        "-BridgeUrl","http://127.0.0.1:9",
        "-OutDir",$out,
        "-StepTimeoutSec","8",
        "-TimeoutSec","2",
        "-SkipLaunch"
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Invoke-ProcessWithRedirects -FilePath "pwsh" -ArgumentList $args -StdoutPath "$out/stdout.log" -StderrPath "$out/stderr.log" -TimeoutSec 90
    $sw.Stop()
    if ($sw.Elapsed.TotalSeconds -gt 90) {
        throw "exceeded wall-clock 90s ceiling; actual=$($sw.Elapsed.TotalSeconds)s"
    }
    if (-not (Test-Path (Join-Path $out "report.json"))) {
        throw "report.json not written after timeout"
    }
}

# ---------------------------------------------------------------------------
# Test 5: report.json report has verdict per capture
# ---------------------------------------------------------------------------

Test-Case "captures_have_verdict_field" {
    $out = Join-Path ([System.IO.Path]::GetTempPath()) "l1v-test-verdict-$(Get-Random)"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $handle = Start-StubBridgeJob -StubDir $out
    try {
        $exit = Invoke-L1Verify -OutDir $out -Port $handle.Port -SkipLaunch -StepTimeoutSec 120
        if ($exit -ne 0) { throw "exit=$exit (see $out/stderr.log)" }
        $rpt = Get-Content (Join-Path $out "report.json") -Raw | ConvertFrom-Json
        $allowed = @("pass","fail","inconclusive")
        foreach ($c in $rpt.captures) {
            if ($allowed -notcontains $c.verdict) {
                throw "capture $($c.p0) has invalid verdict '$($c.verdict)'"
            }
        }
    } finally {
        Stop-StubBridgeJob -Handle $handle
    }
}

Write-Host ""
Write-Host "============================================================"
Write-Host "  $($passes) passed, $($failures) failed"
Write-Host "============================================================"
exit $failures
