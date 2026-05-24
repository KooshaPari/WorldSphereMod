"""Log tools: tail, grep, clear."""

import logging
import re
from pathlib import Path
from pydantic import BaseModel
from wsm3d_mcp.paths import PLAYER_LOG

logger = logging.getLogger("wsm3d_mcp")


class LogLine(BaseModel):
    line_no: int
    line: str


async def log_tail(n: int = 50, grep: str | None = None) -> dict:
    """
    Read last N lines from Player.log.
    If grep pattern provided, filter matching lines.
    Returns {lines: list[str]}
    """
    if not PLAYER_LOG.exists():
        return {"lines": [], "error": f"Log not found: {PLAYER_LOG}"}

    try:
        with open(PLAYER_LOG, "r", encoding="utf-8", errors="replace") as f:
            all_lines = f.readlines()

        tail = all_lines[-n:] if n > 0 else all_lines
        tail = [line.rstrip() for line in tail]

        if grep:
            try:
                pattern = re.compile(grep, re.IGNORECASE)
                tail = [line for line in tail if pattern.search(line)]
            except re.error as e:
                return {"lines": tail, "grep_error": str(e)}

        return {"lines": tail}
    except Exception as e:
        logger.error(f"log_tail error: {e}")
        return {"lines": [], "error": str(e)}


async def log_grep(
    pattern: str, head: int = 50
) -> dict:
    """
    Search Player.log for pattern.
    Returns {matches: list[{line_no, line}]}
    """
    if not PLAYER_LOG.exists():
        return {"matches": [], "error": f"Log not found: {PLAYER_LOG}"}

    try:
        regex = re.compile(pattern, re.IGNORECASE)
    except re.error as e:
        return {"matches": [], "error": f"Invalid regex: {e}"}

    try:
        with open(PLAYER_LOG, "r", encoding="utf-8", errors="replace") as f:
            lines = f.readlines()

        matches = []
        for i, line in enumerate(lines, start=1):
            if regex.search(line):
                matches.append({"line_no": i, "line": line.rstrip()})
                if len(matches) >= head:
                    break

        return {"matches": matches}
    except Exception as e:
        logger.error(f"log_grep error: {e}")
        return {"matches": [], "error": str(e)}


async def log_clear_buffer() -> dict:
    """
    Truncate Player.log to keep only the last 10K lines.
    Returns {ok: bool}
    """
    if not PLAYER_LOG.exists():
        return {"ok": False, "error": f"Log not found: {PLAYER_LOG}"}

    try:
        with open(PLAYER_LOG, "r", encoding="utf-8", errors="replace") as f:
            lines = f.readlines()

        # Keep last 10K lines
        tail = lines[-10000:] if len(lines) > 10000 else lines

        with open(PLAYER_LOG, "w", encoding="utf-8") as f:
            f.writelines(tail)

        logger.info(f"Truncated log from {len(lines)} to {len(tail)} lines")
        return {"ok": True, "lines_removed": len(lines) - len(tail)}
    except Exception as e:
        logger.error(f"log_clear_buffer error: {e}")
        return {"ok": False, "error": str(e)}
