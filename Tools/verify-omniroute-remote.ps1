#Requires -Version 7.0
<#
.SYNOPSIS
  Probe OmniRoute from the desk: /models and /chat/completions must both succeed.

.DESCRIPTION
  Loads Tools/omniroute-vision.env, then mirrors do-all.ps1 omniroute probe logic.
  Exit 0 only when both endpoints respond; exit 1 on missing env, peer offline, or API failure.
#>
param(
    [string]$EnvFile = (Join-Path $PSScriptRoot 'omniroute-vision.env'),
    [string]$BaseUrl,
    [int]$ModelsTimeoutSec = 30,
    [int]$ChatTimeoutSec = 0
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'wsm3d.ps1')

function Import-OmniRouteEnv {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Error "Missing env file: $Path (copy Tools/omniroute-vision.env.example)"
    }
    Get-Content -LiteralPath $Path | ForEach-Object {
        if ($_ -match '^\s*([^#][^=]+)=(.*)$') {
            Set-Item -Path "env:$($matches[1].Trim())" -Value $matches[2].Trim()
        }
    }
}

function Test-KooshasLaptopOnline {
    param([string]$Hostname = 'kooshas-laptop')
    try {
        $tsStatus = & tailscale status 2>&1 | Out-String
        $escaped = [regex]::Escape($Hostname)
        if ($tsStatus -match "(?m)^[^\r\n]*\b$escaped\b[^\r\n]*\boffline\b") { return $false }
        if ($tsStatus -match "(?m)^[^\r\n]*\b$escaped\b") { return $true }
        return $null
    } catch {
        return $null
    }
}

Import-OmniRouteEnv -Path $EnvFile

if (-not $env:OMNROUTE_BASE_URL -or -not $env:OMNROUTE_API_KEY) {
    Write-Error 'OMNROUTE_BASE_URL and OMNROUTE_API_KEY must be set in omniroute-vision.env'
}

$laptopOnline = Test-KooshasLaptopOnline
if ($laptopOnline -eq $false) {
    Write-Host 'WARN kooshas-laptop offline on Tailscale — remote probe may fail until laptop wakes' -ForegroundColor Yellow
}

if ($BaseUrl) { $env:OMNROUTE_BASE_URL = $BaseUrl }
$base = $env:OMNROUTE_BASE_URL.TrimEnd('/')
if ($ChatTimeoutSec -le 0) {
    $ChatTimeoutSec = if ($base -match '^https://') { 120 } else { 25 }
}
$headers = @{ Authorization = "Bearer $env:OMNROUTE_API_KEY" }
$modelId = if ($env:OMNROUTE_VISION_COMBO) { $env:OMNROUTE_VISION_COMBO }
elseif ($env:OMNROUTE_VISION_MODEL) { $env:OMNROUTE_VISION_MODEL }
else { 'wsm3d-vision-frontier' }

Write-Host "OmniRoute probe: $base (model=$modelId)" -ForegroundColor Cyan

# /models
try {
    $models = Invoke-RestMethod -Uri "$base/models" -Headers $headers -TimeoutSec $ModelsTimeoutSec
    $modelCount = @($models.data).Count
    Write-Host "OK /models ($modelCount models)" -ForegroundColor Green
} catch {
    Write-Host "FAIL /models: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# /chat/completions (text — same gate as do-all.ps1; stream=false + SSE fallback)
try {
    $probeTimeout = if ($ChatTimeoutSec -gt 0) { $ChatTimeoutSec } else { 0 }
    $txt = Invoke-OmniRouteChatProbe -BaseUrl $base -ModelId $modelId -TimeoutSec $probeTimeout
    Write-Host "OK /chat/completions: $txt" -ForegroundColor Green
} catch {
    Write-Host "FAIL /chat/completions: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Laptop may need: tailscale serve --bg http://127.0.0.1:20128 — see Tools/setup-omniroute-laptop.md' -ForegroundColor DarkYellow
    exit 1
}

exit 0
