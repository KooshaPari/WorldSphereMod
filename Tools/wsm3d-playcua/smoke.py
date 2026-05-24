#!/usr/bin/env python3
"""
WSM3D BridgeRPC smoke test.

This script calls:
1. /health
2. /telemetry
3. /voxel/sprite?name=walk_0
4. /phase/VoxelEntities

and asserts invariant-level sanity checks.
"""

from __future__ import annotations

import argparse
import json
import urllib.parse
import urllib.request
from typing import Any, Dict


class BridgeClient:
    def __init__(self, host: str, port: int, timeout: float = 10.0) -> None:
        self.base = f"http://{host}:{port}"
        self.timeout = timeout

    def get_json(self, path: str, params: Dict[str, Any] | None = None) -> Dict[str, Any]:
        url = self.base + path
        if params:
            url += "?" + urllib.parse.urlencode(params)

        request = urllib.request.Request(url, method="GET")
        with urllib.request.urlopen(request, timeout=self.timeout) as resp:
            payload = json.loads(resp.read().decode("utf-8") or "{}")
            if not isinstance(payload, dict):
                raise AssertionError(f"{path} response is not a JSON object: {payload}")
            return payload


def _as_bool(payload: Dict[str, Any], key: str) -> bool:
    if key not in payload:
        raise AssertionError(f"missing required field {key!r}")
    value = payload[key]
    if not isinstance(value, bool):
        raise AssertionError(f"{key!r} expected bool, got {type(value).__name__}")
    return value


def _as_number(payload: Dict[str, Any], key: str) -> float:
    if key not in payload:
        raise AssertionError(f"missing required field {key!r}")
    value = payload[key]
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise AssertionError(f"{key!r} expected number, got {type(value).__name__}")
    return float(value)


def _as_str(payload: Dict[str, Any], key: str) -> str:
    if key not in payload:
        raise AssertionError(f"missing required field {key!r}")
    value = payload[key]
    if not isinstance(value, str):
        raise AssertionError(f"{key!r} expected string, got {type(value).__name__}")
    return value


def _build_failure(stage: str, message: str) -> Dict[str, str]:
    return {"stage": stage, "status": "fail", "message": message}


def _run_health_check(client: BridgeClient) -> Dict[str, Any]:
    payload = client.get_json("/health")

    if not isinstance(payload.get("ok"), bool):
        raise AssertionError(f"health ok field invalid: {payload.get('ok')}")
    if not payload.get("ok", False):
        raise AssertionError(f"health returned ok=false: {payload}")

    is_world_3d = _as_bool(payload, "isWorld3D")
    if not is_world_3d:
        raise AssertionError(f"health isWorld3D check failed: {payload}")

    version = _as_str(payload, "version")
    if not version.strip():
        raise AssertionError(f"health version check failed: {payload}")

    return {"stage": "health", "status": "pass", "isWorld3D": is_world_3d, "version": version}


def _run_telemetry_check(client: BridgeClient) -> Dict[str, Any]:
    payload = client.get_json("/telemetry")

    draw_calls = _as_number(payload, "drawCalls")
    if draw_calls <= 0:
        raise AssertionError(f"telemetry drawCalls must be > 0, got {draw_calls}")

    voxel_cache_hit = _as_number(payload, "voxelCacheHit")
    impostor_cache_hit = _as_number(payload, "impostorCacheHit")
    for name, value in (("voxelCacheHit", voxel_cache_hit), ("impostorCacheHit", impostor_cache_hit)):
        if not (0.0 <= value <= 1.0):
            raise AssertionError(f"{name} must be a rate in [0, 1], got {value}")

    return {
        "stage": "telemetry",
        "status": "pass",
        "drawCalls": draw_calls,
        "voxelCacheHit": voxel_cache_hit,
        "impostorCacheHit": impostor_cache_hit,
    }


def _run_sprite_check(client: BridgeClient, sprite_name: str) -> Dict[str, Any]:
    payload = client.get_json("/voxel/sprite", {"name": sprite_name})

    if not payload.get("ok", False):
        raise AssertionError(f"voxel sprite request failed: {payload}")

    invariants = payload.get("invariants")
    if not isinstance(invariants, dict):
        raise AssertionError(f"missing invariants object: {payload}")

    distinct_ok = bool(invariants.get("distinctTriVerts"))
    max_idx_ok = bool(invariants.get("maxTriIndexLessThanVerts"))

    if not distinct_ok:
        raise AssertionError(f"distinctTriVerts check failed: {invariants}")
    if not max_idx_ok:
        raise AssertionError(f"maxTriIndexLessThanVerts check failed: {invariants}")

    return {
        "stage": "voxel_sprite",
        "status": "pass",
        "sprite": sprite_name,
        "distinctTriVerts": distinct_ok,
        "maxTriIndexLessThanVerts": max_idx_ok,
    }


def _run_phase_check(client: BridgeClient, phase: str) -> Dict[str, Any]:
    payload = client.get_json(f"/phase/{phase}")

    if not payload.get("ok", False):
        raise AssertionError(f"phase endpoint failed: {payload}")

    status = _as_str(payload, "status")
    if status not in {"enabled", "disabled"}:
        raise AssertionError(f"phase status invalid: {status}")
    _ = _as_bool(payload, "enabled")
    _ = _as_number(payload, "patchedTypes")

    return {
        "stage": "phase_status",
        "status": "pass",
        "phase": phase,
        "phasePayload": {"status": payload["status"], "enabled": payload["enabled"], "patchedTypes": payload["patchedTypes"]},
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run a BridgeRPC smoke test against WSM3D")
    parser.add_argument("--host", default="127.0.0.1", help="Bridge host")
    parser.add_argument("--port", type=int, default=8766, help="Bridge port")
    parser.add_argument("--timeout", type=float, default=10.0, help="Request timeout in seconds")
    parser.add_argument("--sprite", default="walk_0", help="Sprite name for /voxel/sprite")
    parser.add_argument("--phase", default="VoxelEntities", help="Phase slug for /phase/<slug>")
    parser.add_argument("--json", action="store_true", help="Print full JSON report")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    client = BridgeClient(args.host, args.port, timeout=args.timeout)
    checks = []
    exit_code = 0

    try:
        checks.append(_run_health_check(client))
        checks.append(_run_telemetry_check(client))
        checks.append(_run_sprite_check(client, args.sprite))
        checks.append(_run_phase_check(client, args.phase))
    except AssertionError as exc:
        checks.append(_build_failure("smoke", str(exc)))
        exit_code = 1

    report = {"ok": exit_code == 0, "checks": checks}
    if args.json:
        print(json.dumps(report, indent=2))
    else:
        for check in checks:
            stage = check.get("stage", "unknown")
            status = check.get("status", "pass")
            print(f"{stage}: {status}")
        if exit_code == 0:
            print("smoke test passed")
        else:
            print("smoke test failed")

    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
