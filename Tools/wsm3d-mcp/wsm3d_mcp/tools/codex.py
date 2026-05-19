"""Codex bridge tools for MCP."""

from __future__ import annotations

import asyncio
import json
import logging
import os
from pathlib import Path

logger = logging.getLogger("wsm3d_mcp")


ALLOWED_MODELS = [
    "gpt-5.3-codex-spark",
    "gpt-5.4-mini",
    "gpt-5.4-mini-codex",
    "o3",
]
ALLOWED_REASONING = ["low", "medium", "high", "xhigh"]


def _validate_model(model: str) -> str | None:
    if model not in ALLOWED_MODELS:
        return f"Invalid model '{model}'. Allowed: {', '.join(ALLOWED_MODELS)}"
    return None


def _validate_reasoning(reasoning: str) -> str | None:
    if reasoning not in ALLOWED_REASONING:
        return f"Invalid reasoning '{reasoning}'. Allowed: {', '.join(ALLOWED_REASONING)}"
    return None


def _parse_tokens(text: str) -> int:
    try:
        payload = json.loads(text)
    except json.JSONDecodeError:
        return 0

    if isinstance(payload, dict):
        direct = payload.get("tokens_used")
        if isinstance(direct, int):
            return direct

        usage = payload.get("usage")
        if isinstance(usage, dict):
            for key in ("total_tokens", "tokens", "completion_tokens", "prompt_tokens"):
                value = usage.get(key)
                if isinstance(value, int):
                    return value

    return 0


async def _run_codex(command: list[str], cwd: str | None = None, timeout_s: int = 300) -> dict:
    """Run a codex subprocess and capture result."""
    try:
        proc = await asyncio.create_subprocess_exec(
            *command,
            stdin=asyncio.subprocess.DEVNULL,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
            cwd=cwd,
            env=os.environ.copy(),
        )

        stdout_raw, stderr_raw = await asyncio.wait_for(proc.communicate(), timeout=timeout_s)
        stdout = stdout_raw.decode("utf-8", errors="replace")
        stderr = stderr_raw.decode("utf-8", errors="replace")
    except asyncio.TimeoutError:
        proc.kill()
        await proc.wait()
        return {
            "ok": False,
            "stdout": "",
            "stderr": f"Command timed out after {timeout_s}s",
            "exit_code": 124,
            "tokens_used": 0,
        }
    except Exception as exc:  # pragma: no cover
        return {
            "ok": False,
            "stdout": "",
            "stderr": str(exc),
            "exit_code": 1,
            "tokens_used": 0,
        }

    tokens_used = _parse_tokens(stdout)
    return {
        "ok": proc.returncode == 0,
        "stdout": stdout,
        "stderr": stderr,
        "exit_code": proc.returncode,
        "tokens_used": tokens_used,
    }


async def codex_exec(
    prompt: str,
    model: str = "gpt-5.3-codex-spark",
    reasoning: str = "medium",
    workdir: str | None = None,
    extra_dirs: list[str] = [],
    timeout_s: int = 300,
) -> dict:
    """
    Run `codex exec` for the given prompt.
    Returns {ok, stdout, stderr, exit_code, tokens_used}.
    """
    model_error = _validate_model(model)
    if model_error:
        return {
            "ok": False,
            "stdout": "",
            "stderr": model_error,
            "exit_code": 1,
            "tokens_used": 0,
        }

    reasoning_error = _validate_reasoning(reasoning)
    if reasoning_error:
        return {
            "ok": False,
            "stdout": "",
            "stderr": reasoning_error,
            "exit_code": 1,
            "tokens_used": 0,
        }

    resolved_workdir = None
    if workdir is not None:
        resolved_workdir = str(Path(workdir).expanduser().resolve())

    command = ["codex", "exec", "--model", model, "--reasoning", reasoning]
    if resolved_workdir is not None:
        command.extend(["--workdir", resolved_workdir])
    for directory in extra_dirs:
        command.extend(["--extra-dir", str(Path(directory).expanduser())])
    command.append(prompt)

    return await _run_codex(command, cwd=resolved_workdir, timeout_s=timeout_s)


async def codex_doctor() -> dict:
    """Run `codex doctor`. Returns command status and output."""
    return await _run_codex(["codex", "doctor"], timeout_s=120)


def codex_models() -> list[str]:
    """Return hard-coded allowed Codex model list."""
    return ALLOWED_MODELS.copy()
