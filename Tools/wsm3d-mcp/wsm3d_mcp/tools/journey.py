"""Journey tools: phenotype-journey verification integration."""

import json
import logging
import os
import subprocess
from pathlib import Path

logger = logging.getLogger("wsm3d_mcp")
REPO_ROOT = Path(__file__).resolve().parents[4]
LOCAL_PHENOTYPE_REPO = Path("C:/Users/koosh/Dino/tools/phenotype-journeys")
LOCAL_PHENOTYPE_CACHE = REPO_ROOT / "tools/.cache/phenotype-journeys"


def _journey_binary_candidates() -> list[Path]:
    return [
        LOCAL_PHENOTYPE_REPO / "target/release/phenotype-journey.exe",
        LOCAL_PHENOTYPE_REPO / "target/release/phenotype-journey",
        LOCAL_PHENOTYPE_CACHE / "target/release/phenotype-journey.exe",
        LOCAL_PHENOTYPE_CACHE / "target/release/phenotype-journey",
    ]


def _resolve_manifest_path(journey_id: str) -> Path:
    manifest = REPO_ROOT / "docs/journeys/manifests" / journey_id / "manifest.json"
    if not manifest.exists():
        raise FileNotFoundError(f"Manifest not found for journey '{journey_id}': {manifest}")
    return manifest


def _build_journey_binary() -> Path | None:
    for root in (LOCAL_PHENOTYPE_REPO, LOCAL_PHENOTYPE_CACHE):
        if not root.exists():
            continue

        try:
            result = subprocess.run(
                ["cargo", "build", "--release", "--bin", "phenotype-journey"],
                cwd=str(root),
                capture_output=True,
                text=True,
                timeout=600,
            )
        except FileNotFoundError:
            logger.error("cargo not found while building phenotype-journey")
            return None

        if result.returncode != 0:
            logger.error("phenotype-journey build failed in %s: %s", root, result.stderr.strip())
            continue

        for candidate in _journey_binary_candidates():
            if candidate.exists():
                return candidate

    return None


def _resolve_journey_binary() -> Path | None:
    if os.environ.get("PATH"):
        try:
            result = subprocess.run(
                ["phenotype-journey", "--help"],
                capture_output=True,
                text=True,
                timeout=10,
            )
            if result.returncode == 0:
                return Path("phenotype-journey")
        except FileNotFoundError:
            pass

    for candidate in _journey_binary_candidates():
        if candidate.exists():
            return candidate

    return _build_journey_binary()


async def journey_list() -> dict:
    """Deprecated wrapper-local alias that returns local manifest IDs only."""
    try:
        index_path = REPO_ROOT / "docs/journeys/manifests/index.json"
        if index_path.exists():
            data = json.loads(index_path.read_text(encoding="utf-8"))
            return {"ok": True, "deprecated": True, "warning": "journey_list is wrapper-local and deprecated; use journey_verify with a manifest ID or path.", "journeys": data}

        manifest_root = REPO_ROOT / "docs/journeys/manifests"
        journeys = []
        for manifest in sorted(manifest_root.rglob("manifest.json")):
            journeys.append(
                {
                    "id": manifest.parent.name,
                    "intent": "",
                    "file": str(manifest.relative_to(manifest_root)),
                }
            )
        return {"ok": True, "deprecated": True, "warning": "journey_list is wrapper-local and deprecated; use journey_verify with a manifest ID or path.", "journeys": journeys}
    except Exception as e:
        logger.error(f"journey_list error: {e}")
        return {"ok": False, "deprecated": True, "warning": "journey_list is wrapper-local and deprecated; use journey_verify with a manifest ID or path.", "journeys": [], "error": str(e)}


async def journey_run(journey_id: str) -> dict:
    """Deprecated wrapper-local alias for mock verification of a journey by ID."""
    try:
        binary = _resolve_journey_binary()
        if binary is None:
            return {"ok": False, "error": "phenotype-journey not on PATH and no local source or cache build was available"}
        manifest_path = _resolve_manifest_path(journey_id)
        result = subprocess.run(
            [str(binary), "verify", str(manifest_path), "--mock"],
            capture_output=True,
            text=True,
            timeout=300,
        )
        if result.returncode == 0:
            return {
                "ok": True,
                "deprecated": True,
                "warning": "journey_run is deprecated; use journey_verify instead.",
                "manifest_path": str(manifest_path),
            }
        return {"ok": False, "deprecated": True, "warning": "journey_run is deprecated; use journey_verify instead.", "error": result.stderr or "Run failed"}
    except Exception as e:
        logger.error(f"journey_run error: {e}")
        return {"ok": False, "deprecated": True, "warning": "journey_run is deprecated; use journey_verify instead.", "error": str(e)}


async def journey_verify(journey_ref: str, live: bool = False) -> dict:
    """Verify a manifest by ID or path using phenotype-journey."""
    try:
        binary = _resolve_journey_binary()
        if binary is None:
            return {"ok": False, "error": "phenotype-journey not on PATH and no local source or cache build was available"}
        manifest_path = Path(journey_ref)
        if not manifest_path.exists():
            manifest_path = _resolve_manifest_path(journey_ref)
        mode_flag = "--live" if live else "--mock"
        result = subprocess.run(
            [str(binary), "verify", str(manifest_path), mode_flag],
            capture_output=True,
            text=True,
            timeout=30,
        )
        if result.returncode == 0:
            return {
                "ok": True,
                "mode": "live" if live else "mock",
                "score": 1.0,
                "violations": [],
            }
        return {"ok": False, "error": result.stderr or "Verify failed"}
    except Exception as e:
        logger.error(f"journey_verify error: {e}")
        return {"ok": False, "error": str(e)}
