#!/usr/bin/env python3
"""
Self-tests for pixel-verify.py.  xUnit-style: each test_* function returns
None on pass, raises AssertionError on fail.  No external runner, no
Unity deps — just stdlib + PIL + NumPy.

Run: python Tools/pixel-verify.tests.py
"""

from __future__ import annotations

import importlib.util
import math
import sys
import tempfile
import traceback
from pathlib import Path

import numpy as np
from PIL import Image

# Reuse the harness (filename has a hyphen, so importlib.shim)
_harness_path = Path(__file__).resolve().parent / "pixel-verify.py"
_spec = importlib.util.spec_from_file_location("pixel_verify", _harness_path)
if _spec is None or _spec.loader is None:
    raise RuntimeError(f"could not load harness at {_harness_path}")
pv = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pv)


# ---------------------------------------------------------------------------
# Fixture builders
# ---------------------------------------------------------------------------

def _save(arr: np.ndarray) -> str:
    """Save uint8 HxWx3 array to a temp PNG and return its path."""
    f = tempfile.NamedTemporaryFile(suffix=".png", delete=False)
    f.close()
    Image.fromarray(arr.astype(np.uint8), mode="RGB").save(f.name)
    return f.name


def _flat(rgb=(100, 100, 100), size=(320, 240)) -> np.ndarray:
    return np.tile(np.array(rgb, dtype=np.uint8), (size[1], size[0], 1))


def _perlin_rgb(size=(320, 240), scale=24, seed=0) -> np.ndarray:
    """Cheap perlin-ish noise via summed sinusoids (no scipy)."""
    rng = np.random.default_rng(seed)
    h, w = size[1], size[0]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    img = np.zeros((h, w, 3), dtype=np.float32)
    for c in range(3):
        for k in range(4):
            f = (k + 1) * scale / max(h, w)
            ang = rng.uniform(0, math.tau)
            ax = math.cos(ang) * f * 2 * math.pi
            ay = math.sin(ang) * f * 2 * math.pi
            ph = rng.uniform(0, math.tau)
            img[..., c] += np.sin(xx * ax + yy * ay + ph) * 40
    img = np.clip(64 + img, 0, 255)
    return img.astype(np.uint8)


def _sprite_billboard(size=(320, 240)) -> np.ndarray:
    """A single rectangular dark blob on light bg = 2D billboard lookalike."""
    img = _flat(rgb=(200, 220, 240), size=size)
    h, w, _ = img.shape
    x0, y0, x1, y1 = w // 4, h // 4, 3 * w // 4, 3 * h // 4
    # Solid interior + thin border so the component is a single clean rect
    img[y0:y1, x0:x1] = (60, 60, 60)
    return img


