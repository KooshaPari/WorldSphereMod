#!/usr/bin/env python3
"""
Visual self-verification for WorldSphereMod3D.

Connects to the in-game bridge HTTP server, captures a screenshot, analyzes
it for basic visual indicators, and queries /diag/render_stats for
programmatic render-pipeline health.  Reports PASS/FAIL with specific
metrics on stdout as JSON.

Requirements:
  pip install pillow requests

Usage:
  python Tools/verify-visual.py                 # defaults: port 8766, 60s timeout
  python Tools/verify-visual.py --port 8767
  python Tools/verify-visual.py --skip-screenshot   # render_stats only
  python Tools/verify-visual.py --output-dir C:/tmp  # save screenshot here
"""

from __future__ import annotations

import argparse
import collections
import json
import os
import sys
import time
from pathlib import Path

try:
    import requests
except ImportError:
    print(json.dumps({"ok": False, "error": "requests library required: pip install requests"}))
    sys.exit(2)


def wait_for_bridge(base: str, timeout: int) -> dict | None:
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


def get_render_stats(base: str) -> dict | None:
    try:
        r = requests.get(f"{base}/diag/render_stats", timeout=10)
        return r.json()
    except Exception:
        return None


def capture_screenshot(base: str, output_path: str | None = None) -> dict:
    params = {}
    if output_path:
        params["path"] = output_path
    try:
        r = requests.post(f"{base}/actions/screenshot", params=params, timeout=15)
        return r.json()
    except Exception as exc:
        return {"ok": False, "error": str(exc)}


