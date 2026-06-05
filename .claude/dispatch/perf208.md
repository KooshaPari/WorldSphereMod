WSM3D PERF P0 — task: bring idle-frame FPS from 6/4 to playable on a clean 3D world.

REPO: E:/Dev/WorldSphereMod  BRANCH: wip/208-height-fix  (do NOT create a worktree, edit in main dir only)
DANGER ZONE — STALE PROCESS = STALE RESULTS. Before ANY test:
  1) kill any worldbox.exe, also delete the NML CompiledMods/WORLDSPHERE3D_FORK.dll so NML recompiles.
  2) install your fresh DLL, then launch.

CONTEXT (from prior fixes; they did NOT fix the low FPS):
  - HeightField DrawTiles per-frame stall was previously a suspected culprit.
  - A "rebuild-storm debounce" was added; assume it's already in the tree.

YOUR JOB (4 ordered phases — STOP only when you have a green phase or a blocker):

PHASE A — find the root cause
  1) Build: `cd E:/Dev/WorldSphereMod && dotnet build -c Release -p:WarningLevel=2`. Must succeed.
  2) Install: `& E:/Dev/WorldSphereMod/install.ps1`  (canonical root only — StreamingAssets/Mods/WorldSphereMod).
  3) Launch: `& "C:/Program Files (x86)/Steam/steamapps/common/WorldBox/worldbox.exe"`. Wait ~25s for 3D world bootstrap; in this dev loop there is NO auto-load. The launch script `wsm3d.ps1 relaunch` is the canonical recipe if you need to use it.
  4) GET http://127.0.0.1:8766/health — confirm bridge up + isWorld3D=true. If 8766 is dead, the world isn't 3D yet — try a couple more seconds and check `Select-String -Path "$env:USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log" -Pattern "BridgeServer|listening on 127"` for the actual bound port (the listener tries 8765/8766/8767/8768 in order).
  5) Bridge has NO /perf endpoint. The actual perf sample is `GET /telemetry` which returns `{frameMs, voxelCacheHit, drawCalls, lastNonZeroDrawCalls, instances, actorDrawCumulative, actorFrontCount}`. Sample it 120 times with a 1s sleep between samples:
        for ($i=0; $i -lt 120; $i++) {
          $t = curl -s http://127.0.0.1:8766/telemetry | ConvertFrom-Json
          "$($i),$($t.frameMs),$($t.drawCalls),$($t.instances),$($t.actorFrontCount)"
          Start-Sleep 1
        }
     Compute median + p95 of frameMs. The user expects "frameMs" — Unity Time.unscaledDeltaTime*1000, so 1000/frameMs = FPS. 6/4 FPS ≈ 167/250 ms/frame. p95 > 80 ms is bad.

PHASE B — grep suspects (READ ONLY). Confirm or refute:
  - "HEIGHTFIELD SLOW" (any tagged diag string)
  - per-frame mesh rebuild loops in Voxel/, Foliage/, SpriteVoxelizer, Rig/, ProcGen/
  - any `UploadMeshData` / `SetVertices` / `SetIndices` called from Update or LateUpdate
  - any `MarkDynamic()` + `SetVertices` combo without dirty-flag gating
  - `BridgeServer.cs:RefreshTelemetryCache` `_cachedFrameMs = Time.unscaledDeltaTime * 1000f` — this is the source of frameMs; confirm.

PHASE C — minimal fix
  - Edit in main dir (no worktree). One commit. Smallest possible diff.
  - Bake a version string into the install + a banner log so we can prove fresh code (per memory: "prove fresh code via version string"). Use Core.savedSettings or a static const.
  - 2026-06-02 lesson: PERF lines in Player.log are per-step (`sw.Restart`), NOT cumulative. Don't read cumulative values that span frames.

PHASE D — re-verify
  - Kill worldbox, delete NML CompiledMods/WORLDSPHERE3D_FORK.dll, install, launch, re-sample 120 frames at /telemetry, compute median/p95, report before/after.

RETURN EXACTLY:
  - root cause file:line
  - frameMs BEFORE (median / p95 / min)
  - frameMs AFTER  (median / p95 / min)
  - commit hash (git -C E:/Dev/WorldSphereMod rev-parse HEAD)
  - confirmed-fresh-code banner log line you saw in Player.log
  - any blocker (e.g., cannot launch, shader error, NML skip)

ABSOLUTELY DO NOT:
  - do visual reads of screenshots (I hallucinate 3D into 2D — verify with numeric frameMs only).
  - touch WSM3D branch lineage git history (no rewrites, no force pushes).
  - commit on a different branch.
  - add new Phase toggles or new features.

REPORT TAGS: use ✅ confirmed / ~ measured / ✗ broken / → blocker (per memory).

Bypass flag (Windows): use `--dangerously-bypass-approvals-and-sandbox`. Model: `gpt-5.3-codex-spark` (medium). Code in this REPO directory only (no worktree). Stay terse.
