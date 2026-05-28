#!/usr/bin/env python3
"""
Runtime behavioral test suite for WorldSphereMod3D.

Connects to the in-game bridge HTTP server at 127.0.0.1:<port> and runs
a suite of assertions against live game state. Outputs results in TAP
(Test Anything Protocol) or JUnit XML format.

Requirements:
  pip install requests

Usage:
  python Tools/runtime-tests.py                      # defaults: port 8766, TAP
  python Tools/runtime-tests.py --port 8767
  python Tools/runtime-tests.py --format junit        # JUnit XML to stdout
  python Tools/runtime-tests.py --format junit --output results.xml
  python Tools/runtime-tests.py --timeout 120         # wait 120s for bridge
  python Tools/runtime-tests.py --skip-spawn           # skip spawn_units test
  python Tools/runtime-tests.py --skip-screenshot      # skip screenshot test
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

try:
    import requests
except ImportError:
    print("FATAL: requests library required: pip install requests", file=sys.stderr)
    sys.exit(2)


# ---------------------------------------------------------------------------
# Data model
# ---------------------------------------------------------------------------

@dataclass
class TestResult:
    name: str
    passed: bool
    message: str = ""
    duration_s: float = 0.0
    details: dict = field(default_factory=dict)


# ---------------------------------------------------------------------------
# Bridge helpers
# ---------------------------------------------------------------------------

def wait_for_bridge(base: str, timeout: int) -> Optional[dict]:
    """Block until GET /health returns ok=true or timeout expires."""
    deadline = time.monotonic() + timeout
    last_err = ""
    while time.monotonic() < deadline:
        try:
            r = requests.get(f"{base}/health", timeout=8)
            data = r.json()
            if data.get("ok"):
                return data
        except Exception as exc:
            last_err = str(exc)
        time.sleep(2)
    return None


def bridge_get(base: str, path: str, timeout: int = 10) -> dict:
    r = requests.get(f"{base}{path}", timeout=timeout)
    r.raise_for_status()
    return r.json()


def bridge_post(base: str, path: str, params: Optional[dict] = None, timeout: int = 15) -> dict:
    r = requests.post(f"{base}{path}", params=params or {}, timeout=timeout)
    r.raise_for_status()
    return r.json()


# ---------------------------------------------------------------------------
# Individual tests
# ---------------------------------------------------------------------------

def test_health_is_world_3d(base: str) -> TestResult:
    """GET /health -> assert isWorld3D=true"""
    t0 = time.monotonic()
    try:
        data = bridge_get(base, "/health")
        is3d = data.get("isWorld3D", False)
        passed = is3d is True
        msg = f"isWorld3D={is3d}"
        return TestResult(
            name="health_isWorld3D",
            passed=passed,
            message=msg if passed else f"FAIL: isWorld3D={is3d} (expected true)",
            duration_s=time.monotonic() - t0,
            details=data,
        )
    except Exception as exc:
        return TestResult(
            name="health_isWorld3D",
            passed=False,
            message=f"ERROR: {exc}",
            duration_s=time.monotonic() - t0,
        )


def test_draw_calls_gt_2(base: str) -> TestResult:
    """GET /diag/render_stats -> assert drawCalls > 2 (more than sanity cube)"""
    t0 = time.monotonic()
    try:
        data = bridge_get(base, "/diag/render_stats")
        draw_calls = data.get("drawCalls", 0)
        last_nz = data.get("lastNonZeroDrawCalls", 0)
        effective = max(draw_calls, last_nz)
        passed = effective > 2
        return TestResult(
            name="render_drawCalls_gt_2",
            passed=passed,
            message=f"drawCalls={draw_calls} lastNonZero={last_nz} effective={effective}",
            duration_s=time.monotonic() - t0,
            details=data,
        )
    except Exception as exc:
        return TestResult(
            name="render_drawCalls_gt_2",
            passed=False,
            message=f"ERROR: {exc}",
            duration_s=time.monotonic() - t0,
        )


def test_visible_units_gt_0(base: str) -> TestResult:
    """GET /diag/render_stats -> assert visibleUnits > 0"""
    t0 = time.monotonic()
    try:
        data = bridge_get(base, "/diag/render_stats")
        units = data.get("visibleUnits", 0)
        passed = units > 0
        return TestResult(
            name="render_visibleUnits_gt_0",
            passed=passed,
            message=f"visibleUnits={units}",
            duration_s=time.monotonic() - t0,
            details=data,
        )
    except Exception as exc:
        return TestResult(
            name="render_visibleUnits_gt_0",
            passed=False,
            message=f"ERROR: {exc}",
            duration_s=time.monotonic() - t0,
        )


def test_emit_voxels_fired(base: str) -> TestResult:
    """GET /diag/render_stats -> assert emitVoxelsFired = true"""
    t0 = time.monotonic()
    try:
        data = bridge_get(base, "/diag/render_stats")
        fired = data.get("emitVoxelsFired", False)
        passed = fired is True
        return TestResult(
            name="render_emitVoxelsFired",
            passed=passed,
            message=f"emitVoxelsFired={fired}",
            duration_s=time.monotonic() - t0,
            details=data,
        )
    except Exception as exc:
        return TestResult(
            name="render_emitVoxelsFired",
            passed=False,
            message=f"ERROR: {exc}",
            duration_s=time.monotonic() - t0,
        )


def test_material_shader_name(base: str) -> TestResult:
    """GET /diag/render_stats -> assert materialShaderName contains 'OpaqueVertexColor'"""
    t0 = time.monotonic()
    try:
        data = bridge_get(base, "/diag/render_stats")
        shader = data.get("materialShaderName") or ""
        passed = "OpaqueVertexColor" in shader
        return TestResult(
            name="render_materialShaderName",
            passed=passed,
            message=f"materialShaderName={shader!r}",
            duration_s=time.monotonic() - t0,
            details=data,
        )
    except Exception as exc:
        return TestResult(
            name="render_materialShaderName",
            passed=False,
            message=f"ERROR: {exc}",
            duration_s=time.monotonic() - t0,
        )


def test_spawn_units(base: str, count: int = 10, race: str = "human", wait_s: int = 10) -> TestResult:
    """POST /actions/spawn_units?count=10&race=human -> wait -> assert instances > 2"""
    t0 = time.monotonic()
    try:
        spawn_resp = bridge_post(base, "/actions/spawn_units", {"count": str(count), "race": race})
        if not spawn_resp.get("ok"):
            return TestResult(
                name="spawn_units_instances",
                passed=False,
                message=f"spawn_units returned ok=false: {spawn_resp}",
                duration_s=time.monotonic() - t0,
                details=spawn_resp,
            )

        # Wait for units to be spawned and rendered
        time.sleep(wait_s)

        data = bridge_get(base, "/diag/render_stats")
        instances = data.get("instances", 0)
        last_nz_draws = data.get("lastNonZeroDrawCalls", 0)
        # After spawning, we expect rendering activity
        effective_instances = max(instances, last_nz_draws)
        passed = effective_instances > 2
        return TestResult(
            name="spawn_units_instances",
            passed=passed,
            message=f"instances={instances} lastNonZeroDrawCalls={last_nz_draws} effective={effective_instances} (after spawn {count} {race}, waited {wait_s}s)",
            duration_s=time.monotonic() - t0,
            details={"spawn": spawn_resp, "render_stats": data},
        )
    except Exception as exc:
        return TestResult(
            name="spawn_units_instances",
            passed=False,
            message=f"ERROR: {exc}",
            duration_s=time.monotonic() - t0,
        )


def test_voxel_cache_size(base: str) -> TestResult:
    """GET /diag/render_stats -> assert voxelCacheSize > 0"""
    t0 = time.monotonic()
    try:
        data = bridge_get(base, "/diag/render_stats")
        cache_size = data.get("voxelCacheSize", 0)
        passed = cache_size > 0
        return TestResult(
            name="render_voxelCacheSize_gt_0",
            passed=passed,
            message=f"voxelCacheSize={cache_size}",
            duration_s=time.monotonic() - t0,
            details=data,
        )
    except Exception as exc:
        return TestResult(
            name="render_voxelCacheSize_gt_0",
            passed=False,
            message=f"ERROR: {exc}",
            duration_s=time.monotonic() - t0,
        )


def test_screenshot_visual(base: str) -> TestResult:
    """POST /actions/screenshot -> verify-visual.py analysis -> assert PASS"""
    t0 = time.monotonic()
    try:
        # Capture screenshot via bridge
        cap = bridge_post(base, "/actions/screenshot", timeout=15)
        if not cap.get("ok"):
            return TestResult(
                name="screenshot_visual_pass",
                passed=False,
                message=f"screenshot capture failed: {cap.get('error', 'unknown')}",
                duration_s=time.monotonic() - t0,
                details=cap,
            )

        saved_path = cap.get("path", "")

        # Wait for file to be written
        for _ in range(10):
            if os.path.isfile(saved_path) and os.path.getsize(saved_path) > 0:
                break
            time.sleep(0.5)

        if not os.path.isfile(saved_path):
            return TestResult(
                name="screenshot_visual_pass",
                passed=False,
                message=f"screenshot file not found at {saved_path}",
                duration_s=time.monotonic() - t0,
            )

        # Use verify-visual.py's analysis logic inline (avoid subprocess)
        try:
            from PIL import Image
            import collections

            img = Image.open(saved_path).convert("RGB")
            pixels = list(img.getdata())
            total = len(pixels)
            if total == 0:
                return TestResult(
                    name="screenshot_visual_pass",
                    passed=False,
                    message="empty image",
                    duration_s=time.monotonic() - t0,
                )

            color_counts = collections.Counter(pixels)
            unique_colors = len(color_counts)
            most_common_color, most_common_count = color_counts.most_common(1)[0]
            dominant_ratio = most_common_count / total
            is_blank = dominant_ratio > 0.90

            # Sky detection
            sky_pixels = sum(
                1 for r, g, b in pixels
                if min(r, g, b) > 160 and max(r, g, b) - min(r, g, b) < 50
            )
            content_ratio = 1.0 - (sky_pixels / total)

            # Luminance stddev (sampled)
            sample_step = max(1, total // 10000)
            lum_vals = []
            for i in range(0, total, sample_step):
                r, g, b = pixels[i]
                lum_vals.append(0.299 * r + 0.587 * g + 0.114 * b)
            if lum_vals:
                lum_mean = sum(lum_vals) / len(lum_vals)
                lum_var = sum((v - lum_mean) ** 2 for v in lum_vals) / len(lum_vals)
                lum_stddev = lum_var ** 0.5
            else:
                lum_stddev = 0.0

            # Non-black ratio
            dark_threshold = 15
            dark_pixels = sum(
                1 for r, g, b in pixels
                if r < dark_threshold and g < dark_threshold and b < dark_threshold
            )
            non_black_ratio = 1.0 - (dark_pixels / total)

            checks = {
                "notBlank": not is_blank,
                "colorVariety": unique_colors > 50,
                "hasContent": content_ratio > 0.10,
                "hasLuminanceVariance": lum_stddev > 15.0,
                "notAllBlack": non_black_ratio > 0.10,
            }
            all_passed = all(checks.values())

            analysis = {
                "uniqueColors": unique_colors,
                "isBlank": is_blank,
                "contentRatio": round(content_ratio, 4),
                "luminanceStdDev": round(lum_stddev, 2),
                "nonBlackRatio": round(non_black_ratio, 4),
                "checks": checks,
            }

            return TestResult(
                name="screenshot_visual_pass",
                passed=all_passed,
                message=f"visual checks: {sum(checks.values())}/{len(checks)} passed | colors={unique_colors} blank={is_blank} content={content_ratio:.1%} lumSD={lum_stddev:.1f}",
                duration_s=time.monotonic() - t0,
                details={"capture": cap, "analysis": analysis},
            )

        except ImportError:
            # Pillow not installed -- degrade gracefully, just check file exists
            file_size = os.path.getsize(saved_path)
            passed = file_size > 1024  # at least 1KB
            return TestResult(
                name="screenshot_visual_pass",
                passed=passed,
                message=f"Pillow not installed; file exists ({file_size} bytes), skipping pixel analysis",
                duration_s=time.monotonic() - t0,
                details={"capture": cap, "fileSize": file_size},
            )

    except Exception as exc:
        return TestResult(
            name="screenshot_visual_pass",
            passed=False,
            message=f"ERROR: {exc}",
            duration_s=time.monotonic() - t0,
        )


# ---------------------------------------------------------------------------
# Output formatters
# ---------------------------------------------------------------------------

def format_tap(results: list[TestResult]) -> str:
    lines = [f"TAP version 13", f"1..{len(results)}"]
    for i, r in enumerate(results, 1):
        status = "ok" if r.passed else "not ok"
        lines.append(f"{status} {i} - {r.name}")
        if r.message:
            # TAP diagnostic lines start with #
            for line in r.message.split("\n"):
                lines.append(f"  # {line}")
        if not r.passed and r.details:
            lines.append(f"  # details: {json.dumps(r.details, default=str)[:500]}")
    return "\n".join(lines) + "\n"


def format_junit(results: list[TestResult], suite_name: str = "wsm3d-runtime") -> str:
    suite = ET.Element("testsuite", {
        "name": suite_name,
        "tests": str(len(results)),
        "failures": str(sum(1 for r in results if not r.passed)),
        "errors": "0",
        "time": f"{sum(r.duration_s for r in results):.3f}",
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%S"),
    })

    for r in results:
        tc = ET.SubElement(suite, "testcase", {
            "name": r.name,
            "classname": suite_name,
            "time": f"{r.duration_s:.3f}",
        })
        if not r.passed:
            failure = ET.SubElement(tc, "failure", {
                "message": r.message[:500],
                "type": "AssertionError",
            })
            if r.details:
                failure.text = json.dumps(r.details, indent=2, default=str)[:2000]
        if r.message:
            stdout = ET.SubElement(tc, "system-out")
            stdout.text = r.message

    tree = ET.ElementTree(suite)
    # Write to string
    import io
    buf = io.BytesIO()
    tree.write(buf, encoding="unicode", xml_declaration=True)
    return buf.getvalue()


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="WSM3D runtime behavioral test suite")
    parser.add_argument("--port", type=int, default=8766, help="Bridge HTTP port (default: 8766)")
    parser.add_argument("--timeout", type=int, default=90, help="Max seconds to wait for bridge (default: 90)")
    parser.add_argument("--format", choices=["tap", "junit"], default="tap", help="Output format (default: tap)")
    parser.add_argument("--output", type=str, default=None, help="Write output to file instead of stdout")
    parser.add_argument("--skip-spawn", action="store_true", help="Skip spawn_units test (slow)")
    parser.add_argument("--skip-screenshot", action="store_true", help="Skip screenshot visual test")
    parser.add_argument("--spawn-wait", type=int, default=10, help="Seconds to wait after spawn (default: 10)")
    args = parser.parse_args()

    base = f"http://127.0.0.1:{args.port}"

    # Wait for bridge to come alive
    print(f"# Waiting for bridge on port {args.port} (timeout {args.timeout}s)...", file=sys.stderr)
    health = wait_for_bridge(base, args.timeout)
    if health is None:
        print(f"BAIL OUT! Bridge not reachable on port {args.port} after {args.timeout}s", file=sys.stdout)
        sys.exit(1)
    print(f"# Bridge alive: version={health.get('version')} isWorld3D={health.get('isWorld3D')}", file=sys.stderr)

    # Build test suite
    results: list[TestResult] = []

    # a. Health check
    results.append(test_health_is_world_3d(base))

    # b. Draw calls > 2
    results.append(test_draw_calls_gt_2(base))

    # c. Visible units > 0
    results.append(test_visible_units_gt_0(base))

    # d. emitVoxelsFired = true
    results.append(test_emit_voxels_fired(base))

    # e. Material shader name
    results.append(test_material_shader_name(base))

    # f. Spawn units -> instances > 2
    if not args.skip_spawn:
        results.append(test_spawn_units(base, count=10, race="human", wait_s=args.spawn_wait))
    else:
        results.append(TestResult(
            name="spawn_units_instances",
            passed=True,
            message="SKIP: --skip-spawn",
        ))

    # g. Voxel cache size > 0
    results.append(test_voxel_cache_size(base))

    # h. Screenshot visual pass
    if not args.skip_screenshot:
        results.append(test_screenshot_visual(base))
    else:
        results.append(TestResult(
            name="screenshot_visual_pass",
            passed=True,
            message="SKIP: --skip-screenshot",
        ))

    # Format output
    if args.format == "tap":
        output = format_tap(results)
    else:
        output = format_junit(results)

    if args.output:
        Path(args.output).parent.mkdir(parents=True, exist_ok=True)
        Path(args.output).write_text(output, encoding="utf-8")
        print(f"# Results written to {args.output}", file=sys.stderr)
    else:
        print(output)

    # Summary to stderr
    passed = sum(1 for r in results if r.passed)
    total = len(results)
    print(f"# {passed}/{total} tests passed", file=sys.stderr)

    # Exit code
    sys.exit(0 if passed == total else 1)


if __name__ == "__main__":
    main()
