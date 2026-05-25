#!/usr/bin/env pwsh
# Copy PlayCUA desktop captures into docs/screenshots for HANDOFF gate tracking.

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Dest = Join-Path $RepoRoot 'docs/screenshots'
$sources = @(
    (Join-Path $RepoRoot 'Tools/wsm3d-playcua/artifacts'),
    (Join-Path $RepoRoot 'Tools/wsm3d-playcua/.reports/run-all-artifacts/artifacts'),
    (Join-Path $RepoRoot 'Tools/wsm3d-playcua/.reports/live-verify-artifacts/artifacts')
)

if (-not (Test-Path -LiteralPath $Dest)) {
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
}

$copied = 0
foreach ($src in $sources) {
    if (-not (Test-Path -LiteralPath $src)) { continue }
    Get-ChildItem -LiteralPath $src -Recurse -Filter '*.png' -File -ErrorAction SilentlyContinue | ForEach-Object {
        $rel = $_.FullName.Substring($src.Length).TrimStart('\', '/')
        $parts = $rel -split '[\\/]'
        $phaseDir = $parts | Where-Object { $_ -match '^phase-' } | Select-Object -First 1
        if (-not $phaseDir) { return }
        $slug = if ($parts.Count -gt 1) { [System.IO.Path]::GetFileNameWithoutExtension($parts[-1]) } else { 'capture' }
        $phaseNum = if ($phaseDir -match 'phase-(\d+)') { $Matches[1] } elseif ($phaseDir -match 'phase-(\d+)b') { $Matches[1] + 'b' } else { 'x' }
        $outName = "phase-$phaseNum-$slug.png"
        $outPath = Join-Path $Dest $outName
        Copy-Item -LiteralPath $_.FullName -Destination $outPath -Force
        $script:copied++
        Write-Host "Copied -> $outName"
    }
}

Write-Host "sync-playcua-screenshots: $copied file(s) in $Dest"
