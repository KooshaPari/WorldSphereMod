---
description: Offline CI-equivalent validation — dotnet test + journey mock verify
---

# wsm-validate-all

Run the full **offline** programmatic gate before merge or release. Matches
`.github/workflows/live-verify-gate.yml` stages 1–2 and `task live-verify`.

## What This Does

1. **dotnet test** — unit, integration, and E2E projects under `tests/` (Release)
2. **Journey mock verify** — `Tools/verify-journeys.ps1` runs
   `phenotype-journey verify <manifest> --mock` for every manifest under
   `docs/journeys/manifests/` (all 10 phase journeys + any other manifests)
3. **Skips live stages** — bridge, PlayCUA, and SSIM require `-Live` (local only)
4. Writes `Tools/.reports/live-verify-latest.json` with per-stage status

This is the primary validation workflow before shipping a build.

## Steps

From the repo root (`C:/Users/koosh/Dev/WorldSphereMod`):

```pwsh
$wsmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $wsmRoot
try {
    & pwsh -NoProfile -File "$wsmRoot/Tools/wsm-live-verify.ps1"
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
```

Equivalent: `task live-verify` or `powershell -File Tools/wsm-live-verify.ps1`.

### Debug individual stages

```pwsh
# Stage 1 only
dotnet test tests/WorldSphereMod.Tests.Unit -c Release --nologo
dotnet test tests/WorldSphereMod.Tests.Integration -c Release --nologo
dotnet test tests/WorldSphereMod.Tests.E2E -c Release --nologo

# Stage 2 only (all manifests, mock)
pwsh -NoProfile -File Tools/verify-journeys.ps1

# Single phase journey (mock)
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-1-voxel-actors
```

### Live tier (optional, not part of validate-all)

```pwsh
pwsh Tools/wsm-live-verify.ps1 -Live              # bridge + PlayCUA + SSIM
pwsh Tools/wsm3d.ps1 journey verify -Id <id> -Live # live phenotype-journey
```

See `docs/live-verification.md`.

## Expected output (all pass)

```
dotnet test WorldSphereMod.Tests.Unit ...
dotnet test WorldSphereMod.Tests.Integration ...
dotnet test WorldSphereMod.Tests.E2E ...
OK .../docs/journeys/manifests/us-wsm-phase-1-voxel-actors/manifest.json
...
Verified 10 journey manifest(s).
Report: Tools/.reports/live-verify-latest.json
```

Report JSON includes `overallOk: true` and stages `dotnet-tests`,
`journey-mock-verify`, `live-playcua-ssim` (skipped).

## Expected output (failure)

```
dotnet test WorldSphereMod.Tests.Integration ...
# test failure output
Stage dotnet-tests failed.
Report: Tools/.reports/live-verify-latest.json
```

Or journey mock failure from `verify-journeys.ps1` / `phenotype-journey` with
non-zero exit; harness stops at stage 2 and sets `overallOk: false`.

Use this before merging to match CI `live-verify-gate` and nightly offline stages.
