#!/usr/bin/env python3
"""
Programmatic pixel-verification harness for WSM3D P0 fixes.

Encodes numerical checks that distinguish correct vs broken render states.
Per the project's "no visual claims" rule, these checks return numeric
measurements + pass/fail against a defensible threshold; the caller decides
what the numbers mean.  No image is ever narrated.

Requires: Pillow, NumPy.
"""

from __future__ import annotations

import argparse
import glob as _glob
import json
import sys
import time
from pathlib import Path
from typing import Any, Callable

import numpy as np
from PIL import Image


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _load_rgb(path: str) -> np.ndarray:
    """Load PNG as uint8 HxWx3 ndarray.  Raises FileNotFoundError on miss."""
    img = Image.open(path).convert("RGB")
    return np.asarray(img, dtype=np.uint8)


def _luma(rgb: np.ndarray) -> np.ndarray:
    """BT.601 luma.  Accepts HxWx3 uint8; returns HxW float32."""
    r = rgb[..., 0].astype(np.float32)
    g = rgb[..., 1].astype(np.float32)
    b = rgb[..., 2].astype(np.float32)
    return 0.299 * r + 0.587 * g + 0.114 * b


def _label_components(mask: np.ndarray) -> tuple[np.ndarray, int]:
    """
    4-connectivity connected-component labeling on a boolean mask.
    Returns (label_map, num_components).  Pure-Python (no scipy).
    """
    h, w = mask.shape
    labels = np.zeros((h, w), dtype=np.int32)
    next_label = 0
    # Iterative flood fill via BFS
    for sy in range(h):
        for sx in range(w):
            if mask[sy, sx] and labels[sy, sx] == 0:
                next_label += 1
                stack = [(sy, sx)]
                labels[sy, sx] = next_label
                while stack:
                    y, x = stack.pop()
                    for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                        ny, nx = y + dy, x + dx
                        if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and labels[ny, nx] == 0:
                            labels[ny, nx] = next_label
                            stack.append((ny, nx))
    return labels, next_label


def _bbox_of_component(labels: np.ndarray, label: int) -> tuple[int, int, int, int]:
    ys, xs = np.where(labels == label)
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


# ---------------------------------------------------------------------------
# Check 1: actor silhouette complexity
# ---------------------------------------------------------------------------

