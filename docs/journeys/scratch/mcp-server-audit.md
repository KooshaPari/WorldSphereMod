# WSM3D MCP Server Audit

## Current surface

`Tools/wsm3d-mcp/wsm3d_mcp/server.py` currently defines 21 MCP tools, not 18: `game_launch`, `game_kill`, `game_status`, `game_screenshot`, `game_send_key`, `log_tail`, `log_grep`, `log_clear_buffer`, `settings_get`, `settings_set`, `settings_toggle`, `mod_build`, `mod_install`, `mod_relaunch`, `journey_list`, `journey_run`, `journey_verify`, `codex_exec`, `codex_doctor`, `codex_models`, and `status` (`server.py:63-292`). The named tools the prompt called out do exist: `game_launch` (`server.py:63-66`), `log_grep` (`server.py:119-125`), `settings_toggle` (`server.py:162-169`), and `journey_run` (`server.py:216-222`).

The “18 tools” claim in repo docs is stale but still visible in `CLAUDE.md` and `docs/tooling.md`; the documented 18 are `wsm3d_build`, `wsm3d_install`, `wsm3d_launch`, `wsm3d_kill`, `wsm3d_relaunch`, `wsm3d_log`, `wsm3d_screenshot`, `wsm3d_settings_get`, `wsm3d_settings_set`, `wsm3d_toggle`, `wsm3d_status`, `wsm3d_journey_list`, `wsm3d_journey_run`, `wsm3d_journey_verify`, `wsm3d_watch`, `wsm3d_get_worldbox_path`, `wsm3d_get_version`, and `wsm3d_poll_game_ready` (`CLAUDE.md:63-66`, `docs/tooling.md:81-102`).

## Plate-19 bridge gaps

The proposed bridge RPC methods are not implemented in this MCP server. There is no `load_save`, no `capture_screenshot` bridge method, and no `query_telemetry` method anywhere under `Tools/wsm3d-mcp/` (`server.py:63-292`; `tools/game.py:65-224`; `tools/settings.py:10-95`; `tools/journey.py:11-92`; `tools/build.py:47-115`; `tools/codex.py:100-153`).

Closest existing functionality:

- `game_screenshot` captures the host desktop via PowerShell `CopyFromScreen`, so it is not an in-game bridge screenshot path (`tools/game.py:149-195`).
- `settings_get` / `settings_set` / `settings_toggle` operate on `SavedSettings.json`, but they do not load saves or expose telemetry (`tools/settings.py:10-95`).
- `game_launch`, `game_kill`, `game_status`, and `game_send_key` are process/input wrappers only (`tools/game.py:65-224`).

The design doc for plate-19 explicitly expects `load_save(path)`, `capture_screenshot(path)`, and `query_telemetry()` as bridge RPCs, plus a Unity-side main-thread queue (`docs/journeys/scratch/game-bridge-rpc-design.md:19-67`).

## Thread-safety / Unity main thread

Current MCP behavior does not touch WorldBox’s Unity main thread at all. The Python server shells out to OS processes, reads/writes files, and captures the desktop; there is no `MonoBehaviour`, no Unity API call, and no queue into the game process (`tools/game.py:65-224`, `tools/log.py:17-100`, `tools/settings.py:10-95`, `tools/build.py:21-115`, `tools/journey.py:11-92`, `tools/codex.py:56-153`).

That means thread-safety concerns are currently host-side only. The proposed bridge design is different: Unity-safe work must be enqueued to a main-thread command queue drained from `Update()`; the HTTP request thread must not call Unity APIs directly (`docs/journeys/scratch/game-bridge-rpc-design.md:50-67`).

## Health endpoint / port 8765

`/health` is implemented in this MCP server, but the server defaults to `127.0.0.1:8766`, not `8765` (`server.py:12`, `server.py:300-309`, `server.py:330-350`). The repo also forwards both `8765` and `8766` in devcontainer config, and the server comment explicitly says `8766` is used “to avoid collision with dinoforge@8765” (`.devcontainer/devcontainer.json:44-54`, `server.py:12`).

Conclusion: the health endpoint on `:8765` is not from this MCP server. If anything is listening there, it is a different service; this repo’s FastMCP health route is on the MCP server itself, with the default HTTP port at `8766` (`server.py:300-309`, `server.py:332-350`).

## Startup note

`Tools/wsm3d-mcp/wsm3d_mcp/server.py` currently has a syntax-level indentation problem at the `mcp = FastMCP(...)` definition (`server.py:45`). `python -m py_compile Tools/wsm3d-mcp/wsm3d_mcp/server.py` fails with `IndentationError`, so the entrypoint is not currently importable as written.
