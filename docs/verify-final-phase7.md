# Phase 7 Verification Report

HEADLINE: frameMs median 453.09 ms; texture-property-error count 7

Status: FAIL

## Procedure

1. Stopped `worldbox.exe`.
2. Started `C:/Program Files (x86)/Steam/steamapps/common/worldbox/worldbox.exe`.
3. Polled `http://127.0.0.1:8766/health` every 10 seconds until `{ok:true}`.
4. Polled until `isWorld3D:true`.
5. Posted to `http://127.0.0.1:8766/actions/spawn_units?count=30&race=human` with empty body and `Content-Length: 0`.
6. Waited 45 seconds.
7. Sampled `/telemetry` 12 times at 6-second intervals.
8. Grepped `Player.log` for the requested error patterns and shader/perf markers.
9. Read `http://127.0.0.1:8766/diag/render_stats`.

## Telemetry

frameMs samples:
- 886.847168
- 941.782166
- 921.7955
- 651.0544
- 231.9176
- 232.507385
- 235.0551
- 233.9942
- 251.301285
- 255.117
- 1161.29956
- 1166.19006

Median frameMs: 453.0857

## Log Counts

- `texture property`: 7
- `isReadable is false`: 0
- `DIAG-SUBMIT`: 5
- `ManagedStream`: 0
- `Crash!!!`: 0

## Log Markers

LoadedShaders line:

`[WSM3D] PostStack shader 'BrpACES' not found in any resolution path (LoadedShaders[count=1,hasBundleCache=True], Shader.Find('WSM3D/BrpACES'), Resources.Load('Shaders/BrpACES'), Shader.Find('Hidden/WSM3D/BrpACES')). PostFX shaders are unavailable — Unity cannot runtime-compile .shader source files outside an AssetBundle.`

Last 3 `render3DStuff SLOW` lines:

`[WSM3D][PERF] render3DStuff SLOW: 125ms (sprite=0ms precalc=124ms redraw=0ms debug=0ms timer=1ms refresh=1ms)`

`[WSM3D][PERF] render3DStuff SLOW: 366ms (sprite=0ms precalc=366ms redraw=0ms debug=0ms timer=0ms refresh=0ms)`

`[WSM3D][PERF] render3DStuff SLOW: 114ms (sprite=0ms precalc=114ms redraw=0ms debug=0ms timer=0ms refresh=0ms)`

## Render Stats

- visibleUnits: 11
- lastNonZeroDrawCalls: 36

## Verdict

FAIL. Median frame time is above the 150 ms threshold, and texture-property errors are not near zero.