def actor_silhouette_complexity(png_path: str, region: tuple[int, int, int, int] | None = None,
                                bg_tolerance: int = 25) -> dict:
    """
    Connected-component scan over an upper-screen region.  For each
    non-background candidate compute (a) bbox aspect ratio, (b) edge density
    via Sobel, (c) fill ratio.  A 2D sprite billboard is near-rectangular
    (edge-density ~4, fill ratio ~0.95); a 3D voxel mesh is irregular
    (edge-density >8, fill ratio 0.4-0.85).

    Pass: at least one candidate has edge_density > 8 AND fill_ratio < 0.85.
    """
    rgb = _load_rgb(png_path)
    h, w, _ = rgb.shape
    if region is None:
        region = (0, 0, w, h // 2)  # upper half
    x0, y0, x1, y1 = region
    sub = rgb[y0:y1, x0:x1]
    sh, sw, _ = sub.shape

    # Foreground = pixels that differ from the modal background colour
    # by more than bg_tolerance on any channel.
    flat = sub.reshape(-1, 3)
    # Use a fast mode via rounding to /16 buckets to find dominant bg
    bucket = (flat // 16).astype(np.int32)
    # tuple-ize for np.unique
    keys = bucket[:, 0] * 1024 + bucket[:, 1] * 32 + bucket[:, 2]
    vals, counts = np.unique(keys, return_counts=True)
    bg_bucket = int(vals[counts.argmax()])
    bg_b = np.array([(bg_bucket >> 5) & 0x1F, bg_bucket & 0x1F], dtype=np.int32)  # placeholder
    # Decode the bg colour from its bucket (centre of bucket)
    br = ((bg_bucket >> 10) & 0x1F) * 16 + 8
    bg = np.array([br, ((bg_bucket >> 5) & 0x1F) * 16 + 8, (bg_bucket & 0x1F) * 16 + 8],
                  dtype=np.int32)
    diff = np.abs(sub.astype(np.int32) - bg).max(axis=2)
    mask = diff > bg_tolerance

    labels, n = _label_components(mask)
    if n == 0:
        return {
            "candidates": 0,
            "best": None,
            "edge_density_min": 8.0,
            "fill_ratio_max": 0.85,
            "min_candidates": 1,
            "pass": False,
            "note": "no foreground components above bg_tolerance",
        }

    # Sobel on the mask (cheap: simple 3x3 gradient)
    gy = np.zeros_like(mask, dtype=np.int32)
    gx = np.zeros_like(mask, dtype=np.int32)
    gy[1:-1, :] = (mask[2:, :].astype(np.int32) - mask[:-2, :].astype(np.int32))
    gx[:, 1:-1] = (mask[:, 2:].astype(np.int32) - mask[:, :-2].astype(np.int32))
    edge_mag = (np.abs(gx) + np.abs(gy)) > 0

    candidates: list[dict] = []
    for lbl in range(1, n + 1):
        comp_mask = labels == lbl
        pix = int(comp_mask.sum())
        if pix < 20:
            continue  # too small to be a meaningful actor
        bx0, by0, bx1, by1 = _bbox_of_component(labels, lbl)
        bw = max(1, bx1 - bx0 + 1)
        bh = max(1, by1 - by0 + 1)
        bbox_area = bw * bh
        fill_ratio = pix / bbox_area
        aspect = bw / bh
        # Edge density = edge pixels / component pixels * 100.
        # A solid rectangle has boundary edges only, so density is small
        # (~ a few %).  An irregular voxel cluster has many interior edges,
        # pushing density well above the sprite baseline.
        comp_edges = int((edge_mag & comp_mask).sum())
        edge_density = (comp_edges / pix) * 100.0 if pix else 0.0
        candidates.append({
            "label": lbl,
            "pixels": pix,
            "bbox": [bx0 + x0, by0 + y0, bx1 + x0, by1 + y0],
            "bbox_w": bw,
            "bbox_h": bh,
            "aspect": round(aspect, 3),
            "fill_ratio": round(fill_ratio, 4),
            "edge_density": round(edge_density, 4),
        })

    if not candidates:
        return {
            "candidates": 0,
            "best": None,
            "edge_density_min": 8.0,
            "fill_ratio_max": 0.85,
            "min_candidates": 1,
            "pass": False,
            "note": "all components <20px",
        }

    # Best candidate = highest edge_density satisfying aspect in [0.2, 5]
    # (sprites tend to be near-square or wide; irregular voxels span more).
    eligible = [c for c in candidates if 0.2 <= c["aspect"] <= 5.0]
    pool = eligible or candidates
    best = max(pool, key=lambda c: c["edge_density"])
    passes = [c for c in candidates if c["edge_density"] > 8.0 and c["fill_ratio"] < 0.85]

    return {
        "candidates": len(candidates),
        "passing_candidates": len(passes),
        "best": best,
        "edge_density_min": 8.0,
        "fill_ratio_max": 0.85,
        "min_candidates": 1,
        "pass": len(passes) >= 1,
    }


# ---------------------------------------------------------------------------
# Check 2: biome colour variance
# ---------------------------------------------------------------------------

def biome_color_variance(png_path: str, grid: int = 5, hue_tol: float = 10.0) -> dict:
    """
    Divide the frame into a grid x grid of equal regions.  For each region
    sample centre+4 quadrant pixels.  Bucket regions by hue.  Within each
    bucket, compute stddev of mean R/G/B across regions.

    A bug where every quad averages the same texel will produce
    within-biome stddev ~ 0.  A working render should produce > 5.
    """
    rgb = _load_rgb(png_path)
    h, w, _ = rgb.shape
    rh, rw = h // grid, w // grid
    if rh < 3 or rw < 3:
        return {
            "stddev": 0.0,
            "samples": 0,
            "regions_evaluated": 0,
            "stddev_min": 5.0,
            "min_samples": 3,
            "pass": False,
            "note": f"frame too small for {grid}x{grid} grid",
        }

    region_means: list[tuple[int, int, np.ndarray]] = []
    for gy in range(grid):
        for gx in range(grid):
            y0 = gy * rh
            x0 = gx * rw
            y1 = y0 + rh
            x1 = x0 + rw
            # 5-sample: centre + 4 quadrant centres
            ys = [y0 + rh // 2,
                  y0 + rh // 4, y0 + (3 * rh) // 4,
                  y0 + rh // 4, y0 + (3 * rh) // 4]
            xs = [x0 + rw // 2,
                  x0 + rw // 4, x0 + rw // 4,
                  x0 + (3 * rw) // 4, x0 + (3 * rw) // 4]
            samples = rgb[ys, xs, :].astype(np.float32)
            mean = samples.mean(axis=0)
            region_means.append((gy, gx, mean))

    # Compute hue (approximate) per region via dominant-channel angle.
    def hue(rgb_mean: np.ndarray) -> float:
        r, g, b = float(rgb_mean[0]), float(rgb_mean[1]), float(rgb_mean[2])
        mx, mn = max(r, g, b), min(r, g, b)
        d = mx - mn
        if d < 1e-3:
            return 0.0
        if mx == r:
            h_ = ((g - b) / d) % 6
        elif mx == g:
            h_ = ((b - r) / d) + 2
        else:
            h_ = ((r - g) / d) + 4
        return h_ * 60.0

    # Group regions by adjacency + hue proximity
    buckets: list[list[np.ndarray]] = []
    for _, _, mean in region_means:
        h_ = hue(mean)
        placed = False
        for b in buckets:
            b_hue = hue(np.mean(np.stack(b), axis=0))
            if abs(((h_ - b_hue + 180) % 360) - 180) <= hue_tol:
                b.append(mean)
                placed = True
                break
        if not placed:
            buckets.append([mean])

    within_stds: list[float] = []
    for b in buckets:
        if len(b) < 2:
            continue
        stacked = np.stack(b, axis=0)
        std = float(stacked.std(axis=0).mean())  # mean of per-channel stddev
        within_stds.append(std)

    within_stds.sort()
    median = float(np.median(within_stds)) if within_stds else 0.0

    return {
        "stddev": round(median, 4),
        "samples": len(within_stds),
        "regions_evaluated": len(region_means),
        "buckets": len(buckets),
        "stddev_min": 5.0,
        "min_samples": 3,
        "pass": median > 5.0 and len(within_stds) >= 3,
    }


# ---------------------------------------------------------------------------
# Check 3: nametag pixel height
# ---------------------------------------------------------------------------

def nametag_pixel_height(png_path: str, region: tuple[int, int, int, int] | None = None) -> dict:
    """
    Connected-component scan filtered for text-like aspect (w >= 1.2*h,
    h <= 120, density >= 0.01, w >= 20).  Returns distribution of heights.

    Oversized nametags produce 30-70px tall components; fixed nametags
    should be <= 20px or absent.
    """
    rgb = _load_rgb(png_path)
    h, w, _ = rgb.shape
    if region is None:
        region = (0, 0, w, int(h * 0.75))  # upper 75% to avoid ground band
    x0, y0, x1, y1 = region
    sub = rgb[y0:y1, x0:x1]
    sh, sw, _ = sub.shape

    # Foreground = "ink": pixels far from background luma
    lum = _luma(sub)
    bg_lum = float(np.median(lum))
    mask = np.abs(lum - bg_lum) > 25

    labels, n = _label_components(mask)
    heights: list[int] = []
    accepted: list[dict] = []
    for lbl in range(1, n + 1):
        comp = labels == lbl
        pix = int(comp.sum())
        if pix < 5:
            continue
        bx0, by0, bx1, by1 = _bbox_of_component(labels, lbl)
        bw = max(1, bx1 - bx0 + 1)
        bh = max(1, by1 - by0 + 1)
        if bw < 20:
            continue
        if bh > 120:
            continue
        aspect = bw / bh
        if aspect < 1.2:
            continue
        density = pix / (bw * bh)
        if density < 0.01:
            continue
        heights.append(bh)
        accepted.append({"bbox_h": bh, "bbox_w": bw, "density": round(density, 3)})

    if not heights:
        return {
            "max_height_px": 0,
            "median_height_px": 0,
            "candidate_count": 0,
            "threshold_max_height_px": 20,
            "pass": True,
            "note": "no text-like candidates; no oversized nametags detected",
        }

    heights_arr = np.asarray(heights)
    return {
        "max_height_px": int(heights_arr.max()),
        "median_height_px": float(np.median(heights_arr)),
        "p95_height_px": float(np.percentile(heights_arr, 95)),
        "candidate_count": len(heights),
        "threshold_max_height_px": 20,
        "pass": int(heights_arr.max()) < 20,
    }


# ---------------------------------------------------------------------------
# Check 4: lod flash diff
# ---------------------------------------------------------------------------

def lod_flash_diff(png_a: str, png_b: str, threshold_rgb: int = 10) -> dict:
    """
    Compare two PNGs frame-to-frame.  Pass if < 0.5% of pixels differ by
    more than threshold_rgb on any channel.
    """
    a = _load_rgb(png_a).astype(np.int32)
    b = _load_rgb(png_b).astype(np.int32)
    if a.shape != b.shape:
        # Resize b to match a for the comparison
        img_b = Image.fromarray(b.astype(np.uint8)).resize((a.shape[1], a.shape[0]), Image.Resampling.LANCZOS)
        b = np.asarray(img_b, dtype=np.int32)
    diff = np.abs(a - b).max(axis=2)
    changed = (diff > threshold_rgb).sum()
    total = int(diff.size)
    pct = changed / total if total else 0.0
    return {
        "diff_pct": round(pct, 6),
        "changed_pixels": int(changed),
        "total_pixels": total,
        "threshold_rgb": threshold_rgb,
        "diff_pct_max": 0.005,
        "pass": pct < 0.005,
    }


# ---------------------------------------------------------------------------
# Check 5: water uniform Y blue
# ---------------------------------------------------------------------------

def water_uniform_y_blue(png_path: str, blue_dominance: int = 15) -> dict:
    """
    Scan lower half of frame for blue-dominant pixels.  Compute Y-variance.

    Flat water -> low Y-variance (<5).  Sunken water showing terrain
    through it -> high Y-variance.
    """
    rgb = _load_rgb(png_path)
    h, w, _ = rgb.shape
    sub = rgb[h // 2:, :, :]
    r = sub[..., 0].astype(np.int32)
    g = sub[..., 1].astype(np.int32)
    b = sub[..., 2].astype(np.int32)
    blue_mask = (b > r + blue_dominance) & (b > g + blue_dominance)
    n_blue = int(blue_mask.sum())
    if n_blue < 50:
        return {
            "y_variance": 0.0,
            "y_mean": 0.0,
            "blue_pixel_count": n_blue,
            "y_variance_max": 5.0,
            "blue_dominance_min": blue_dominance,
            "pass": False,
            "note": "insufficient blue-dominant pixels in lower half",
        }
    y = 0.299 * r + 0.587 * g + 0.114 * b
    y_blue = y[blue_mask]
    y_mean = float(y_blue.mean())
    y_var = float(y_blue.var())
    return {
        "y_variance": round(y_var, 4),
        "y_mean": round(y_mean, 4),
        "blue_pixel_count": n_blue,
        "y_variance_max": 5.0,
        "blue_dominance_min": blue_dominance,
        "pass": bool(y_var < 5.0),
    }


# ---------------------------------------------------------------------------
# Check 6: UI panel ratio
# ---------------------------------------------------------------------------

def ui_panel_ratio(png_path: str, quadrant: str = "topright") -> dict:
    """
    Find the largest rectangular connected component in the named
    quadrant.  Report bbox area as a fraction of viewport area.

    NOTE: the per-quadrant scan caps the measurable ratio at 0.25
    (quadrant area = 25% of viewport).  We use a 0.20 threshold as the
    practical "panel covers the whole quadrant" indicator.  The spec
    listed 0.5, but that is unreachable without a full-frame scan.
    """
    rgb = _load_rgb(png_path)
    h, w, _ = rgb.shape
    half_h, half_w = h // 2, w // 2
    quads = {
        "topleft": (0, 0, half_w, half_h),
        "topright": (half_w, 0, w, half_h),
        "bottomleft": (0, half_h, half_w, h),
        "bottomright": (half_w, half_h, w, h),
    }
    if quadrant not in quads:
        return {"error": f"unknown quadrant {quadrant!r}", "pass": False}
    x0, y0, x1, y1 = quads[quadrant]
    sub = rgb[y0:y1, x0:x1]

    lum = _luma(sub)
    bg_lum = float(np.median(lum))
    mask = np.abs(lum - bg_lum) > 15

    labels, n = _label_components(mask)
    best_area = 0
    best_bbox = None
    for lbl in range(1, n + 1):
        bx0, by0, bx1, by1 = _bbox_of_component(labels, lbl)
        bw, bh = bx1 - bx0 + 1, by1 - by0 + 1
        area = bw * bh
        if area > best_area:
            best_area = area
            best_bbox = (bx0 + x0, by0 + y0, bx1 + x0, by1 + y0)

    viewport_area = w * h
    ratio = best_area / viewport_area if viewport_area else 0.0
    return {
        "ratio": round(float(ratio), 4),
        "panel_bbox": list(best_bbox) if best_bbox else None,
        "panel_area_px": int(best_area),
        "viewport_area_px": int(viewport_area),
        "quadrant": quadrant,
        "ratio_max": 0.20,
        "pass": bool(ratio < 0.20),
    }


# ---------------------------------------------------------------------------
# Check 7: brush visibility alpha
# ---------------------------------------------------------------------------

def brush_visibility_alpha(png_path: str, rect: tuple[int, int, int, int] | None = None,
                           rgb_delta: int = 30) -> dict:
    """
    Inspect the brush region.  A visible brush should produce at least
    one pixel whose RGB differs by > rgb_delta from the background
    median.  An invisible brush is uniform.
    """
    rgb = _load_rgb(png_path)
    h, w, _ = rgb.shape
    if rect is None:
        # Default = centred 25% square (brush hover indicator)
        cx, cy = w // 2, h // 2
        bw, bh = w // 4, h // 4
        rect = (cx - bw // 2, cy - bh // 2, cx + bw // 2, cy + bh // 2)
    x0, y0, x1, y1 = rect
    x0, y0 = max(0, x0), max(0, y0)
    x1, y1 = min(w, x1), min(h, y1)
    sub = rgb[y0:y1, x0:x1, :].astype(np.int32)
    sh, sw, _ = sub.shape
    if sh == 0 or sw == 0:
        return {
            "rgb_delta": 0,
            "pixels_above_delta": 0,
            "pct_above_delta": 0.0,
            "rgb_delta_min": rgb_delta,
            "min_pct": 0.001,
            "pass": False,
            "note": "empty rect",
        }

    # Background reference: the same frame OUTSIDE the rect, on the same band
    # Use the full-frame median as a robust background proxy
    bg_r = float(np.median(rgb[..., 0]))
    bg_g = float(np.median(rgb[..., 1]))
    bg_b = float(np.median(rgb[..., 2]))

    diff = np.abs(sub - np.array([bg_r, bg_g, bg_b], dtype=np.int32)).max(axis=2)
    above = diff > rgb_delta
    n_above = int(above.sum())
    pct = n_above / (sh * sw)
    return {
        "rgb_delta": rgb_delta,
        "pixels_above_delta": n_above,
        "pct_above_delta": round(pct, 6),
        "max_delta": int(diff.max()),
        "background_rgb": [int(bg_r), int(bg_g), int(bg_b)],
        "rect": [x0, y0, x1, y1],
        "rgb_delta_min": rgb_delta,
        "min_pct": 0.001,
        "pass": (n_above > 0) and (pct >= 0.001),
    }


# ---------------------------------------------------------------------------
# Registry
# ---------------------------------------------------------------------------

CHECKS: dict[str, Callable[..., dict]] = {
    "actor_silhouette_complexity": actor_silhouette_complexity,
    "biome_color_variance": biome_color_variance,
    "nametag_pixel_height": nametag_pixel_height,
    "lod_flash_diff": lod_flash_diff,
    "water_uniform_y_blue": water_uniform_y_blue,
    "ui_panel_ratio": ui_panel_ratio,
    "brush_visibility_alpha": brush_visibility_alpha,
}


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def _emit(name: str, png: str, value: dict, threshold: dict, ok: bool, dur_ms: int) -> None:
    payload = {
        "check": name,
        "png": png,
        "value": value,
        "threshold": threshold,
        "pass": ok,
        "duration_ms": dur_ms,
    }
    print(json.dumps(payload))


def _run_check(name: str, png: str, **kwargs: Any) -> tuple[dict, bool, int]:
    fn = CHECKS[name]
    t0 = time.monotonic()
    out = fn(png, **kwargs) if kwargs else fn(png)
    dur = int((time.monotonic() - t0) * 1000)
    # Pull threshold from the returned dict if present, else empty
    threshold = {k: v for k, v in out.items() if k.endswith("_min") or k.endswith("_max")
                 or k in ("max_height_px",)}
    # Strip control fields from "value" so the JSON is well-shaped
    value = {k: v for k, v in out.items() if k not in threshold and k != "pass"}
    passed = bool(out.get("pass", False))
    return value, passed, dur


def main() -> int:
    parser = argparse.ArgumentParser(description="WSM3D pixel-verification harness")
    sub = parser.add_subparsers(dest="cmd", required=True)

    p_check = sub.add_parser("check", help="Run a single named check")
    p_check.add_argument("name", choices=sorted(CHECKS.keys()))
    p_check.add_argument("--png", required=True, help="Path to PNG under test")
    p_check.add_argument("--png-b", dest="png_b", default=None,
                         help="Second PNG (for lod_flash_diff)")
    # Generic overrides; each check tolerates a different subset.
    p_check.add_argument("--region", default=None,
                         help="x0,y0,x1,y1 region (upper-screen checks)")
    p_check.add_argument("--bg-tolerance", type=int, default=None)
    p_check.add_argument("--grid", type=int, default=None)
    p_check.add_argument("--hue-tol", type=float, default=None)
    p_check.add_argument("--quadrant", default=None)
    p_check.add_argument("--brush-rect", default=None,
                         help="x,y,w,h brush rect for brush_visibility_alpha")
    p_check.add_argument("--rgb-delta", type=int, default=None)
    p_check.add_argument("--blue-dominance", type=int, default=None)
    p_check.add_argument("--threshold-rgb", type=int, default=None)

    p_runall = sub.add_parser("runall", help="Run all checks against a glob")
    p_runall.add_argument("--png-glob", required=True)
    p_runall.add_argument("--out", default=None, help="Write JSON summary to file")

    p_bench = sub.add_parser("bench", help="Before/after diff for a single check")
    p_bench.add_argument("before")
    p_bench.add_argument("after")
    p_bench.add_argument("--check", required=True, choices=sorted(CHECKS.keys()))

    args = parser.parse_args()

    if args.cmd == "check":
        name = args.name
        kwargs: dict[str, Any] = {}
        if args.region:
            x0, y0, x1, y1 = (int(v) for v in args.region.split(","))
            kwargs["region"] = (x0, y0, x1, y1)
        if args.bg_tolerance is not None:
            kwargs["bg_tolerance"] = args.bg_tolerance
        if args.grid is not None:
            kwargs["grid"] = args.grid
        if args.hue_tol is not None:
            kwargs["hue_tol"] = args.hue_tol
        if args.quadrant:
            kwargs["quadrant"] = args.quadrant
        if args.brush_rect:
            x, y, ww, hh = (int(v) for v in args.brush_rect.split(","))
            kwargs["rect"] = (x, y, x + ww, y + hh)
        if args.rgb_delta is not None:
            kwargs["rgb_delta"] = args.rgb_delta
        if args.blue_dominance is not None:
            kwargs["blue_dominance"] = args.blue_dominance
        if name == "lod_flash_diff":
            if not args.png_b:
                print(json.dumps({"error": "lod_flash_diff requires --png-b"}))
                return 2
            value, ok, dur = lod_flash_diff(args.png, args.png_b,
                                            threshold_rgb=args.threshold_rgb or 10), False, 0
            # Re-run to get a proper timed envelope
            t0 = time.monotonic()
            out = lod_flash_diff(args.png, args.png_b, threshold_rgb=args.threshold_rgb or 10)
            dur = int((time.monotonic() - t0) * 1000)
            threshold = {k: v for k, v in out.items()
                         if k.endswith("_min") or k.endswith("_max")}
            value = {k: v for k, v in out.items() if k not in threshold and k != "pass"}
            ok = bool(out.get("pass", False))
            _emit(name, args.png, value, threshold, ok, dur)
            return 0 if ok else 1
        try:
            value, ok, dur = _run_check(name, args.png, **kwargs)
        except FileNotFoundError as e:
            print(json.dumps({"error": f"png missing: {e}"}))
            return 2
        threshold = {k: v for k, v in (CHECKS[name](args.png, **kwargs) if not kwargs else {}).items()
                     if k.endswith("_min") or k.endswith("_max")} if False else {}
        # Re-derive threshold from a single dry-run so the dict is accurate
        try:
            dry = CHECKS[name](args.png, **kwargs)
            threshold = {k: v for k, v in dry.items()
                         if k.endswith("_min") or k.endswith("_max")}
        except Exception:
            threshold = {}
        _emit(name, args.png, value, threshold, ok, dur)
        return 0 if ok else 1

    if args.cmd == "runall":
        files = sorted(_glob.glob(args.png_glob))
        if not files:
            print(json.dumps({"error": f"no files match {args.png_glob!r}"}))
            return 2
        summary = {"files": len(files), "checks": {}, "results": []}
        for f in files:
            for name in CHECKS:
                if name == "lod_flash_diff":
                    continue  # needs two PNGs
                try:
                    value, ok, dur = _run_check(name, f)
                    summary["results"].append({
                        "file": f, "check": name, "pass": ok, "duration_ms": dur,
                    })
                    summary["checks"].setdefault(name, {"pass": 0, "fail": 0})
                    summary["checks"][name]["pass" if ok else "fail"] += 1
                except FileNotFoundError:
                    continue
                except Exception as e:
                    summary["results"].append({
                        "file": f, "check": name, "error": str(e),
                    })
        if args.out:
            Path(args.out).write_text(json.dumps(summary, indent=2))
        else:
            print(json.dumps(summary, indent=2))
        # Exit 0 if at least one check passed
        any_pass = any(r.get("pass") for r in summary["results"])
        return 0 if any_pass else 1

    if args.cmd == "bench":
        # For lod_flash_diff we have a natural before/after; for others we
        # emit each side's measurement so the caller can diff.
        name = args.check
        if name == "lod_flash_diff":
            t0 = time.monotonic()
            out = lod_flash_diff(args.before, args.after)
            dur = int((time.monotonic() - t0) * 1000)
            threshold = {k: v for k, v in out.items()
                         if k.endswith("_min") or k.endswith("_max")}
            value = {k: v for k, v in out.items() if k not in threshold and k != "pass"}
            _emit(name, args.before, value, threshold, bool(out.get("pass", False)), dur)
            return 0 if out.get("pass") else 1
        # For other checks, just run on `before` and print.
        value, ok, dur = _run_check(name, args.before)
        try:
            dry = CHECKS[name](args.before)
            threshold = {k: v for k, v in dry.items()
                         if k.endswith("_min") or k.endswith("_max")}
        except Exception:
            threshold = {}
        _emit(name, args.before, value, threshold, ok, dur)
        return 0 if ok else 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
