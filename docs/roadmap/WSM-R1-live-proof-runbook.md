# WSM R1 Live-Proof Readiness Runbook

Status: R1 is `in_progress`; R2 is `blocked_live_runtime`. Do not mark either complete from this runbook alone.

## Scope

This is a retry checklist for producing live startup proof, `Player.log` proof, screenshot/hash proof, and gate evidence without editing source, JSON, or verifier scripts.

## Inputs

- Repo: `E:\Dev\WorldSphereMod`
- Verifier entrypoint: `Tools\l1-verify.ps1`
- Pixel verifier: `Tools\pixel-verify.py`
- Prior scratch output: `docs\journeys\scratch\`
- WorldBox install path: pass explicitly as `$WorldBoxPath` if auto-detection is unreliable.

## R1 Commands

Run from PowerShell:

```powershell
cd E:\Dev\WorldSphereMod
git status --short
dotnet build .\WorldSphereMod.csproj -c Release
.\Tools\install.ps1
```

If the game path is not detected:

```powershell
.\Tools\install.ps1 -WorldBoxPath "C:\Program Files (x86)\Steam\steamapps\common\worldbox"
```

Start WorldBox manually after install, wait for NeoModLoader to finish Roslyn-compiling `Code\*.cs`, then capture live proof. Use `-SkipLaunch` here: without it, `Tools\l1-verify.ps1` may kill, install, and launch WorldBox itself.

```powershell
cd E:\Dev\WorldSphereMod
.\Tools\l1-verify.ps1 -SkipLaunch -OutDir .\docs\journeys\scratch\l1-verify-live
python .\Tools\pixel-verify.py check actor_silhouette_complexity --png .\docs\journeys\scratch\l1-verify-live\P0-1\default.png
python .\Tools\pixel-verify.py check actor_silhouette_complexity --png .\docs\journeys\scratch\l1-verify-live\P0-1\frame2.png
python .\Tools\pixel-verify.py check biome_color_variance --png .\docs\journeys\scratch\l1-verify-live\P0-7\terrain.png
```

## 2026-06-21 Worker 5 `/health` Refused Receipt

Current live bridge probe:

```powershell
Invoke-RestMethod -Uri http://127.0.0.1:8766/health -TimeoutSec 5
```

Observed result: connection actively refused on `127.0.0.1:8766`. That proves no bridge listener accepted the request at that endpoint during this check; it does not prove a mod startup failure by itself.

Non-destructive next steps before any live-proof claim:

1. Verify the installed mod path without changing runtime code:

```powershell
Test-Path "C:\Program Files (x86)\Steam\steamapps\common\worldbox\worldbox_Data\StreamingAssets\Mods\WorldSphereMod\Code"
Test-Path "C:\Program Files (x86)\Steam\steamapps\common\worldbox\worldbox_Data\StreamingAssets\Mods\WorldSphereMod\mod.json"
```

2. If either path is missing, record the current install inventory before running any install command:

```powershell
$mods = "C:\Program Files (x86)\Steam\steamapps\common\worldbox\worldbox_Data\StreamingAssets\Mods"
Get-ChildItem $mods -Force | Select-Object Name,FullName,LastWriteTime
Test-Path "$mods\WorldSphereMod\Code"
Test-Path "$mods\WorldSphereMod\mod.json"
Test-Path "$mods\WorldSphereMod3D\Code"
Test-Path "$mods\WorldSphereMod3D\mod.json"
```

3. If the expected files are still missing, close the stale WorldBox process and run the documented install command:

```powershell
cd E:\Dev\WorldSphereMod
.\Tools\install.ps1 -WorldBoxPath "C:\Program Files (x86)\Steam\steamapps\common\worldbox"
```

Expected post-install shape before launch:

```text
C:\Program Files (x86)\Steam\steamapps\common\worldbox\worldbox_Data\StreamingAssets\Mods\WorldSphereMod\Code
C:\Program Files (x86)\Steam\steamapps\common\worldbox\worldbox_Data\StreamingAssets\Mods\WorldSphereMod\mod.json
```

If the repo/install script intentionally emits `WorldSphereMod3D` instead, record that path explicitly and do not mix the two layouts in proof receipts.

4. Launch WorldBox manually, enable the WSM3D fork in NeoModLoader if needed, and wait for NML Roslyn compile/init to settle.

5. Re-run the bridge probe:

```powershell
Invoke-RestMethod -Uri http://127.0.0.1:8766/health -TimeoutSec 5
```

6. If `/health` is still refused, copy and inspect the active `Player.log` before rerunning automation:

```powershell
$log = Join-Path $env:USERPROFILE "AppData\LocalLow\mkarpenko\WorldBox\Player.log"
Copy-Item $log .\docs\journeys\scratch\Player-live-proof.log -Force
Select-String -Path .\docs\journeys\scratch\Player-live-proof.log -Pattern "WorldSphere|WORLDSPHERE|NML|NeoModLoader|Bridge|8766|Roslyn|Exception|error"
```

7. Only after `/health` returns JSON should `Tools\l1-verify.ps1 -SkipLaunch` be used for live evidence. If the bridge remains refused, keep R1 `in_progress` and R2 `blocked_live_runtime`.

## 2026-06-21 Worker 4 Recovery Diagnostic

Latest diagnostic receipt: `docs\roadmap\artifacts\2026-06-21-WSM-R1-live-attempt.md`

Observed state:

- 2026-06-21T17:08:08.3721309-07:00: `worldbox` process was present as PID `156176`, started `6/18/2026 4:55:16 AM`, from `C:\Program Files (x86)\Steam\steamapps\common\worldbox\worldbox.exe`.
- 2026-06-21T17:08:08.7413949-07:00: `Invoke-RestMethod -Uri http://127.0.0.1:8766/health -TimeoutSec 5` was actively refused on `127.0.0.1:8766`.
- 2026-06-21T17:08:08.2674924-07:00: `Player.log` was still stale at `LastWriteTimeUtc: 6/18/2026 11:56:02 AM`, length `1779`.
- 2026-06-21T17:23:36.9186379-07:00: both checked installed mod layouts were missing `Code` and `mod.json` under `WorldSphereMod` and `WorldSphereMod3D`.