def _voxel_like(size=(320, 240), seed=1) -> np.ndarray:
    """Irregular noisy cluster = 3D voxel mesh lookalike."""
    w, h = size
    rng = np.random.default_rng(seed)
    img = _flat(rgb=(80, 100, 70), size=size)  # grass bg
    # Drop many small irregular clusters
    for _ in range(40):
        cx, cy = int(rng.integers(w // 5, 4 * w // 5)), int(rng.integers(h // 5, 4 * h // 5))
        r = int(rng.integers(8, 22))
        # Build an irregular mask: filled disk with random holes
        cy0, cy1 = max(0, cy - r), min(h, cy + r)
        cx0, cx1 = max(0, cx - r), min(w, cx + r)
        if cy1 <= cy0 or cx1 <= cx0:
            continue
        yy, xx = np.mgrid[cy0:cy1, cx0:cx1]
        in_disk = (yy - cy) ** 2 + (xx - cx) ** 2 < r * r
        holes = rng.random(in_disk.shape) < 0.3
        mask = in_disk & ~holes
        col = rng.integers(40, 200, size=3)
        # Set each pixel of the mask to col (broadcasting)
        sub = img[cy0:cy1, cx0:cx1]
        sub[mask] = col
        img[cy0:cy1, cx0:cx1] = sub
    return img


def _water_flat(size=(320, 240)) -> np.ndarray:
    """Solid blue water in lower half = uniform Y."""
    img = _flat(rgb=(150, 150, 150), size=size)  # sky/terrain above
    h, w, _ = img.shape
    img[h // 2:, :, :] = (40, 60, 180)  # uniform blue
    return img


def _water_sunken(size=(320, 240), seed=2) -> np.ndarray:
    """Blue water with terrain poking through = high Y variance."""
    img = _water_flat(size=size)
    h, w, _ = img.shape
    rng = np.random.default_rng(seed)
    # Sprinkle varied patches (terrain poking through)
    for _ in range(80):
        cx, cy = int(rng.integers(0, w)), int(rng.integers(h // 2, h))
        r = int(rng.integers(2, 8))
        col = rng.integers(20, 230, size=3)
        yy, xx = np.mgrid[max(0, cy - r):min(h, cy + r), max(0, cx - r):min(w, cx + r)]
        img[yy, xx] = col
    return img


def _ui_panel_huge(size=(320, 240)) -> np.ndarray:
    """Big flat UI panel covering > 50% of the entire viewport."""
    img = _flat(rgb=(20, 20, 30), size=size)  # dark scene
    h, w, _ = img.shape
    # Solid white panel that fills nearly the whole frame; the topright
    # quadrant (half the viewport) is fully covered, so ratio > 0.5.
    img[0: h, 0: w, :] = (220, 220, 220)
    # Re-add a thin dark border so the foreground mask forms a single
    # connected component with a bbox that is the full quadrant
    img[0:1, :, :] = (20, 20, 20)
    img[h - 1:h, :, :] = (20, 20, 20)
    img[:, 0:1, :] = (20, 20, 20)
    img[:, w - 1:w, :] = (20, 20, 20)
    return img


def _brush_invisible(size=(320, 240)) -> np.ndarray:
    """Uniform scene with no brush hover delta."""
    return _flat(rgb=(120, 130, 90), size=size)


def _brush_visible(size=(320, 240)) -> np.ndarray:
    img = _flat(rgb=(120, 130, 90), size=size)
    h, w, _ = img.shape
    # Bright brush outline
    cx, cy = w // 2, h // 2
    bw, bh = 30, 30
    img[cy - bh:cy + bh, cx - 2:cx + 2] = (250, 250, 250)
    img[cy - 2:cy + 2, cx - bw:cx + bw] = (250, 250, 250)
    return img


# ---------------------------------------------------------------------------
# Test runner
# ---------------------------------------------------------------------------

TESTS: list[tuple[str, callable]] = []


def test(name: str):
    def deco(fn):
        TESTS.append((name, fn))
        return fn
    return deco


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

@test("flat_png_biome_variance_fails")
def t_flat_png_biome_variance_fails():
    """Flat PNG should produce stddev ~0 and fail the variance check."""
    path = _save(_flat())
    out = pv.biome_color_variance(path)
    assert out["stddev"] < 0.5, f"flat png should have stddev < 0.5, got {out['stddev']}"
    assert out["pass"] is False, "flat png should fail biome_color_variance"


@test("perlin_png_biome_variance_passes")
def t_perlin_png_biome_variance_passes():
    """Noisy perlin PNG should produce stddev > 5 and pass."""
    path = _save(_perlin_rgb())
    out = pv.biome_color_variance(path)
    assert out["stddev"] > 5.0, f"perlin png should have stddev > 5, got {out['stddev']}"
    assert out["pass"] is True, f"perlin png should pass, got {out}"


@test("sprite_billboard_fails_actor_silhouette")
def t_sprite_billboard_fails_actor_silhouette():
    """Solid rectangular sprite should fail (high fill ratio, low edges)."""
    path = _save(_sprite_billboard())
    out = pv.actor_silhouette_complexity(path)
    assert out["pass"] is False, (
        f"sprite should fail; got best={out.get('best')} "
        f"passing_candidates={out.get('passing_candidates')}"
    )


@test("voxel_like_passes_actor_silhouette")
def t_voxel_like_passes_actor_silhouette():
    """Irregular noisy clusters should pass."""
    path = _save(_voxel_like())
    out = pv.actor_silhouette_complexity(path)
    best = out.get("best") or {}
    assert best.get("fill_ratio", 1.0) < 0.85, f"voxel fill_ratio too high: {best}"
    assert best.get("edge_density", 0.0) > 8.0, f"voxel edge_density too low: {best}"
    assert out["pass"] is True, f"voxel-like should pass, got {out}"


@test("water_flat_passes_uniform_y")
def t_water_flat_passes_uniform_y():
    path = _save(_water_flat())
    out = pv.water_uniform_y_blue(path)
    assert out["y_variance"] < 5.0, f"flat water should have low Y-variance, got {out}"
    assert out["pass"] is True, f"flat water should pass, got {out}"


@test("water_sunken_fails_uniform_y")
def t_water_sunken_fails_uniform_y():
    path = _save(_water_sunken())
    out = pv.water_uniform_y_blue(path)
    assert out["y_variance"] > 5.0, f"sunken water should have high Y-variance, got {out}"
    assert out["pass"] is False, f"sunken water should fail, got {out}"


@test("huge_ui_panel_fails_ratio")
def t_huge_ui_panel_fails_ratio():
    path = _save(_ui_panel_huge())
    out = pv.ui_panel_ratio(path)
    print(f"    [debug] ui_panel_ratio out: {out}")
    # The per-quadrant scan caps measurable ratio at 0.25.  A fullscreen
    # panel that fills the whole frame should hit the cap and fail the
    # 0.20 threshold.
    assert out["ratio"] >= 0.24, f"huge panel should fill the topright quadrant, got {out}"
    assert out["pass"] is False, f"huge panel should fail, got pass={out['pass']}"


@test("invisible_brush_fails")
def t_invisible_brush_fails():
    path = _save(_brush_invisible())
    out = pv.brush_visibility_alpha(path)
    # Uniform scene: any pixel above delta is zero, so pass must be False
    assert out["pixels_above_delta"] == 0, f"uniform scene should have 0 above-delta pixels, got {out}"
    assert out["pass"] is False


@test("visible_brush_passes")
def t_visible_brush_passes():
    path = _save(_brush_visible())
    out = pv.brush_visibility_alpha(path)
    assert out["pixels_above_delta"] > 0, f"visible brush should have > 0 above-delta pixels, got {out}"
    assert out["pass"] is True


@test("lod_flash_diff_identical_passes")
def t_lod_flash_diff_identical_passes():
    a = _perlin_rgb(seed=3)
    path_a = _save(a)
    path_b = _save(a.copy())  # identical copy
    out = pv.lod_flash_diff(path_a, path_b, threshold_rgb=10)
    print(f"    [debug] identical lod_flash_diff out: {out}")
    assert float(out["diff_pct"]) < 1e-4, f"identical frames should have ~0 diff, got {out['diff_pct']}"
    assert bool(out["pass"]) is True, f"identical frames should pass, got pass={out['pass']}"


@test("lod_flash_diff_different_fails")
def t_lod_flash_diff_different_fails():
    a = _perlin_rgb(seed=4)
    b = _perlin_rgb(seed=5)  # different noise
    path_a = _save(a)
    path_b = _save(b)
    out = pv.lod_flash_diff(path_a, path_b, threshold_rgb=10)
    print(f"    [debug] different lod_flash_diff out: {out}")
    assert float(out["diff_pct"]) > 0.005, f"different frames should exceed 0.5% diff, got {out['diff_pct']}"
    assert bool(out["pass"]) is False, f"different frames should fail, got pass={out['pass']}"


# ---------------------------------------------------------------------------
# Runner
# ---------------------------------------------------------------------------

def run() -> int:
    passed = 0
    failed = 0
    print("Running pixel-verify self-tests")
    print("-" * 60)
    for name, fn in TESTS:
        try:
            fn()
            print(f"  PASS  {name}")
            passed += 1
        except AssertionError as e:
            print(f"  FAIL  {name}: {e}")
            failed += 1
        except Exception as e:
            print(f"  ERROR {name}: {e}")
            traceback.print_exc()
            failed += 1
    print("-" * 60)
    print(f"  {passed} passed, {failed} failed (of {len(TESTS)})")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    raise SystemExit(run())
