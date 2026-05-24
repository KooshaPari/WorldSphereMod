# Assets — `diagnose-perf`

Captures for the [Diagnose performance](../../diagnose-perf.md) journey.
All PNGs 1280 × 720, each under 2 MB.

| Filename                | Step | What it shows |
|-------------------------|------|---------------|
| `01-profiler-flag.png`  | 1    | In-game settings panel, **WorldSphere** tab, with the `ProfilerDump` toggle highlighted (red box) flipped to ON. |
| `02-stats-overlay.png`  | 2    | In-game view with the `RuntimeStatsOverlay` text drawn in the top-left corner — visible frame time, actor / building / tree counts. Background can be any world. |
| `03-prof-line.png`      | 3    | Player.log open in a tail viewer (`Get-Content -Wait`, `tail -f`, or NeoModLoader's log viewer). Filter applied so only `[WSM-PROF]` lines are showing; at least 5–10 lines visible. |
| `04-unity-profiler.png` | "GC spikes" variant | Unity Profiler window attached to a running WorldBox process, CPU Usage track in focus, allocations highlighted in the WorldSphere modules. |
