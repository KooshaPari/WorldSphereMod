#!/usr/bin/env pwsh
# agileplus-dispatch.ps1 - DAG-driven dispatch loop (Phase 2).
# Pulls the next READY WorkPackage from the AgilePlus DAG (deps Done + no file_scope
# overlap with an in-flight WP), claims it (-> doing), and emits the coder dispatch
# command for its file_scope. Dry-run by default; -Go actually dispatches.
#
# The DAG's next-ready guarantees two agents never edit the same file at once
# (structural worktree-isolation), so -Go is safe to fan out.
#
# Usage:
#   pwsh Tools/agileplus-dispatch.ps1            # show the next ready WP + the command (dry-run)
#   pwsh Tools/agileplus-dispatch.ps1 -All       # list ALL ready WPs
#   pwsh Tools/agileplus-dispatch.ps1 -Go        # claim the next ready WP (-> doing) + dispatch
param([switch]$Go, [switch]$All)
$ErrorActionPreference = 'Stop'
if (-not $env:AGILEPLUS_DB) { $env:AGILEPLUS_DB = 'C:/Users/koosh/.agileplus/wsm3d.db' }
$exe    = 'E:/cargo-target-phase0/debug/agileplus.exe'
$runner = 'C:/Users/koosh/.claude/tools/agent-runner/agent-runner.exe'
$wsmWt  = 'C:/Users/koosh/Dev/WSM3D-wt/render-foundation'
if (-not (Test-Path $exe)) { Write-Error "agileplus.exe not found at $exe"; exit 3 }

$ready = & $exe next-ready --json | ConvertFrom-Json
if (-not $ready -or $ready.Count -eq 0) { Write-Output "no ready WorkPackages (all blocked or done)"; exit 0 }

if ($All) {
  Write-Output "READY WorkPackages ($($ready.Count)):"
  foreach ($w in $ready) { Write-Output ("  wp{0}  {1}  [{2}]" -f $w.id, $w.title, ($w.file_scope -join ',')) }
  exit 0
}

$wp = $ready[0]
$files = $wp.file_scope -join ','
$prompt = "WSM3D WorkPackage wp$($wp.id): $($wp.title). Edit ONLY: $files. Acceptance (machine-verifiable): $($wp.acceptance_criteria). Build net48 via PowerShell (`$env:WORLDBOX_PATH='C:/Program Files (x86)/Steam/steamapps/common/worldbox'; dotnet build WorldSphereMod.csproj -c Release -f net48) to 0 errors. Commit technical/neutral. NO push. NEVER claim a visual win - report machine facts only."

Write-Output "NEXT READY: wp$($wp.id) - $($wp.title)"
Write-Output "  file_scope: $files"
Write-Output "  acceptance: $($wp.acceptance_criteria)"

if ($Go) {
  & $exe transition --wp $wp.id --to doing | Out-Null
  Write-Output "  claimed -> doing"
  & $runner dispatch $prompt --model gpt-5.5 --cwd $wsmWt
  Write-Output "  dispatched to agent-runner (gpt-5.5) on $wsmWt"
} else {
  Write-Output "  (dry-run) to dispatch: pwsh Tools/agileplus-dispatch.ps1 -Go"
  Write-Output "  command would be: agent-runner dispatch <prompt> --model gpt-5.5 --cwd $wsmWt"
}
