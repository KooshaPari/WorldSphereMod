"""Build and install tools: mod_build, mod_install, mod_relaunch."""

import asyncio
import logging
import subprocess
from pathlib import Path
from pydantic import BaseModel
from wsm3d_mcp.paths import REPO_ROOT

logger = logging.getLogger("wsm3d_mcp")


class BuildResponse(BaseModel):
    ok: bool
    warnings: int = 0
    time_s: float = 0.0
    log_tail: str = ""
    error: str | None = None


async def _run_ps1_cli(
    cli_script: Path, args: list[str] = None, timeout: int = 120
) -> tuple[bool, str, str]:
    """
    Run PowerShell CLI script with args.
    Returns (success: bool, stdout: str, stderr: str)
    """
    cmd = ["pwsh", "-NoProfile", "-File", str(cli_script)]
    if args:
        cmd.extend(args)

    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout,
            cwd=str(REPO_ROOT),
        )
        return result.returncode == 0, result.stdout, result.stderr
    except subprocess.TimeoutExpired:
        return False, "", f"Command timed out after {timeout}s"
    except Exception as e:
        return False, "", str(e)


async def mod_build(configuration: str = "Release") -> BuildResponse:
    """
    Build the mod.
    Shells to Tools/wsm3d.ps1 build.
    Returns {ok: bool, warnings: int, time_s: float, log_tail: str}
    """
    cli_script = REPO_ROOT / "Tools/wsm3d.ps1"
    if not cli_script.exists():
        return BuildResponse(ok=False, error=f"CLI script not found: {cli_script}")

    success, stdout, stderr = await _run_ps1_cli(cli_script, ["build", configuration])

    # Parse output for warnings and timing
    warnings = stdout.count("warning")
    log_tail = "\n".join((stdout + stderr).split("\n")[-20:])

    if success:
        return BuildResponse(
            ok=True, warnings=warnings, time_s=0.0, log_tail=log_tail
        )

    return BuildResponse(
        ok=False, warnings=warnings, log_tail=log_tail, error=stderr
    )


async def mod_install(launch: bool = False, no_build: bool = False) -> dict:
    """
    Install the mod to game directory.
    Shells to Tools/wsm3d.ps1 install [-Launch] [-NoBuild].
    Returns {ok: bool, installed_to: str} or {ok: False, error: str}
    """
    cli_script = REPO_ROOT / "Tools/wsm3d.ps1"
    if not cli_script.exists():
        return {"ok": False, "error": f"CLI script not found: {cli_script}"}

    args = ["install"]
    if launch:
        args.append("-Launch")
    if no_build:
        args.append("-NoBuild")

    success, stdout, stderr = await _run_ps1_cli(cli_script, args)

    if success:
        # Extract install path from output (typically last line with path)
        lines = stdout.strip().split("\n")
        installed_to = lines[-1] if lines else "unknown"
        return {"ok": True, "installed_to": installed_to}

    return {"ok": False, "error": stderr or stdout}


async def mod_relaunch(no_build: bool = False) -> dict:
    """
    Rebuild, install, and launch.
    Returns {ok: bool}
    """
    # Build
    build_result = await mod_build()
    if not build_result.ok:
        return {"ok": False, "error": f"Build failed: {build_result.error}"}

    # Install with launch
    install_result = await mod_install(launch=True, no_build=no_build)
    if not install_result.get("ok"):
        return {"ok": False, "error": install_result.get("error")}

    return {"ok": True}