Interpretation: the refused bridge is currently consistent with a stale WorldBox process and missing installed WSM mod files at the checked install path. This remains a live-runtime blocker, not a source-code failure claim.

Next non-destructive recovery step: close the stale WorldBox process, run the documented install command with explicit `-WorldBoxPath "C:\Program Files (x86)\Steam\steamapps\common\worldbox"` from a clean shell, manually launch WorldBox, and re-probe `/health`. Do not run `Tools\l1-verify.ps1 -SkipLaunch` until `/health` returns JSON.

Collect the active Unity log:

```powershell
$log = Join-Path $env:USERPROFILE "AppData\LocalLow\mkarpenko\WorldBox\Player.log"
Copy-Item $log .\docs\journeys\scratch\Player-live-proof.log -Force
Select-String -Path .\docs\journeys\scratch\Player-live-proof.log -Pattern "WorldSphere|WORLDSPHERE|NML|NeoModLoader|water-flat-sealevel|LOD-POLICY|MeshWater|isWorld3D"
```

Optional verifier self-checks, if the local test runner supports them:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\Tools\l1-verify.tests.ps1
python .\Tools\pixel-verify.tests.py
```

Non-live readiness checks that are safe before launching WorldBox:

```powershell
cd E:\Dev\WorldSphereMod
dotnet build .\WorldSphereMod.csproj -c Release
pwsh -NoProfile -Command '$errs=$null; [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path "Tools/install.ps1"), [ref]$null, [ref]$errs) > $null; if ($errs) { $errs | ForEach-Object Message; exit 1 }'
pwsh -NoProfile -Command '$errs=$null; [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path "Tools/l1-verify.ps1"), [ref]$null, [ref]$errs) > $null; if ($errs) { $errs | ForEach-Object Message; exit 1 }'
python -m py_compile .\Tools\pixel-verify.py .\Tools\pixel-verify.tests.py
pwsh -NoProfile -ExecutionPolicy Bypass -File .\Tools\l1-verify.tests.ps1
python .\Tools\pixel-verify.tests.py
```

## Expected Artifacts

The R1 retry should leave these files under the selected live output directory, for example `docs\journeys\scratch\l1-verify-live\`:

- `report.json`
- `P0-1\default.png`
- `P0-1\frame2.png`
- `P0-2\trees.png`
- `P0-3\flash1.png`
- `P0-3\flash2.png`
- `P0-5\brush-close.png`
- `P0-5b\brush-out.png`
- `P0-6\menu.png`
- `P0-7\terrain.png`
- `P0-8\water.png`
- `Player-live-proof.log`

## R1 Pass/Fail Criteria

R1 is live-proof ready only when all are true:

- `dotnet build .\WorldSphereMod.csproj -c Release` exits `0`.
- `install.ps1` reports the mod installed to the expected `WorldSphereMod3D` folder and does not leave a stale self DLL in installed `Assemblies`.
- WorldBox starts with NeoModLoader compile/init evidence in `Player-live-proof.log`.
- `report.json` is regenerated from the current live run.
- The report includes bridge health evidence and `isWorld3D=true`.
- The report includes screenshot paths for default, frame2, zoomout, and menu captures.
- Pixel verification passes for required screenshots or records an explicit failure reason.
- `Player-live-proof.log` contains WSM/NML startup proof and no duplicate-mod or Roslyn compile failure.

R1 fails or remains `in_progress` if any live artifact is missing, the bridge is unreachable, screenshots are stale, `Player.log` cannot prove the current startup, or the verifier reports a blocker without a captured reason.

## Known Non-Green State From Prior L1 Report

Prior `docs\journeys\scratch\l1-verify-report.json` showed:

- Build succeeded: `0` errors, `13` warnings.
- Test state was non-green: `527` passed, `13` failed, `3` skipped out of `543`.
- Live telemetry was mixed: median frame time `18.76ms`, p95 `34.24ms`, bridge timeout rate `19/60`.
- P0 blocker remained: `HEIGHTFIELD_BLOCKING`, with `DrawTiles HEIGHTFIELD SLOW` events at roughly `5-7s` per call.
- Additional blockers recorded: `BRIDGE_FLAKY`, `FALLBACK_PATH`, `NO_INSTANCING`, and `UI_SCREENSHOT_FAIL`.

Do not treat this prior report as a current pass. It is only baseline context for the retry.

## R2 Unlock Conditions

R2 stays `blocked_live_runtime` until R1 has fresh live evidence and these conditions are met:

- Fresh `Player-live-proof.log` proves current WorldBox startup, NML compile/init, and WSM feature banners.
- Fresh `report.json` and screenshots are generated in the same run.
- Screenshot pixel/hash verification passes or has reviewed, documented failure output.
- `HEIGHTFIELD_BLOCKING` is resolved or explicitly accepted as an R2 carry-forward risk by the owner.
- Bridge polling is stable enough for the verifier to complete without losing required samples.
- Any non-green tests are listed as known state, with no new source changes hidden behind the live-proof run.

Only after those gates are satisfied should R2 move out of `blocked_live_runtime`; this runbook does not make that status change.
