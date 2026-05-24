# WorldSphereMod3D MCP Server

FastMCP (Python 3.11+) server for WorldSphereMod3D game automation and deployment.

## Install

### Via uv (Recommended)

```bash
cd Tools/wsm3d-mcp
uv pip install -e .
```

### Via pip

```bash
cd Tools/wsm3d-mcp
pip install -e .
```

## Run

### HTTP/SSE Mode (Persistent)

```bash
# Auto-detects available port; defaults to 127.0.0.1:8766
python -m wsm3d_mcp.server --http

# Custom port
WSM3D_MCP_PORT=9000 python -m wsm3d_mcp.server --http
```

Health check: `curl http://127.0.0.1:8766/health`

### Stdio Mode (Claude Desktop)

```bash
python -m wsm3d_mcp.server
```

## Claude Integration

The MCP server is automatically registered in `.claude/mcp-servers.json` (project-local config).

### First-Time Setup

```bash
# 1. Clone the repo (if not already)
git clone https://github.com/KooshaPari/WorldSphereMod.git

# 2. Install MCP server in development mode
pip install -e Tools/wsm3d-mcp

# 3. Restart Claude Code
# The MCP server will auto-connect via stdio mode (stdio) or HTTP (wsm3d-http)
```

### Manual Config (if needed)

The config at `.claude/mcp-servers.json` has two entries:
- **`wsm3d`** (stdio): `python -m wsm3d_mcp.server` — auto-connect on Claude startup
- **`wsm3d-http`** (HTTP): `http://127.0.0.1:8766` — use when the server is pre-started in HTTP mode

## Tools

### Game Control

- `game_launch()` → {ok, pid}
- `game_kill()` → {ok}
- `game_status()` → {running, pid, ram_mb, log_size, log_mtime}
- `game_screenshot(out_path=None)` → {ok, path, width, height}
- `game_send_key(key, repeat=1)` → {ok}

### Log Analysis

- `log_tail(n=50, grep=None)` → {lines: list[str]}
- `log_grep(pattern, head=50)` → {matches: list[{line_no, line}]}
- `log_clear_buffer()` → {ok, lines_removed}

### Settings

- `settings_get(key=None)` → {settings: dict | value}
- `settings_set(key, value)` → {ok, old, new}
- `settings_toggle(phase)` → {ok, old, new}

### Build & Deploy

- `mod_build(configuration="Release")` → {ok, warnings, time_s, log_tail}
- `mod_install(launch=False, no_build=False)` → {ok, installed_to}
- `mod_relaunch(no_build=False)` → {ok}

### Phenotype Journeys

- `journey_verify(ref, live=False)` → {ok, mode, score, violations: list[str]}
- `journey_list()` → deprecated wrapper-local alias that lists local manifest IDs only
- `journey_run(id)` → deprecated wrapper-local alias that performs mock verification only

Examples:

```powershell
wsm3d journey verify -Id smoke-test-phase1
wsm3d journey verify .\docs\journeys\manifests\us-wsm-phase-1-voxel-actors\manifest.json -Live
```

### Codex integration

- `codex_exec(prompt, model='gpt-5.3-codex-spark', reasoning='medium', workdir=None, extra_dirs=[], timeout_s=300)` → {ok, stdout, stderr, exit_code, tokens_used}
- `codex_doctor()` → {ok, stdout, stderr, exit_code, tokens_used}
- `codex_models()` → ['gpt-5.3-codex-spark', 'gpt-5.4-mini', 'gpt-5.4-mini-codex', 'o3']

### System

- `status()` → {build_state, install_state, game_state, log_state, last_commit_sha}

## Hard Paths

- Game: `C:/Program Files (x86)/Steam/steamapps/common/worldbox/`
- Mod install: `{game}/Mods/WorldSphereMod3D/`
- Player log: `C:/Users/koosh/AppData/LocalLow/mkarpenko/WorldBox/Player.log`
- SavedSettings: `{appdata}/WorldSphereMod.json` (auto-discovered)
- CLI: `Tools/wsm3d.ps1`

## Debug

Set `WSM3D_MCP_DEBUG=1` for verbose logging:

```bash
WSM3D_MCP_DEBUG=1 python -m wsm3d_mcp.server --http
```

## Architecture

- `paths.py` — Path constants and discovery
- `tools/game.py` — Game process management, screenshots
- `tools/log.py` — Player.log I/O
- `tools/settings.py` — SavedSettings.json I/O
- `tools/build.py` — Build/install (wraps wsm3d.ps1)
- `tools/journey.py` — phenotype-journey wrapper
- `tools/codex.py` — Codex CLI bridge tools
- `server.py` — FastMCP server entrypoint

All shell-outs via `subprocess.run(["pwsh", ...])` for consistency with legacy CLI.

---

**Total LOC**: ~630 (target: 600–900)
