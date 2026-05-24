#!/usr/bin/env python3
"""Compare two PNGs with luminance SSIM; emit JSON on stdout."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def ssim_luminance(expected_path: Path, actual_path: Path) -> float:
    try:
        from PIL import Image
    except ImportError as exc:
        raise RuntimeError("Pillow is required. Install with `pip install pillow`.") from exc

    expected = Image.open(expected_path).convert("L")
    actual = Image.open(actual_path).convert("L")
    if actual.size != expected.size:
        actual = actual.resize(expected.size, Image.Resampling.LANCZOS)

    pixels_a = list(expected.getdata())
    pixels_b = list(actual.getdata())
    count = len(pixels_a)
    if count == 0:
        return 0.0

    c1 = (0.01 * 255) ** 2
    c2 = (0.03 * 255) ** 2

    mean_a = sum(pixels_a) / count
    mean_b = sum(pixels_b) / count
    var_a = sum((value - mean_a) ** 2 for value in pixels_a) / count
    var_b = sum((value - mean_b) ** 2 for value in pixels_b) / count
    cov = sum((pixels_a[i] - mean_a) * (pixels_b[i] - mean_b) for i in range(count)) / count

    numerator = (2 * mean_a * mean_b + c1) * (2 * cov + c2)
    denominator = (mean_a**2 + mean_b**2 + c1) * (var_a + var_b + c2)
    if denominator == 0:
        return 0.0
    return numerator / denominator


def main() -> int:
    parser = argparse.ArgumentParser(description="SSIM-compare two PNG fixtures")
    parser.add_argument("--expected", required=True, help="Canonical/reference PNG")
    parser.add_argument("--actual", required=True, help="Captured PNG to evaluate")
    parser.add_argument("--threshold", type=float, default=0.95, help="Pass threshold (default 0.95)")
    args = parser.parse_args()

    expected = Path(args.expected)
    actual = Path(args.actual)
    if not expected.is_file():
        print(json.dumps({"ok": False, "error": f"expected missing: {expected}"}))
        return 2
    if not actual.is_file():
        print(json.dumps({"ok": False, "error": f"actual missing: {actual}"}))
        return 2

    score = float(ssim_luminance(expected, actual))
    passed = score >= args.threshold
    payload = {
        "ok": passed,
        "ssim": round(score, 6),
        "threshold": args.threshold,
        "expected": str(expected),
        "actual": str(actual),
    }
    print(json.dumps(payload))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
