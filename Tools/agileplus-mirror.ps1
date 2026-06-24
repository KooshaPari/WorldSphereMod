#!/usr/bin/env pwsh
# agileplus-mirror.ps1 - AgilePlus is the MASTER tracker; this mirrors its state into a
# human-readable summary (and is the hook point for Claude task-list reconciliation).
# Usage: pwsh Tools/agileplus-mirror.ps1 [-Json]
param([switch]$Json)
$ErrorActionPreference = 'Stop'
if (-not $env:AGILEPLUS_DB) { $env:AGILEPLUS_DB = 'C:/Users/koosh/.agileplus/wsm3d.db' }
$exe = 'E:/cargo-target-phase0/debug/agileplus.exe'
if (-not (Test-Path $exe)) { Write-Error "agileplus.exe not found at $exe"; exit 3 }
$epics   = & $exe list-epics   --json | ConvertFrom-Json
$stories = & $exe list-stories --json | ConvertFrom-Json
$mirror = foreach ($e in $epics) {
  $es = $stories | Where-Object { $_.epic_id -eq $e.id }
  [pscustomobject]@{
    epic        = $e.title
    status      = $e.status
    stories     = $es.Count
    storyTitles = @($es | ForEach-Object { $_.title })
  }
}
if ($Json) {
  $mirror | ConvertTo-Json -Depth 5
} else {
  Write-Output "AgilePlus mirror (master = $($env:AGILEPLUS_DB)):"
  Write-Output ("  epics={0} stories={1}" -f $epics.Count, $stories.Count)
  foreach ($m in $mirror) {
    Write-Output ("  [E] {0} ({1}) - {2} stories" -f $m.epic, $m.status, $m.stories)
  }
}
