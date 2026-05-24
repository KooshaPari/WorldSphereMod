# wsm-log-parser

Rust CLI for extracting `[WSM3D]` events from `Player.log`.

- Parses init profiler timings and renders a duration-sorted table.
- Extracts phase-toggle output lines.
- Tails log output (including `stdin`) for `[WSM3D]` lines.

## Build

```powershell
cargo build --release
```

## Commands

```powershell
wsm-log-parser init-profile <path-to-log>
```

Extracts lines like:

```
[WSM3D] InitProfiler {label} = {duration}{unit}
```

Prints a table sorted by duration descending.

```powershell
wsm-log-parser phase-toggles <path-to-log>
```

Extracts lines containing:

- `[WSM3D] Voxel material resolved`
- `[WSM3D] Phase tagging output`

```powershell
wsm-log-parser tail <path-to-log> --filter "<regex>"
wsm-log-parser tail --filter "<regex>"
wsm-log-parser tail <path-to-log>
wsm-log-parser tail
```

`tail` prints matching `[WSM3D]` lines only; when a log path is provided it follows the file (like a pure-Rust tail/follow mode). When no path is provided, it tails stdin.
