#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Phase 5 prep helper — documents cloning KooshaPari/Compound-Spheres-3D and optional submodule add.

.DESCRIPTION
  Prints the fork/submodule workflow for Phase 5 backend work. Does not mutate WorldSphereMod
  unless you run the commented git submodule commands below manually.

  Full checklist: docs/phase5-prep.md
#>

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Phase5PrepDoc = Join-Path $RepoRoot 'docs/phase5-prep.md'
$ForkUrl = 'https://github.com/KooshaPari/Compound-Spheres-3D.git'
$UpstreamUrl = 'https://github.com/MelvinShwuaner/Compound-Spheres.git'
$SubmodulePath = 'External/Compound-Spheres-3D'
$UpstreamPinExample = '73a7b77'

function Write-Phase5Step {
    param([string]$Message)
    Write-Host $Message
}

Write-Phase5Step ''
Write-Phase5Step 'Compound-Spheres-3D — Phase 5 setup (documentation only)'
Write-Phase5Step '============================================================'
Write-Phase5Step ''
Write-Phase5Step "Canonical guide: $Phase5PrepDoc"
if (Test-Path -LiteralPath $Phase5PrepDoc) {
    Write-Phase5Step '  (file exists — open for fork, Unity 2022.3, DLL swap, and smoke gates)'
} else {
    Write-Phase5Step '  [WARN] phase5-prep.md not found at expected path.'
}
Write-Phase5Step ''

Write-Phase5Step '1. Create the fork on GitHub (if not live yet)'
Write-Phase5Step '   - Fork https://github.com/MelvinShwuaner/Compound-Spheres'
Write-Phase5Step '   - Rename to Compound-Spheres-3D under KooshaPari'
Write-Phase5Step "   - Placeholder remote until published: $ForkUrl"
Write-Phase5Step ''

Write-Phase5Step '2. Clone the fork locally (scratch or dev machine)'
Write-Phase5Step @"
   git clone $ForkUrl
   cd Compound-Spheres-3D
   git remote add upstream $UpstreamUrl
   git fetch upstream
   git log -1 --oneline main
"@

$gitCmd = Get-Command git -ErrorAction SilentlyContinue
if ($gitCmd) {
    Write-Phase5Step ''
    Write-Phase5Step '   Checking whether the fork remote responds...'
    $lsRemote = & git ls-remote --heads $ForkUrl 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Phase5Step '   [PLACEHOLDER] Fork not reachable yet — create/publish KooshaPari/Compound-Spheres-3D first.'
        Write-Phase5Step "   git said: $($lsRemote -join '; ')"
    } else {
        Write-Phase5Step '   [OK] Fork remote is reachable; clone command above should work.'
    }
} else {
    Write-Phase5Step ''
    Write-Phase5Step '   [WARN] git not on PATH — skip remote check.'
}

Write-Phase5Step ''
Write-Phase5Step '3. Optional: add as WorldSphereMod git submodule (run manually from repo root)'
Write-Phase5Step '   Commands are commented in this script; uncomment when the fork is live:'
Write-Phase5Step ''
Write-Phase5Step '   # cd <WorldSphereMod repo root>'
Write-Phase5Step '   # git status   # working tree must be clean'
Write-Phase5Step "   # git submodule add $ForkUrl $SubmodulePath"
Write-Phase5Step "   # cd $SubmodulePath"
Write-Phase5Step "   # git checkout $UpstreamPinExample   # pin to upstream-equivalent SHA — see phase5-prep.md"
Write-Phase5Step '   # cd ../..'
Write-Phase5Step '   # git add .gitmodules External/Compound-Spheres-3D'
Write-Phase5Step '   # git commit -m "chore(external): add Compound-Spheres-3D submodule (fork pin)"'
Write-Phase5Step ''
Write-Phase5Step '   After submodule add, contributors clone with:'
Write-Phase5Step '     git clone --recurse-submodules <WorldSphereMod-url>'
Write-Phase5Step "     # or: git submodule update --init --recursive $SubmodulePath"
Write-Phase5Step ''
Write-Phase5Step '4. Unity 2022.3 smoke build + DLL parity — see phase5-prep.md § DLL swap checklist'
Write-Phase5Step '   CLI: wsm3d submodule init   (existing External/Compound-Spheres only today)'
Write-Phase5Step ''

# --- Optional git submodule add (commented; enable when fork is published) ---
# cd $RepoRoot
# git status
# git submodule add $ForkUrl $SubmodulePath
# Push-Location (Join-Path $RepoRoot $SubmodulePath)
# try {
#     git checkout $UpstreamPinExample
# } finally {
#     Pop-Location
# }
# git add .gitmodules $SubmodulePath
# git commit -m "chore(external): add Compound-Spheres-3D submodule (fork pin)"
