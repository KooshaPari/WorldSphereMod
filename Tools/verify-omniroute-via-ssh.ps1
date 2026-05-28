#Requires -Version 7.0
<#
.SYNOPSIS
  Probe OmniRoute on the MacBook via SSH (localhost:20128), bypassing funnel HTTPS.

.PARAMETER SshTarget
  Tailscale SSH target, e.g. kooshapari@kooshas-laptop or kooshapari@100.112.14.98

.PARAMETER UseTailscaleSsh
  Use `tailscale ssh` instead of OpenSSH `ssh`.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SshTarget,
    [switch]$UseTailscaleSsh,
    [string]$EnvFile = (Join-Path $PSScriptRoot 'omniroute-vision.env'),
    [int]$TimeoutSec = 60
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $EnvFile)) {
    Write-Error "Missing $EnvFile"
}
Get-Content -LiteralPath $EnvFile | ForEach-Object {
    if ($_ -match '^\s*([^#][^=]+)=(.*)$') {
        Set-Item -Path "env:$($matches[1].Trim())" -Value $matches[2].Trim()
    }
}
if (-not $env:OMNROUTE_API_KEY) {
    Write-Error 'OMNROUTE_API_KEY must be set in omniroute-vision.env'
}
$modelId = if ($env:OMNROUTE_VISION_MODEL) { $env:OMNROUTE_VISION_MODEL } else { 'gemini/gemini-2.5-flash' }
$key = $env:OMNROUTE_API_KEY -replace "'", "'\\''"

$remoteScript = @"
set -e
BASE='http://127.0.0.1:20128/v1'
AUTH='Authorization: Bearer $key'
echo "=== /models ==="
curl -sS -m $TimeoutSec "\$BASE/models" -H "\$AUTH" | head -c 200
echo ""
echo "=== /chat/completions ==="
BODY='{"model":"$modelId","max_tokens":16,"temperature":0,"messages":[{"role":"user","content":"Reply: vision-ok"}]}'
curl -sS -m $TimeoutSec "\$BASE/chat/completions" -H "\$AUTH" -H 'Content-Type: application/json' -d "\$BODY"
echo ""
"@

Write-Host "SSH probe via $SshTarget (OmniRoute on laptop loopback)" -ForegroundColor Cyan
if ($UseTailscaleSsh) {
    tailscale ssh $SshTarget "bash -lc $(($remoteScript -replace '"', '\"'))"
} else {
    ssh -o StrictHostKeyChecking=accept-new -o ConnectTimeout=20 $SshTarget "bash -lc '$($remoteScript -replace "'", "'\"'\"'")'"
}
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "SSH probe finished (check output above for models + chat JSON)" -ForegroundColor Green
