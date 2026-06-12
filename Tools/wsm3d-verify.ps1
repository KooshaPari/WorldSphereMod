#requires -Version 5.1
<#
.SYNOPSIS
    Machine-metric render-foundation verification harness for WorldSphereMod3D (WSM3D).

.DESCRIPTION
    Verifies the render-foundation (commit 40f903d: built-in LIT shader fallback,
    per-vertex RecalculateNormals, ambient + directional Sun) by PROGRAMMATIC METRICS
    pulled from the in-game HTTP bridge -- NOT by reading screenshots.

    TIERED FALLBACK (always try cheaper, more-objective tiers first):

      TIER 1  (default, -Tier 1)  BRIDGE MACHINE METRICS  [PREFERRED]
              GET /telemetry  -> renderFoundation { actorMaterialShader,
                 terrainMaterialShader, ambientLight{r,g,b}, ambientMode,
                 sunPresent, sunIsDirectional, actorMeshVertCount,
                 terrainMeshVertCount } + actorDrawCumulative
              GET /diag/render_stats -> frameMs, drawCalls, visibleUnits
              Two /telemetry samples 1s apart confirm actorDrawCumulative is rising
              (actors actually drawing each frame). This is the source of truth.

      TIER 2  (-Tier 2)  RGB GetPixel SAMPLING  [LAST RESORT]
              ONLY runs for a criterion that TIER 1 reported as unavailable/inconclusive
              (a metric was null / the field is missing). Captures a frame via
              POST /actions/screenshot and samples pixel RGB. Pixel reads are noisy and
              easy to hallucinate over -- treat them as corroborating evidence only.

      TIER 3  (-Tier 3)  VLM (minimax) ANALYSIS  [FINAL FALLBACK -- NOT IMPLEMENTED]
              ONLY if TIER 1 + TIER 2 are BOTH inconclusive. Scaffolded as
              Invoke-MinimaxVlmCheck (a clearly-marked TODO stub). It is NOT called by
              default and performs no network call today.

    Exit code 0 iff every machine criterion PASSed; non-zero otherwise. If the bridge
    is unreachable the script fails fast (exit 3) with a clear "bridge down" message --
    it never hangs.

.PARAMETER BridgeUrl
    Base URL of the mod's HTTP bridge. Default http://127.0.0.1:8766 .

.PARAMETER Json
    Emit the structured verdict as JSON to stdout (machine-readable). Without it, a
    human-readable PASS/FAIL summary is printed.

.PARAMETER Tier
    Maximum fallback tier to attempt (1=bridge metrics only [default], 2=+pixels,
    3=+VLM stub). Tiers 2/3 only fire for criteria TIER 1 left inconclusive.

.EXAMPLE
    pwsh Tools/wsm3d-verify.ps1
    pwsh Tools/wsm3d-verify.ps1 -Json
    pwsh Tools/wsm3d-verify.ps1 -Tier 2 -BridgeUrl http://127.0.0.1:8766
