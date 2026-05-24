"""Live verification tools: offline harness, PlayCUA scenarios, pipeline docs."""

from __future__ import annotations

import json
import logging
import re
import subprocess
from pathlib import Path

from wsm3d_mcp.paths import REPO_ROOT as TOOLS_ROOT

logger = logging.getLogger("wsm3d_mcp")

WORLD_REPO = TOOLS_ROOT.parent
LIVE_VERIFY_SCRIPT = TOOLS_ROOT / "wsm-live-verify.ps1"
LIVE_VERIFY_REPORT = TOOLS_ROOT / ".reports/live-verify-latest.json"
PLAYCUA_SCENARIOS_DIR = TOOLS_ROOT / "wsm3d-playcua/sample-scenarios"
LIVE_VERIFICATION_DOC = WORLD_REPO / "docs/live-verification.md"

_NAME_RE = re.compile(r"^name:\s*(.+?)\s*$", re.MULTILINE)


def _read_scenario_name(path: Path) -> str:
    try:
        head = path.read_text(encoding="utf-8")[:4096]
    except OSError as exc:
        logger.warning("Could not read %s: %s", path, exc)
        return path.stem
    match = _NAME_RE.search(head)
    return match.group(1).strip() if match else path.stem


async def run_live_verify_offline(timeout_s: int = 3600) -> dict:
    """
    Run Tools/wsm-live-verify.ps1 without -Live (dotnet tests + journey mock; Stage 3 skipped).
    Writes Tools/.reports/live-verify-latest.json.
    """
    if not LIVE_VERIFY_SCRIPT.exists():
        return {
            "ok": False,
            "error": f"Missing harness script: {LIVE_VERIFY_SCRIPT}",
        }

    cmd = ["pwsh", "-NoProfile", "-File", str(LIVE_VERIFY_SCRIPT)]
    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout_s,
            cwd=str(WORLD_REPO),
        )
    except subprocess.TimeoutExpired:
        return {
            "ok": False,
            "error": f"live-verify timed out after {timeout_s}s",
            "report_path": str(LIVE_VERIFY_REPORT),
        }
    except Exception as exc:
        logger.error("run_live_verify_offline error: %s", exc)
        return {"ok": False, "error": str(exc)}

    report: dict | None = None
    if LIVE_VERIFY_REPORT.exists():
        try:
            report = json.loads(LIVE_VERIFY_REPORT.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            logger.warning("Could not parse %s: %s", LIVE_VERIFY_REPORT, exc)

    log_tail = "\n".join((result.stdout + result.stderr).splitlines()[-40:])
    ok = result.returncode == 0 and (report is None or report.get("overallOk", True))

    return {
        "ok": ok,
        "exit_code": result.returncode,
        "live": False,
        "report_path": str(LIVE_VERIFY_REPORT),
        "report": report,
        "log_tail": log_tail,
    }


async def list_playcua_scenarios() -> dict:
    """List YAML sample scenarios under Tools/wsm3d-playcua/sample-scenarios."""
    if not PLAYCUA_SCENARIOS_DIR.exists():
        return {
            "ok": False,
            "scenarios": [],
            "error": f"Scenario directory not found: {PLAYCUA_SCENARIOS_DIR}",
        }

    scenarios = []
    for path in sorted(PLAYCUA_SCENARIOS_DIR.glob("*.yaml")):
        scenarios.append(
            {
                "file": path.name,
                "name": _read_scenario_name(path),
                "path": str(path),
            }
        )

    return {
        "ok": True,
        "count": len(scenarios),
        "scenario_dir": str(PLAYCUA_SCENARIOS_DIR),
        "scenarios": scenarios,
    }


async def describe_live_verification() -> dict:
    """Structured summary of the live verification gates and harness stages."""
    stages = [
        {
            "id": "dotnet-tests",
            "gate": "programmatic",
            "summary": "dotnet test WorldSphereMod.Tests.{Unit,Integration,E2E} (Release)",
        },
        {
            "id": "journey-mock-verify",
            "gate": "programmatic",
            "summary": "phenotype-journey verify --mock via Tools/verify-journeys.ps1",
        },
        {
            "id": "live-playcua-ssim",
            "gate": "agentic",
            "summary": "Bridge :8766, all sample-scenarios/*.yaml, SSIM vs docs/journeys/phase-previews (requires -Live)",
            "skipped_offline_reason": "Pass -Live to require bridge, run playcua, and SSIM-compare phase previews.",
        },
    ]

    return {
        "ok": True,
        "doc_path": str(LIVE_VERIFICATION_DOC),
        "doc_exists": LIVE_VERIFICATION_DOC.exists(),
        "harness_script": str(LIVE_VERIFY_SCRIPT),
        "report_path": str(LIVE_VERIFY_REPORT),
        "ssim_threshold": 0.95,
        "bridge_url": "http://127.0.0.1:8766/health",
        "commands": {
            "offline": "pwsh Tools/wsm-live-verify.ps1",
            "live": "pwsh Tools/wsm-live-verify.ps1 -Live",
            "live_with_vision": "pwsh Tools/wsm-live-verify.ps1 -Live -Vision",
            "playcua_scenario": "python Tools/wsm3d-playcua/main.py <scenario.yaml>",
            "playcua_smoke": "python Tools/wsm3d-playcua/smoke.py",
        },
        "gates": {
            "programmatic": [
                "dotnet test (unit, integration, e2e)",
                "journey mock verify (manifest/schema/canonical previews)",
                "optional local SSIM via Tools/wsm-ssim-compare.py",
            ],
            "agentic": [
                "wsm3d-playcua YAML scenarios (bridge actions + screenshots)",
                "OmniRoute vision combo for screenshot vision: steps",
                "bridge smoke.py JSON invariants",
                "journey verify -Live for real captures",
            ],
        },
        "stages": stages,
        "scenario_dir": str(PLAYCUA_SCENARIOS_DIR),
    }
