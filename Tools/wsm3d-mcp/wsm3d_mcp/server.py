"""
WorldSphereMod3D MCP Server — FastMCP HTTP/SSE + stdio

Architecture:
  MCP Client (Claude) → FastMCP server
    ├─ game_* tools   → game control (launch, kill, screenshot)
    ├─ log_* tools    → Player.log read/grep/tail
    ├─ settings_*     → SavedSettings.json I/O
    ├─ build_*        → shells to wsm3d.ps1
    └─ journey_*      → phenotype-journey wrapper

Default transport: HTTP/SSE on 127.0.0.1:8766 (to avoid collision with dinoforge@8765).
Override port via WSM3D_MCP_PORT env var.
Stdio mode via --stdio flag for Claude Desktop config.
"""

from __future__ import annotations

import argparse
import asyncio
import logging
import os
from pathlib import Path

from dotenv import load_dotenv
from fastmcp import FastMCP, Context
from pydantic import BaseModel
from starlette.responses import JSONResponse
from starlette.requests import Request

from wsm3d_mcp.paths import validate_paths
from wsm3d_mcp.tools import build, codex, game, journey, log, settings

load_dotenv()
logging.basicConfig(
    level=logging.INFO if os.getenv("WSM3D_MCP_DEBUG") else logging.WARNING,
    format="%(name)s [%(levelname)s] %(message)s",
)
logger = logging.getLogger("wsm3d_mcp")

# ============================================================================
# FastMCP Server
# ============================================================================

    mcp = FastMCP(
        "wsm3d",
        instructions=(
            "WorldSphereMod3D unified MCP server. "
            "game_* tools: game process management and screenshots. "
            "log_* tools: Player.log analysis (tail, grep, clear). "
            "settings_* tools: SavedSettings.json I/O. "
            "build_*: mod compilation and deployment (wraps wsm3d.ps1). "
            "journey_*: phenotype-journey integration. "
            "codex_*: codex CLI helpers."
        ),
    )

# ============================================================================
# GAME CONTROL TOOLS
# ============================================================================


@mcp.tool()
async def game_launch(ctx: Context) -> dict:
    """Launch WorldBox game. Returns {ok: bool, pid: int}."""
    return await game.game_launch()


@mcp.tool()
async def game_kill(ctx: Context) -> dict:
    """Kill all WorldBox processes. Returns {ok: bool}."""
    return await game.game_kill()


@mcp.tool()
async def game_status(ctx: Context) -> dict:
    """
    Get game process status.
    Returns {running: bool, pid: int|None, ram_mb: int|None, log_size: int|None, log_mtime: float|None}.
    """
    result = await game.game_status()
    return result.model_dump(exclude_none=True)


@mcp.tool()
async def game_screenshot(ctx: Context, out_path: str | None = None) -> dict:
    """
    Capture screenshot to PNG.
    Returns {ok: bool, path: str, width: int, height: int} or {ok: False, error: str}.
    """
    result = await game.game_screenshot(out_path)
    return result.model_dump(exclude_none=True)


@mcp.tool()
async def game_send_key(ctx: Context, key: str, repeat: int = 1) -> dict:
    """
    Send keyboard input. Key can be 'f', 'enter', 'space', 'tab', etc.
    Returns {ok: bool}.
    """
    return await game.game_send_key(key, repeat)


# ============================================================================
# LOG TOOLS
# ============================================================================


@mcp.tool()
async def log_tail(ctx: Context, n: int = 50, grep: str | None = None) -> dict:
    """
    Read last N lines from Player.log.
    If grep pattern provided, filter matching lines.
    Returns {lines: list[str]}.
    """
    return await log.log_tail(n, grep)


@mcp.tool()
async def log_grep(ctx: Context, pattern: str, head: int = 50) -> dict:
    """
    Search Player.log for regex pattern.
    Returns {matches: list[{line_no: int, line: str}]}.
    """
    return await log.log_grep(pattern, head)


@mcp.tool()
async def log_clear_buffer(ctx: Context) -> dict:
    """
    Truncate Player.log to last 10K lines to keep size manageable.
    Returns {ok: bool, lines_removed: int}.
    """
    return await log.log_clear_buffer()


# ============================================================================
# SETTINGS TOOLS
# ============================================================================


@mcp.tool()
async def settings_get(ctx: Context, key: str | None = None) -> dict:
    """
    Read SavedSettings.json.
    If key is None, return full dict. Otherwise return single field.
    Returns {settings: dict | any}.
    """
    return await settings.settings_get(key)


@mcp.tool()
async def settings_set(ctx: Context, key: str, value: str) -> dict:
    """
    Set a field in SavedSettings.json.
    Validates field exists before writing.
    Returns {ok: bool, old: any, new: any}.
    """
    return await settings.settings_set(key, value)


@mcp.tool()
async def settings_toggle(ctx: Context, phase: str) -> dict:
    """
    Toggle a boolean phase setting.
    Accepts snake_case OR camelCase.
    Returns {ok: bool, old: bool, new: bool}.
    """
    return await settings.settings_toggle(phase)