#>
[CmdletBinding()]
param(
    [string]$BridgeUrl = 'http://127.0.0.1:8766',
    [switch]$Json,
    [ValidateRange(1, 3)]
    [int]$Tier = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Exit codes -----------------------------------------------------------
#   0 = all criteria PASS
#   1 = one or more criteria FAIL
#   2 = inconclusive (a required metric was unavailable and tiers exhausted)
#   3 = bridge down / unreachable
# --------------------------------------------------------------------------

$AMBIENT_TARGET = 0.4
$AMBIENT_TOL    = 0.08      # ambientLight within +/-0.08 of (0.4,0.4,0.4)
$ACTOR_VERT_MIN = 8         # > 8 verts => real volume, not a flat 4-vert quad
$FRAME_MS_BUDGET = 50.0     # report-only: 20fps soft budget
$LIT_SHADERS = @('Mobile/VertexLit', 'Standard', 'Mobile/Diffuse', 'Diffuse',
                 'WSM3D/OpaqueVertexColor', 'CompoundSphere')

function PF { param([bool]$Cond) if ($Cond) { 'PASS' } else { 'FAIL' } }

function Get-BridgeJson {
    param([string]$Path)
    $uri = "$BridgeUrl$Path"
    try {
        return Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec 5
    } catch {
        return $null
    }
}

function Test-BridgeUp {
    # Cheap liveness probe; returns the parsed /telemetry object or $null.
    return Get-BridgeJson -Path '/telemetry'
}

# --- TIER 2 helper: RGB GetPixel sampling (LAST RESORT) -------------------
# Captures a frame and samples pixel RGB. Only invoked for a criterion TIER 1
# left inconclusive. Pixel reads are noisy/subjective -- corroborating only.
function Invoke-PixelSampleCheck {
    param([string]$Criterion)
    try {
        $shot = Invoke-RestMethod -Uri "$BridgeUrl/actions/screenshot" -Method Post -TimeoutSec 8
        $path = $null
        if ($shot -and ($shot.PSObject.Properties.Name -contains 'path')) { $path = $shot.path }
        if (-not $path -or -not (Test-Path $path)) {
            return [pscustomobject]@{ tier = 2; criterion = $Criterion; result = 'INCONCLUSIVE'; note = 'screenshot path missing' }
        }
        Add-Type -AssemblyName System.Drawing
        $bmp = New-Object System.Drawing.Bitmap $path
        try {
            # Sample center pixel as a coarse "is anything lit / non-black" signal.
            $px = $bmp.GetPixel([int]($bmp.Width / 2), [int]($bmp.Height / 2))
            $lit = ($px.R + $px.G + $px.B) -gt 30
            return [pscustomobject]@{
                tier = 2; criterion = $Criterion
                result = if ($lit) { 'PASS' } else { 'FAIL' }
                note = "center RGB=($($px.R),$($px.G),$($px.B)) [pixel sample - corroborating only]"
            }
        } finally { $bmp.Dispose() }
    } catch {
        return [pscustomobject]@{ tier = 2; criterion = $Criterion; result = 'INCONCLUSIVE'; note = "pixel sample failed: $($_.Exception.Message)" }
    }
}

# --- TIER 3 stub: VLM (minimax) analysis (FINAL FALLBACK) -----------------
# TODO(final-fallback): NOT IMPLEMENTED and NOT called by default. Only wire this
# up if TIER 1 (bridge metrics) AND TIER 2 (pixels) are both inconclusive for a
# criterion. Should POST the captured frame to a minimax VLM endpoint and ask a
# yes/no question about render state, then map the answer to PASS/FAIL. Left as a
# scaffold so machine metrics are always preferred over a model's judgement.
function Invoke-MinimaxVlmCheck {
    param([string]$Criterion, [string]$ImagePath)
    throw 'Invoke-MinimaxVlmCheck is a TIER 3 final-fallback stub and is not implemented. Resolve via TIER 1 bridge metrics or TIER 2 pixels.'
}

function New-Criterion {
    param([string]$Name, [string]$Result, [string]$Detail)
    [pscustomobject]@{ name = $Name; result = $Result; detail = $Detail }
}

# ==========================================================================
# TIER 1 -- bridge machine metrics (default + preferred)
# ==========================================================================
$tel1 = Test-BridgeUp
if ($null -eq $tel1) {
    $msg = "bridge down at $BridgeUrl -- launch WorldBox + enter a 3D world, then retry. (mod bridge only listens while a 3D world is loaded)"
    if ($Json) {
        [pscustomobject]@{ ok = $false; reason = 'bridge_down'; bridgeUrl = $BridgeUrl; message = $msg } | ConvertTo-Json -Depth 6
    } else {
        Write-Host "FAIL  bridge unreachable" -ForegroundColor Red
        Write-Host "      $msg"
    }
    exit 3
}

$rf = $null
if ($tel1.PSObject.Properties.Name -contains 'renderFoundation') { $rf = $tel1.renderFoundation }

$renderStats = Get-BridgeJson -Path '/diag/render_stats'

# Second telemetry sample 1s later to measure actorDrawCumulative delta.
Start-Sleep -Seconds 1
$tel2 = Get-BridgeJson -Path '/telemetry'

$criteria = New-Object System.Collections.Generic.List[object]
$inconclusive = New-Object System.Collections.Generic.List[string]

# -- actorMaterialShader is a LIT built-in (not Sprites/Default / unlit) ----
$actorShader = if ($rf) { $rf.actorMaterialShader } else { $null }
if ([string]::IsNullOrWhiteSpace($actorShader)) {
    $criteria.Add((New-Criterion 'actorMaterialShader_lit' 'INCONCLUSIVE' 'actorMaterialShader unavailable (material not yet resolved -- spawn units / load 3D world)'))
    $inconclusive.Add('actorMaterialShader_lit')
} else {
    $isLit = ($LIT_SHADERS -contains $actorShader) -and ($actorShader -ne 'Sprites/Default')
    $criteria.Add((New-Criterion 'actorMaterialShader_lit' (PF $isLit) "shader='$actorShader'"))
}

# -- sunPresent && sunIsDirectional ----------------------------------------
if ($rf) {
    $sunOk = [bool]$rf.sunPresent -and [bool]$rf.sunIsDirectional
    $criteria.Add((New-Criterion 'sun_directional' (PF $sunOk) "sunPresent=$($rf.sunPresent) sunIsDirectional=$($rf.sunIsDirectional)"))
} else {
    $criteria.Add((New-Criterion 'sun_directional' 'INCONCLUSIVE' 'renderFoundation block absent'))
    $inconclusive.Add('sun_directional')
}

# -- ambientLight ~ (0.4,0.4,0.4) ------------------------------------------
if ($rf -and ($rf.PSObject.Properties.Name -contains 'ambientLight')) {
    $a = $rf.ambientLight
    $within = (([math]::Abs($a.r - $AMBIENT_TARGET) -le $AMBIENT_TOL) -and
               ([math]::Abs($a.g - $AMBIENT_TARGET) -le $AMBIENT_TOL) -and
               ([math]::Abs($a.b - $AMBIENT_TARGET) -le $AMBIENT_TOL))
    $criteria.Add((New-Criterion 'ambient_light' (PF $within) "ambient=($([math]::Round($a.r,3)),$([math]::Round($a.g,3)),$([math]::Round($a.b,3))) target=$AMBIENT_TARGET+/-$AMBIENT_TOL mode=$($rf.ambientMode)"))
} else {
    $criteria.Add((New-Criterion 'ambient_light' 'INCONCLUSIVE' 'ambientLight unavailable'))
    $inconclusive.Add('ambient_light')
}

# -- actorMeshVertCount > 8 (real volume, not flat quad) -------------------
if ($rf -and ($rf.PSObject.Properties.Name -contains 'actorMeshVertCount')) {
    $verts = [int]$rf.actorMeshVertCount
    if ($verts -le 0) {
        $criteria.Add((New-Criterion 'actor_mesh_volume' 'INCONCLUSIVE' 'actorMeshVertCount=0 (no actor voxel mesh submitted yet -- spawn units)'))
        $inconclusive.Add('actor_mesh_volume')
    } else {
        $criteria.Add((New-Criterion 'actor_mesh_volume' (PF ($verts -gt $ACTOR_VERT_MIN)) "actorMeshVertCount=$verts (>$ACTOR_VERT_MIN required)"))
    }
} else {
    $criteria.Add((New-Criterion 'actor_mesh_volume' 'INCONCLUSIVE' 'actorMeshVertCount unavailable'))
    $inconclusive.Add('actor_mesh_volume')
}

# -- actorDrawCumulative increasing across two samples ---------------------
$d1 = if ($tel1.PSObject.Properties.Name -contains 'actorDrawCumulative') { [long]$tel1.actorDrawCumulative } else { $null }
$d2 = if ($tel2 -and ($tel2.PSObject.Properties.Name -contains 'actorDrawCumulative')) { [long]$tel2.actorDrawCumulative } else { $null }
if ($null -eq $d1 -or $null -eq $d2) {
    $criteria.Add((New-Criterion 'actor_drawing_each_frame' 'INCONCLUSIVE' 'actorDrawCumulative unavailable'))
    $inconclusive.Add('actor_drawing_each_frame')
} else {
    $delta = $d2 - $d1
    $criteria.Add((New-Criterion 'actor_drawing_each_frame' (PF ($delta -gt 0)) "actorDrawCumulative $d1 -> $d2 (delta=$delta over ~1s)"))
}

# -- frameMs within budget (report-only) -----------------------------------
$frameMs = $null
if ($renderStats -and ($renderStats.PSObject.Properties.Name -contains 'frameMs')) { $frameMs = [double]$renderStats.frameMs }
elseif ($tel1.PSObject.Properties.Name -contains 'frameMs' -and $null -ne $tel1.frameMs) { $frameMs = [double]$tel1.frameMs }
$frameReport = if ($null -eq $frameMs) { 'frameMs unavailable' } else { "frameMs=$([math]::Round($frameMs,2)) budget=$FRAME_MS_BUDGET ($(if ($frameMs -le $FRAME_MS_BUDGET) { 'within' } else { 'OVER' }))" }

# ==========================================================================
# TIER 2 / TIER 3 escalation -- only for INCONCLUSIVE criteria
# ==========================================================================
$fallbackNotes = New-Object System.Collections.Generic.List[object]
if ($Tier -ge 2 -and $inconclusive.Count -gt 0) {
    foreach ($c in $inconclusive) {
        $fb = Invoke-PixelSampleCheck -Criterion $c
        $fallbackNotes.Add($fb)
        # Promote a TIER-2 verdict into the matching criterion if it resolved it.
        $match = $criteria | Where-Object { $_.name -eq $c } | Select-Object -First 1
        if ($match -and $fb.result -ne 'INCONCLUSIVE') {
            $match.result = $fb.result
            $match.detail = "$($match.detail) | TIER2(pixel): $($fb.note)"
        }
    }
    # TIER 3 (VLM) is intentionally NOT auto-invoked. If criteria remain
    # inconclusive after TIER 2, report them and let an operator opt in.
    if ($Tier -ge 3) {
        $fallbackNotes.Add([pscustomobject]@{ tier = 3; result = 'SKIPPED'; note = 'Invoke-MinimaxVlmCheck is a not-implemented final-fallback stub; resolve via TIER 1/2.' })
    }
}

# ==========================================================================
# Verdict
# ==========================================================================
$failed = @($criteria | Where-Object { $_.result -eq 'FAIL' })
$stillInconclusive = @($criteria | Where-Object { $_.result -eq 'INCONCLUSIVE' })
$allPass = ($failed.Count -eq 0) -and ($stillInconclusive.Count -eq 0)

$verdict = [pscustomobject]@{
    ok          = $allPass
    bridgeUrl   = $BridgeUrl
    tierUsed    = $Tier
    criteria    = $criteria
    frameReport = $frameReport
    fallback    = $fallbackNotes
    failedCount = $failed.Count
    inconclusiveCount = $stillInconclusive.Count
}

if ($Json) {
    $verdict | ConvertTo-Json -Depth 8
} else {
    Write-Host ""
    Write-Host "WSM3D render-foundation verdict  (bridge=$BridgeUrl, maxTier=$Tier)" -ForegroundColor Cyan
    Write-Host "------------------------------------------------------------"
    foreach ($c in $criteria) {
        $color = switch ($c.result) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
        Write-Host ("  {0,-5} {1,-26} {2}" -f $c.result, $c.name, $c.detail) -ForegroundColor $color
    }
    Write-Host ("  REPORT {0}" -f $frameReport) -ForegroundColor Gray
    if ($fallbackNotes.Count -gt 0) {
        Write-Host "  -- fallback tiers --" -ForegroundColor DarkGray
        foreach ($f in $fallbackNotes) { Write-Host ("    T{0} {1}" -f $f.tier, $f.note) -ForegroundColor DarkGray }
    }
    Write-Host "------------------------------------------------------------"
    if ($allPass) {
        Write-Host "ALL PASS" -ForegroundColor Green
    } else {
        Write-Host ("FAILED={0}  INCONCLUSIVE={1}" -f $failed.Count, $stillInconclusive.Count) -ForegroundColor Red
    }
}

if ($allPass) { exit 0 }
elseif ($failed.Count -gt 0) { exit 1 }
else { exit 2 }
