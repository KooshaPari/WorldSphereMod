"""Game control tools: launch, kill, status, screenshot."""

import asyncio
import logging
import os
import subprocess
import tempfile
from pathlib import Path
from pydantic import BaseModel
from wsm3d_mcp.paths import GAME_EXE, PLAYER_LOG

logger = logging.getLogger("wsm3d_mcp")


class GameStatusResponse(BaseModel):
    running: bool
    pid: int | None = None
    ram_mb: int | None = None
    uptime_min: float | None = None
    log_size: int | None = None
    log_mtime: float | None = None


class GameScreenshotResponse(BaseModel):
    ok: bool
    path: str | None = None
    width: int | None = None
    height: int | None = None
    error: str | None = None


async def game_launch() -> dict:
    """Launch worldbox game. Returns {ok: bool, pid: int or error: str}."""
    if not GAME_EXE.exists():
        return {"ok": False, "error": f"Game exe not found: {GAME_EXE}"}
    try:
        proc = subprocess.Popen(
            [str(GAME_EXE)],
            cwd=str(GAME_EXE.parent),
        )
        return {"ok": True, "pid": proc.pid}
    except Exception as e:
        return {"ok": False, "error": str(e)}


async def game_kill() -> dict:
    """Kill all worldbox processes. Returns {ok: bool, killed_pids: list[int]}."""
    try:
        result = subprocess.run(
            [
                "powershell",
                "-NoProfile",
                "-Command",
                "Get-Process worldbox -ErrorAction SilentlyContinue | Stop-Process -Force; $?",
            ],
            capture_output=True,
            text=True,
            timeout=10,
        )
        if result.returncode == 0:
            return {"ok": True, "killed_pids": []}
        return {"ok": True, "killed_pids": []}
    except Exception as e:
        return {"ok": False, "error": str(e)}


async def game_status() -> GameStatusResponse:
    """Get game process status."""
    try:
        # Check if process is running via powershell
        result = subprocess.run(
            [
                "powershell",
                "-NoProfile",
                "-Command",
                "Get-Process worldbox -ErrorAction SilentlyContinue | Select-Object Id, @{N='WorkingSetMB';E={[int]($_.WorkingSet64/1MB)}} | ConvertTo-Json",
            ],
            capture_output=True,
            text=True,
            timeout=5,
        )

        import json

        proc_info = None
        if result.stdout.strip():
            try:
                proc_info = json.loads(result.stdout)
            except json.JSONDecodeError:
                pass

        log_size = None
        log_mtime = None
        if PLAYER_LOG.exists():
            stat = PLAYER_LOG.stat()
            log_size = stat.st_size
            log_mtime = stat.st_mtime

        if proc_info and isinstance(proc_info, dict):
            return GameStatusResponse(
                running=True,
                pid=proc_info.get("Id"),
                ram_mb=proc_info.get("WorkingSetMB"),
                log_size=log_size,
                log_mtime=log_mtime,
            )

        return GameStatusResponse(
            running=False, log_size=log_size, log_mtime=log_mtime
        )
    except Exception as e:
        logger.error(f"game_status error: {e}")
        return GameStatusResponse(running=False, log_size=None, log_mtime=None)


async def game_screenshot(out_path: str | None = None) -> GameScreenshotResponse:
    """
    Capture screenshot using PowerShell System.Drawing.
    Returns {ok: bool, path: str, width: int, height: int} or {ok: False, error: str}
    """
    if not out_path:
        temp_dir = Path(os.getenv("TEMP", "/tmp")) / "wsm3d-mcp"
        temp_dir.mkdir(parents=True, exist_ok=True)
        out_path = str(temp_dir / "screenshot.png")

    script = f"""
Add-Type -AssemblyName System.Drawing
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$bitmap.Save('{out_path}')
$graphics.Dispose()
$bitmap.Dispose()
Write-Output "$($bounds.Width)x$($bounds.Height)"
"""

    try:
        result = subprocess.run(
            ["powershell", "-NoProfile", "-Command", script],
            capture_output=True,
            text=True,
            timeout=10,
        )
        if result.returncode == 0 and "x" in result.stdout:
            w, h = result.stdout.strip().split("x")
            return GameScreenshotResponse(
                ok=True, path=out_path, width=int(w), height=int(h)
            )
        return GameScreenshotResponse(
            ok=False, error=result.stderr or "Screenshot failed"
        )
    except Exception as e:
        return GameScreenshotResponse(ok=False, error=str(e))


async def game_send_key(key: str, repeat: int = 1) -> dict:
    """
    Send keyboard input via PowerShell SendKeys.
    Returns {ok: bool} or {ok: False, error: str}
    """
    script = f"""
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('{{{key}}}' * {repeat})
Write-Output 'ok'
"""
    try:
        result = subprocess.run(
            ["powershell", "-NoProfile", "-Command", script],
            capture_output=True,
            text=True,
            timeout=5,
        )
        if result.returncode == 0:
            return {"ok": True}
        return {"ok": False, "error": result.stderr or "SendKeys failed"}
    except Exception as e:
        return {"ok": False, "error": str(e)}