def analyze_screenshot(path: str) -> dict:
    try:
        from PIL import Image
    except ImportError:
        return {"ok": False, "error": "Pillow required: pip install pillow"}

    if not os.path.isfile(path):
        return {"ok": False, "error": f"screenshot not found: {path}"}

    img = Image.open(path).convert("RGB")
    pixels = list(img.getdata())
    total = len(pixels)
    if total == 0:
        return {"ok": False, "error": "empty image"}

    # Color histogram: count occurrences of each color
    color_counts: dict[tuple, int] = collections.Counter(pixels)
    unique_colors = len(color_counts)
    most_common_color, most_common_count = color_counts.most_common(1)[0]
    dominant_ratio = most_common_count / total

    # Blank detection: >90% same color = broken/blank
    is_blank = dominant_ratio > 0.90

    # Sky detection heuristic: classify pixels as "sky-like" (very bright,
    # low saturation) vs content. WorldBox sky tends to be light blue or
    # grey. Consider a pixel "sky" if all channels > 180 and spread < 40.
    sky_pixels = 0
    for r, g, b in pixels:
        if min(r, g, b) > 160 and max(r, g, b) - min(r, g, b) < 50:
            sky_pixels += 1
    sky_ratio = sky_pixels / total
    content_ratio = 1.0 - sky_ratio

    # Color variance: sample luminance std dev
    lum_sum = 0.0
    lum_sq_sum = 0.0
    sample_step = max(1, total // 10000)  # sample ~10k pixels for speed
    sample_count = 0
    for i in range(0, total, sample_step):
        r, g, b = pixels[i]
        lum = 0.299 * r + 0.587 * g + 0.114 * b
        lum_sum += lum
        lum_sq_sum += lum * lum
        sample_count += 1

    lum_mean = lum_sum / sample_count if sample_count > 0 else 0
    lum_var = (lum_sq_sum / sample_count - lum_mean * lum_mean) if sample_count > 0 else 0
    lum_stddev = lum_var ** 0.5

    # Non-black pixel ratio: how many pixels are not near-black
    dark_threshold = 15
    dark_pixels = sum(1 for r, g, b in pixels if r < dark_threshold and g < dark_threshold and b < dark_threshold)
    non_black_ratio = 1.0 - (dark_pixels / total)

    return {
        "ok": True,
        "width": img.width,
        "height": img.height,
        "totalPixels": total,
        "uniqueColors": unique_colors,
        "dominantColor": list(most_common_color),
        "dominantRatio": round(dominant_ratio, 4),
        "isBlank": is_blank,
        "skyRatio": round(sky_ratio, 4),
        "contentRatio": round(content_ratio, 4),
        "nonBlackRatio": round(non_black_ratio, 4),
        "luminanceStdDev": round(lum_stddev, 2),
        "sampleCount": sample_count,
    }


def evaluate_render_stats(stats: dict) -> dict:
    checks = {}

    draw_calls = stats.get("drawCalls", 0)
    last_nz = stats.get("lastNonZeroDrawCalls", 0)
    effective_draws = draw_calls if draw_calls > 0 else last_nz
    checks["hasDrawCalls"] = effective_draws > 0

    checks["hasVisibleUnits"] = stats.get("visibleUnits", 0) > 0
    checks["isWorld3D"] = stats.get("isWorld3D", False)
    checks["voxelEntitiesEnabled"] = stats.get("voxelEntitiesEnabled", False)
    checks["emitVoxelsFired"] = stats.get("emitVoxelsFired", False)
    checks["hasMaterial"] = stats.get("materialShaderName") is not None
    checks["hasCamera"] = stats.get("camera") is not None

    passed = sum(1 for v in checks.values() if v)
    total = len(checks)

    return {
        "checks": checks,
        "passed": passed,
        "total": total,
        "allPassed": passed == total,
    }


def evaluate_screenshot(analysis: dict) -> dict:
    if not analysis.get("ok"):
        return {"checks": {}, "passed": 0, "total": 0, "allPassed": False, "error": analysis.get("error")}

    checks = {}
    checks["notBlank"] = not analysis["isBlank"]
    # Vanilla WorldBox has ~100 unique colors, 3D mod should have more.
    # Even a basic 3D scene should produce >50 unique colors.
    checks["colorVariety"] = analysis["uniqueColors"] > 50
    # Content should cover at least 10% of the screen
    checks["hasContent"] = analysis["contentRatio"] > 0.10
    # Luminance stddev: a 3D-rendered scene should have shading variance
    checks["hasLuminanceVariance"] = analysis["luminanceStdDev"] > 15.0
    # Not all-black
    checks["notAllBlack"] = analysis["nonBlackRatio"] > 0.10

    passed = sum(1 for v in checks.values() if v)
    total = len(checks)
    return {
        "checks": checks,
        "passed": passed,
        "total": total,
        "allPassed": passed == total,
    }


def main():
    parser = argparse.ArgumentParser(description="Visual self-verification for WSM3D")
    parser.add_argument("--port", type=int, default=8766, help="Bridge HTTP port (default: 8766)")
    parser.add_argument("--timeout", type=int, default=60, help="Max seconds to wait for bridge (default: 60)")
    parser.add_argument("--skip-screenshot", action="store_true", help="Only check render_stats, skip screenshot capture")
    parser.add_argument("--output-dir", type=str, default=None, help="Directory to save screenshot (default: game's docs/journeys/scratch)")
    parser.add_argument("--screenshot-path", type=str, default=None, help="Explicit screenshot output path")
    args = parser.parse_args()

    base = f"http://127.0.0.1:{args.port}"
    result: dict = {
        "bridgePort": args.port,
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }

    # Step 1: Wait for bridge
    health = wait_for_bridge(base, args.timeout)
    if health is None:
        result["ok"] = False
        result["verdict"] = "FAIL"
        result["error"] = f"Bridge not reachable on port {args.port} after {args.timeout}s"
        print(json.dumps(result, indent=2))
        sys.exit(1)

    result["health"] = health

    # Step 2: Get render stats
    stats = get_render_stats(base)
    if stats is None:
        result["ok"] = False
        result["verdict"] = "FAIL"
        result["error"] = "Failed to fetch /diag/render_stats"
        print(json.dumps(result, indent=2))
        sys.exit(1)

    result["renderStats"] = stats
    stats_eval = evaluate_render_stats(stats)
    result["renderStatsEval"] = stats_eval

    # Step 3: Screenshot (unless skipped)
    if args.skip_screenshot:
        result["screenshotSkipped"] = True
        overall_pass = stats_eval["allPassed"]
    else:
        out_path = args.screenshot_path
        if not out_path and args.output_dir:
            os.makedirs(args.output_dir, exist_ok=True)
            out_path = os.path.join(args.output_dir, f"verify-visual-{int(time.time())}.png")

        cap = capture_screenshot(base, out_path)
        result["screenshotCapture"] = cap

        if not cap.get("ok"):
            result["screenshotAnalysis"] = {"ok": False, "error": cap.get("error", "capture failed")}
            result["screenshotEval"] = {"checks": {}, "passed": 0, "total": 0, "allPassed": False}
            overall_pass = False
        else:
            saved_path = cap.get("path", "")
            # Wait briefly for Unity to finish writing the file
            for _ in range(10):
                if os.path.isfile(saved_path) and os.path.getsize(saved_path) > 0:
                    break
                time.sleep(0.5)

            analysis = analyze_screenshot(saved_path)
            result["screenshotAnalysis"] = analysis
            ss_eval = evaluate_screenshot(analysis)
            result["screenshotEval"] = ss_eval
            overall_pass = stats_eval["allPassed"] and ss_eval["allPassed"]

    result["ok"] = overall_pass
    result["verdict"] = "PASS" if overall_pass else "FAIL"

    # Summary line for quick scanning
    stats_summary = f"draws={stats.get('drawCalls', 0)} inst={stats.get('instances', 0)} units={stats.get('visibleUnits', 0)} 3d={stats.get('isWorld3D', False)}"
    if not args.skip_screenshot and result.get("screenshotAnalysis", {}).get("ok"):
        sa = result["screenshotAnalysis"]
        stats_summary += f" colors={sa['uniqueColors']} blank={sa['isBlank']} content={sa['contentRatio']:.1%} lumSD={sa['luminanceStdDev']:.1f}"
    result["summary"] = stats_summary

    print(json.dumps(result, indent=2))
    sys.exit(0 if overall_pass else 1)


if __name__ == "__main__":
    main()
