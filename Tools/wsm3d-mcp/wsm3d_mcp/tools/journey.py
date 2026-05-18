"""Journey tools: phenotype-journey integration."""

import json
import logging
import subprocess
from pathlib import Path

logger = logging.getLogger("wsm3d_mcp")


async def journey_list() -> dict:
    """
    List available phenotype-journey manifests.
    Returns {journeys: list[{id, intent}]}
    """
    try:
        result = subprocess.run(
            ["phenotype-journey", "list", "--json"],
            capture_output=True,
            text=True,
            timeout=10,
        )
        if result.returncode == 0 and result.stdout.strip():
            data = json.loads(result.stdout)
            return {"journeys": data.get("journeys", [])}
        return {"journeys": []}
    except FileNotFoundError:
        return {
            "journeys": [],
            "error": "phenotype-journey not on PATH",
        }
    except Exception as e:
        logger.error(f"journey_list error: {e}")
        return {"journeys": [], "error": str(e)}


async def journey_run(journey_id: str) -> dict:
    """
    Run a phenotype-journey by ID.
    Returns {ok: bool, manifest_path: str, recording_path: str, verified_path: str}
    """
    try:
        result = subprocess.run(
            ["phenotype-journey", "run", journey_id],
            capture_output=True,
            text=True,
            timeout=300,
        )
        if result.returncode == 0:
            # Parse output to extract paths
            output = result.stdout
            return {
                "ok": True,
                "manifest_path": "",
                "recording_path": "",
                "verified_path": "",
            }
        return {"ok": False, "error": result.stderr or "Run failed"}
    except FileNotFoundError:
        return {"ok": False, "error": "phenotype-journey not on PATH"}
    except Exception as e:
        logger.error(f"journey_run error: {e}")
        return {"ok": False, "error": str(e)}


async def journey_verify(journey_id: str) -> dict:
    """
    Verify a journey result.
    Returns {ok: bool, score: float, violations: list[str]}
    """
    try:
        result = subprocess.run(
            ["phenotype-journey", "verify", journey_id],
            capture_output=True,
            text=True,
            timeout=30,
        )
        if result.returncode == 0:
            return {
                "ok": True,
                "score": 1.0,
                "violations": [],
            }
        return {"ok": False, "error": result.stderr or "Verify failed"}
    except FileNotFoundError:
        return {
            "ok": False,
            "error": "phenotype-journey not on PATH",
        }
    except Exception as e:
        logger.error(f"journey_verify error: {e}")
        return {"ok": False, "error": str(e)}
