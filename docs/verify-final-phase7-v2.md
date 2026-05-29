# Final cumulative perf verification

Scope: final verification after the LRU cache-spike fix + building budget.

## Requested git history

`git log --oneline -6`

```text
657310e fix: cache procgen building render work
ea2393d perf(voxel): replace periodic tile-height cache full-clear with bounded LRU
3d58844 fix: guard procedural skybox cubemap probe
63e2f19 fix(lighting): stop per-frame _MainTex error on vanilla Skybox/Procedural
9a9405c fix: guard skybox texture lookup
36dbefc perf(voxel): stop clearing tile-height memo every frame in ActorVoxelEmit
```

`git log --oneline -3 -- WorldSphereMod/Code/ProcGen/BuildingProcRender.cs`

```text
657310e fix: cache procgen building render work
4efa128 WorldSphereMod3D: automation, PlayCUA gates, and live-verify harness (#7)
5ec46a9 fix: stabilize render/voxel pipeline and expand test/CI coverage
```

## Live verification

- WorldBox was stopped and relaunched from `C:/Program Files (x86)/Steam/steamapps/common/worldbox/worldbox.exe`.
- `/health` became `{ok:true}` on poll 10/30.
- `/health` reported `isWorld3D:true` on poll 8 of the second gate.
- Spawn test succeeded: `POST /actions/spawn_units?count=30&race=human`.

## Telemetry

12 telemetry samples, 6s apart:

```text
telemetry 1/12 frameMs=20 median=20
telemetry 2/12 frameMs=20 median=20
telemetry 3/12 frameMs=20 median=20
telemetry 4/12 frameMs=20 median=20
telemetry 5/12 frameMs=20 median=20
telemetry 6/12 frameMs=20 median=20
telemetry 7/12 frameMs=20 median=20
telemetry 8/12 frameMs=20 median=20
telemetry 9/12 frameMs=20 median=20
telemetry 10/12 frameMs=20 median=20
telemetry 11/12 frameMs=20 median=20
telemetry 12/12 frameMs=20 median=20
```

Median `frameMs`: `20`

## PERF log breakdown

Last available `"[WSM3D][PERF] render3DStuff SLOW"` lines from `Player.log`:

```text
[WSM3D][PERF] render3DStuff SLOW: 297ms (sprite=251ms precalc=13ms redraw=25ms debug=8ms timer=0ms refresh=0ms)
[WSM3D][PERF] render3DStuff SLOW: 237ms (sprite=16ms precalc=214ms redraw=0ms debug=0ms timer=7ms refresh=7ms)
[WSM3D][PERF] render3DStuff SLOW: 75ms (sprite=1ms precalc=74ms redraw=0ms debug=0ms timer=0ms refresh=0ms)
[WSM3D][PERF] render3DStuff SLOW: 27ms (sprite=1ms precalc=26ms redraw=0ms debug=0ms timer=0ms refresh=0ms)
[WSM3D][PERF] render3DStuff SLOW: 28ms (sprite=0ms precalc=27ms redraw=0ms debug=0ms timer=1ms refresh=1ms)
```

Dominant PERF phase: `precalc`

## Log counters

- `texture property`: `0`
- `isReadable is false`: `0`
- `ManagedStream`: `0`
- `Crash!!!`: `0`

## Render stats

`GET /diag/render_stats` returned `200` with body `null`, so these fields were not exposed at capture time:

- `visibleUnits`
- `visibleBuildings`
- `lastNonZeroDrawCalls`

## Result

PASS criteria:

- `frameMs median < 150ms`: pass (`20`)
- no crash: pass
- `precalc < 70ms`: fail, because one captured slow sample still reports `precalc=214ms`

Headline: `frameMs median=20ms`, dominant PERF phase=`precalc`

Overall verdict: `FAIL` on the strict perf gate, despite the low frame median and no crash.
