# Tooling reference

The WorldSphereMod3D fork ships with an integrated CLI, MCP server, and Claude Code skill that let you build, install, run, and validate WSM3D entirely from the terminal or your AI assistant without touching the GUI.

## Overview

**`wsm3d`** is a cross-platform CLI tool that automates the dev loop: compile, install fresh binaries to your WorldBox mod folder, launch the game, capture screenshots, run journey manifests, and toggle feature flags. It's paired with a **language-model-server** (MCP) that exposes the same operations as callable tools inside Claude Code, and a **Claude skill** that routes complex multi-step tasks (journeys) through an AI agent. Together, they collapse repetitive manual steps and let you stay in your development environment.

## CLI (`wsm3d`)

The command-line interface wraps PowerShell/Bash build tasks, file copies, and game lifecycle control. Install via:

```powershell
# From repo root
dotnet tool install --global --add-source ./Tools/wsm3d-cli-nupkg ./Tools/wsm3d-cli-nupkg/wsm3d.*.nupkg
```

| Command | Args | Effect |
|---|---|---|
| `build` | `[-c Release\|Debug]` | Rebuild `WorldSphereMod.csproj` (C#). Logs to stdout. |
| `install` | `[-Force]` | Copy compiled DLL + Assets to WorldBox Mods folder. Detects Steam path; override via `$env:WORLDBOX_PATH`. |
| `launch` | | Spawn `worldbox.exe` (or `worldbox` on Linux). Non-blocking. |
| `kill` | | Terminate all `worldbox` processes. |
| `relaunch` | | Kill, wait 2s, launch. |
| `log` | `[-Follow]` | Tail the mod's `persistent.log` from `persistentDataPath`. Use `-Follow` for live stream. |
| `screenshot` | `[-Output <path>]` | Send RPC to running game; saves PNG to WorldBox's screenshot folder or specified path. |
| `settings get` | `<key>` | Read a SavedSettings flag (e.g., `Is3D`, `VoxelActors`). Prints JSON value. |
| `settings set` | `<key> <value>` | Write a SavedSettings flag. Accepts bool/int/float strings. |
| `toggle` | `<key>` | Flip a bool SavedSettings flag. Useful in hot-reload loops. |
| `status` | | Print game PID, mod folder path, last build time. |
| `journey verify` | `-Id <id>` or `<manifest-path>` `[-Live]` | Resolve a manifest by ID or path and verify it with `phenotype-journey verify <manifest> --mock` by default. |
| `watch` | `[-Dir <path>]` | Poll `Code/` folder (or `-Dir` path) for changes; auto-rebuild + reinstall + reload on save. Debounce 1s. |
| `help` | `[<command>]` | Print command reference. |

### Example invocations

```powershell
# Fresh build + install + launch
wsm3d build && wsm3d install && wsm3d launch

# One-shot smoke test: build → install → launch → screenshot
wsm3d build && wsm3d install && wsm3d launch && Start-Sleep -Seconds 15 && wsm3d screenshot -Output .\smoke.png

# Toggle Phase 1 at runtime
wsm3d toggle VoxelActors

# Watch for edits and hot-reload
wsm3d watch -Dir .\WorldSphereMod\Code

# Verify a journey manifest in mock mode
wsm3d journey verify -Id smoke-test-phase1

# Stream the game log
wsm3d log -Follow
```

## MCP server (`wsm3d-mcp`)

A Model Context Protocol (MCP) server that exposes `wsm3d` operations as callable tools inside Claude Code or any MCP client. Install and register:

```powershell
# Install as development package
pip install -e Tools/wsm3d-mcp

# Register with Claude Code in ~/.claude/mcp-servers.json
{
  "servers": {
    "wsm3d-mcp": {
      "command": "python",
      "args": ["-m", "wsm3d_mcp.server"],
      "env": {
        "WORLDBOX_PATH": "C:/Program Files (x86)/Steam/steamapps/common/worldbox"
      }
    }
  }
}
```

The server listens on `stdio` (Claude Code integration) or HTTP port `8766` (remote clients). It exposes 18 tools:

| Tool | Arguments | Returns |
|---|---|---|
| `wsm3d_build` | `config: "Release" \| "Debug"` | Build log (stdout + stderr) |
| `wsm3d_install` | `force: bool` | Install summary (files copied, errors if any) |
| `wsm3d_launch` | | Game PID |
| `wsm3d_kill` | | Kill count (0 if no process) |
| `wsm3d_relaunch` | | Game PID after relaunch |
| `wsm3d_log` | `follow: bool`, `lines: int` | Last N log lines, or tail stream |
| `wsm3d_screenshot` | `output_path: string \| null` | Screenshot file path |
| `wsm3d_settings_get` | `key: string` | JSON value (bool/int/float/string) |
| `wsm3d_settings_set` | `key: string`, `value: string` | Confirmation or error |
| `wsm3d_toggle` | `key: string` | New bool value |
| `wsm3d_status` | | JSON: {pid, mod_folder, last_build, uptime_seconds} |
| `wsm3d_journey_list` | | Deprecated wrapper-local alias that returns local manifest IDs only |
| `wsm3d_journey_run` | `id: string` | Deprecated wrapper-local alias that performs mock verification only |
| `wsm3d_journey_verify` | `ref: string`, `live: bool \| null` | Assertion results from `phenotype-journey verify` |
| `wsm3d_watch` | `dir: string \| null` | Watch event stream (file changed → rebuild → reinstall event) |
| `wsm3d_get_worldbox_path` | | Detected or env-configured WorldBox path |
| `wsm3d_get_version` | | CLI + server version string |
| `wsm3d_poll_game_ready` | `timeout_seconds: int` | True if game is loaded and responsive |

### Using the MCP server in Claude Code

Once registered, you can call tools directly:

```
/claude ask "Build and screenshot the current world. Assert that a voxel actor appears on screen."
```

The Skill tool (below) routes this to the MCP server automatically.

## Slash commands (`/wsm-*`)

Ten Claude Code slash commands that wrap the most common workflows. Each is a convenience alias to the MCP server + Skill layer.

| Command | Alias for | When to use |
|---|---|---|
| `/wsm-build` | `wsm3d build -c Release` | Rebuild the mod (C#). |
| `/wsm-install` | `wsm3d install` | Copy binaries to WorldBox Mods. |
| `/wsm-launch` | `wsm3d launch` | Spawn WorldBox. |
| `/wsm-kill` | `wsm3d kill` | Terminate WorldBox (clean exit). |
| `/wsm-log` | `wsm3d log -Follow` | Stream mod log in real-time. |
| `/wsm-screenshot` | `wsm3d screenshot` | Capture screen; auto-timestamps. |
| `/wsm-status` | `wsm3d status` | Check build time, game PID, mod folder. |
| `/wsm-toggle` | Prompt for flag, then `wsm3d toggle <flag>` | Runtime feature toggle. |
| `/wsm-journey` | Prompt for manifest ID, then `wsm3d journey verify` | Verify a test manifest in mock mode. |
| `/wsm-watch` | `wsm3d watch` | Auto-rebuild on save. |

Type `/wsm-` and hit Tab to see all available commands in Claude Code.

## Skill: `wsm3d-skill`

A Claude Code skill that auto-routes at the top of longer multi-step tasks. Reading:

> A multi-step automation skill for WorldSphereMod3D development. Handles complex workflows that combine build, install, screenshot, assertion, and toggle operations. Reads journey manifests, executes OCR-based assertions on screenshots, and adapts the next step based on results. Best used when you ask a question like "Run the Phase 1 smoke test" or "Toggle Phase 2, rebuild, screenshot, and verify the buildings render."

The skill is invoked automatically when your prompt mentions a **phase**, **journey**, **toggle**, or **screenshot** operation and the system detects a need for sequential operations. You don't need to invoke it explicitly; just write your request naturally.

## Phenotype journeys and manifests

A **journey manifest** is a JSON file under `docs/journeys/manifests/<id>/manifest.json` that describes a test scenario: spawn a world with a seed, take a screenshot, assert pixel patterns on screen, and report a pass/fail.

### Capturing a screenshot journey

```powershell
# 1. Launch the game with desired settings
wsm3d launch
# 2. Wait for load, set up a scene
Start-Sleep -Seconds 20
# 3. Capture a screenshot
wsm3d screenshot -Output .\my-scene.png
# 4. Create or edit a manifest
@"
name: smoke-test-my-phase
seed: 12345
steps:
  - action: wait_game_ready
    timeout_seconds: 30
  - action: screenshot
  - action: ocr_assert
    assertions:
      - type: must_contain
        text: "Voxel"
        confidence: 0.8
"@ | Set-Content .\docs\journeys\manifests\smoke-test-my-phase\manifest.json
# 5. Run it
wsm3d journey verify -Id smoke-test-my-phase
```

### OCR assertion DSL

Each `ocr_assert` step in a manifest can contain multiple assertions. Each assertion has:

| Field | Type | Meaning |
|---|---|---|
| `type` | `must_contain` \| `must_not_contain` \| `regex` \| `expected_exit` | Detection mode |
| `text` / `pattern` | string | The substring or regex pattern to match |
| `confidence` | float (0.0–1.0) | OCR confidence threshold (0.8 = 80% sure) |
| `region` | `[x, y, width, height]` | Optional: restrict search to screen rectangle (pixels) |

Example assertion block:

```yaml
assertions:
  - type: must_contain
    text: "Phase 1"
    confidence: 0.85
  - type: must_not_contain
    text: "ERROR"
    confidence: 0.9
  - type: regex
    pattern: "FPS: [0-9]+"
    confidence: 0.8
  - type: expected_exit
    code: 0
```

See the manifest files under `docs/journeys/manifests/` for the full structure and current field shape.

## Hot-reload watch

```powershell
wsm3d watch
# or with custom directory
wsm3d watch -Dir .\WorldSphereMod\Code
```

Polls the `Code/` folder (or `-Dir` path) every 500 ms for file changes. On a change:

1. Wait 1 second for the write to complete (debounce).
2. Run `wsm3d build -c Release`.
3. Run `wsm3d install -Force` (overwrite existing DLLs).
4. Log the rebuild + reinstall time to stdout.
5. Loop.

Useful during rapid iteration: save a `.cs` file, watch automatically rebuilds + reinstalls, then you manually reload in the game or use `/wsm-relaunch`.

::: tip
Watch leaves the game running; you manage the reload. Pair it with `/wsm-relaunch` or toggle a flag with `/wsm-toggle` to test your changes.
:::

## Day in the life: a 6-step workflow

Here's a concrete example of using the tooling to iterate on Phase 1 voxel rendering:

```powershell
# 1. Edit the SpriteVoxelizer.cs file (add a new mesh optimization)
# 2. Save the file
# 3. The watcher auto-detects the change:
wsm3d watch
# (rebuild + reinstall in background)

# 4. Relaunch the game to load the new DLL
wsm3d relaunch

# 5. Wait 15 seconds for the game to load
Start-Sleep -Seconds 15

# 6. Take a screenshot and verify visually
wsm3d screenshot -Output .\check.png
# Now open check.png and inspect the voxel mesh visually
```

Or, automate the assertion:

```powershell
# Build, install, launch, wait, screenshot, and verify with a journey
wsm3d build && wsm3d install && wsm3d launch
Start-Sleep -Seconds 15
wsm3d journey verify -Id smoke-test-phase1
# Exit code 0 = all assertions passed
```

::: warning
Use `journey verify` for both mock and live verification. Mock mode is the default; pass `-Live` to map to `phenotype-journey verify --live`.
:::

## See also

- **[PLAN.md](/PLAN)** — 10-phase roadmap with file-level granularity
- **[CLAUDE.md](https://github.com/KooshaPari/WorldSphereMod/blob/main/CLAUDE.md)** — Cold-start for a new agent session
- **[Contributing guide](/CONTRIBUTING)** — PR process and code style