# ============================================================================
# BUILD TOOLS
# ============================================================================


@mcp.tool()
async def mod_build(ctx: Context, configuration: str = "Release") -> dict:
    """
    Build the mod. Shells to Tools/wsm3d.ps1.
    Returns {ok: bool, warnings: int, time_s: float, log_tail: str}.
    """
    result = await build.mod_build(configuration)
    return result.model_dump(exclude_none=True)


@mcp.tool()
async def mod_install(ctx: Context, launch: bool = False, no_build: bool = False) -> dict:
    """
    Install mod to game directory. Shells to Tools/wsm3d.ps1.
    Returns {ok: bool, installed_to: str} or {ok: False, error: str}.
    """
    return await build.mod_install(launch, no_build)


@mcp.tool()
async def mod_relaunch(ctx: Context, no_build: bool = False) -> dict:
    """Build, install, and launch. Returns {ok: bool}."""
    return await build.mod_relaunch(no_build)


# ============================================================================
# JOURNEY TOOLS
# ============================================================================


@mcp.tool()
async def journey_list(ctx: Context) -> dict:
    """
    List available phenotype-journey manifests.
    Returns {journeys: list[{id: str, intent: str}]}.
    """
    return await journey.journey_list()


@mcp.tool()
async def journey_run(ctx: Context, journey_id: str) -> dict:
    """
    Run a phenotype-journey by ID.
    Returns {ok: bool, manifest_path: str, recording_path: str, verified_path: str}.
    """
    return await journey.journey_run(journey_id)


@mcp.tool()
async def journey_verify(ctx: Context, journey_id: str) -> dict:
    """
    Verify a journey result.
    Returns {ok: bool, score: float, violations: list[str]}.
    """
    return await journey.journey_verify(journey_id)


# ============================================================================
# CODEX TOOLS
# ============================================================================


@mcp.tool()
async def codex_exec(
    ctx: Context,
    prompt: str,
    model: str = "gpt-5.3-codex-spark",
    reasoning: str = "medium",
    workdir: str | None = None,
    extra_dirs: list[str] = [],
    timeout_s: int = 300,
) -> dict:
    """
    Run a codex prompt.
    Returns {ok, stdout, stderr, exit_code, tokens_used}.
    """
    return await codex.codex_exec(
        prompt,
        model=model,
        reasoning=reasoning,
        workdir=workdir,
        extra_dirs=extra_dirs,
        timeout_s=timeout_s,
    )


@mcp.tool()
async def codex_doctor(ctx: Context) -> dict:
    """Run `codex doctor`. Returns {ok, stdout, stderr, exit_code, tokens_used}."""
    return await codex.codex_doctor()


@mcp.tool()
async def codex_models(ctx: Context) -> list[str]:
    """Return hardcoded Codex model whitelist."""
    return codex.codex_models()


# ============================================================================
# STATUS TOOLS
# ============================================================================


@mcp.tool()
async def status(ctx: Context) -> dict:
    """
    Get overall system status: build, install, game, log.
    Returns {build_state: str, install_state: str, game_state: str, log_state: str, last_commit_sha: str}.
    """
    return {
        "build_state": "unknown",
        "install_state": "unknown",
        "game_state": "offline",
        "log_state": "ok",
        "last_commit_sha": "unknown",
    }


# ============================================================================
# HEALTH CHECK ENDPOINT
# ============================================================================


@mcp.custom_route("/health", methods=["GET"])
async def health_check(request: Request):
    """Health check endpoint for service monitoring and startup verification."""
    paths_valid = validate_paths()
    return JSONResponse({
        "status": "ok",
        "server": "wsm3d-mcp",
        "version": "0.1.0",
        "paths": paths_valid,
    })


# ============================================================================
# Entry point
# ============================================================================


def main() -> None:
    """Run the MCP server."""
    parser = argparse.ArgumentParser(
        description="WorldSphereMod3D MCP Server (FastMCP 0.2.0+)",
        epilog="Examples:\n  python -m wsm3d_mcp.server                    # stdio (for MCP client)\n  python -m wsm3d_mcp.server --http --port 8766  # HTTP/SSE",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument(
        "--http",
        action="store_true",
        help="Run as HTTP/SSE server instead of stdio",
    )
    parser.add_argument(
        "--port",
        type=int,
        default=int(os.getenv("WSM3D_MCP_PORT", "8766")),
        help="HTTP server port (default: 8766, overridable via WSM3D_MCP_PORT)",
    )
    parser.add_argument(
        "--host",
        type=str,
        default="127.0.0.1",
        help="HTTP server host (default: 127.0.0.1)",
    )
    args, remaining = parser.parse_known_args()

    if args.http:
        logger.info(f"Starting WorldSphereMod3D MCP in HTTP mode at {args.host}:{args.port}")
        logger.info(f"  JSON-RPC endpoint: http://{args.host}:{args.port}")
        mcp.run(
            transport="http",
            host=args.host,
            port=args.port,
        )
    else:
        logger.info("Starting WorldSphereMod3D MCP in stdio mode (for MCP client)")
        mcp.run()


if __name__ == "__main__":
    main()
