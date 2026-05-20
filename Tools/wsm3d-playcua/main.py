#!/usr/bin/env python3
"""
WorldSphereMod3D PlayCUA runner.

Executes YAML scenario scripts that drive the in-game BridgeRPC server and validate
both telemetry and screenshot content with Anthropic Claude Opus vision.

Supported step actions:
  - load_save
  - wait_n_frames
  - toggle_flag
  - screenshot
  - assert_telemetry
"""

from __future__ import annotations

import argparse
import base64
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Tuple

try:
    import yaml
except ImportError as exc:
    raise SystemExit("PyYAML is required. Install with `pip install pyyaml`.") from exc


def _load_yaml(path: str) -> Dict[str, Any]:
    with open(path, "r", encoding="utf-8") as f:
        payload = yaml.safe_load(f)
    if not isinstance(payload, dict):
        raise ValueError("scenario file must contain a YAML mapping")
    return payload


def _to_bool(value: Any) -> bool:
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        if value.lower() in {"1", "true", "yes", "on"}:
            return True
        if value.lower() in {"0", "false", "no", "off"}:
            return False
    raise ValueError(f"invalid boolean value: {value!r}")


def _coerce_number(value: Any) -> float:
    if isinstance(value, (int, float)):
        return float(value)
    if isinstance(value, str):
        return float(value)
    raise ValueError(f"invalid number: {value!r}")


class BridgeClient:
    """Small HTTP client for 127.0.0.1:8766 BridgeRPC endpoints."""

    def __init__(self, host: str, port: int, timeout_s: float = 5.0) -> None:
        self.base = f"http://{host}:{port}"
        self.timeout = timeout_s

    def _url(self, path: str, params: Dict[str, Any] | None = None) -> str:
        url = self.base + path
        if params:
            q = urllib.parse.urlencode(params)
            url += f"?{q}"
        return url

    def _request_json(self, method: str, path: str, params: Dict[str, Any] | None = None) -> Dict[str, Any]:
        request = urllib.request.Request(
            self._url(path, params),
            method=method.upper(),
        )
        request.add_header("Accept", "application/json")
        with urllib.request.urlopen(request, timeout=self.timeout) as resp:
            body = resp.read().decode("utf-8")
            data = json.loads(body) if body else {}
            if not isinstance(data, dict):
                return {"ok": False, "error": f"non-dict response: {body[:120]}"}
            return data

    def health(self) -> Dict[str, Any]:
        return self._request_json("GET", "/health")

    def telemetry(self) -> Dict[str, Any]:
        return self._request_json("GET", "/telemetry")

    def load_save(self, slot: int) -> Dict[str, Any]:
        return self._request_json("POST", "/actions/load_save", {"slot": slot})

    def toggle_flag(self, key: str, value: bool) -> Dict[str, Any]:
        return self._request_json("POST", f"/settings/{key}", {"value": str(value).lower()})


def _run_wait_n_frames(frames: int, fps: float) -> None:
    frame_delay = 1.0 / max(float(fps), 1.0)
    for _ in range(max(int(frames), 0)):
        time.sleep(frame_delay)


class Win32ScreenshotError(RuntimeError):
    pass


class Win32Capture:
    """Capture full-screen screenshots using native Win32 GDI APIs."""

    def __init__(self) -> None:
        try:
            import ctypes
            self.ctypes = ctypes
        except Exception as exc:
            raise Win32ScreenshotError("ctypes not available in this Python runtime") from exc

    def capture(self, out_path: Path) -> Path:
        c = self.ctypes

        class BITMAPINFOHEADER(c.Structure):
            _fields_ = [
                ("biSize", c.c_uint32),
                ("biWidth", c.c_int32),
                ("biHeight", c.c_int32),
                ("biPlanes", c.c_uint16),
                ("biBitCount", c.c_uint16),
                ("biCompression", c.c_uint32),
                ("biSizeImage", c.c_uint32),
                ("biXPelsPerMeter", c.c_int32),
                ("biYPelsPerMeter", c.c_int32),
                ("biClrUsed", c.c_uint32),
                ("biClrImportant", c.c_uint32),
            ]

        class RGBQUAD(c.Structure):
            _fields_ = [("rgbBlue", c.c_ubyte), ("rgbGreen", c.c_ubyte), ("rgbRed", c.c_ubyte), ("rgbReserved", c.c_ubyte)]

        class BITMAPINFO(c.Structure):
            _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", RGBQUAD * 1)]

        user32 = c.WinDLL("user32", use_last_error=True)
        gdi32 = c.WinDLL("gdi32", use_last_error=True)

        SM_CXSCREEN = 0
        SM_CYSCREEN = 1
        SRCCOPY = 0x00CC0020
        DIB_RGB_COLORS = 0

        width = user32.GetSystemMetrics(SM_CXSCREEN)
        height = user32.GetSystemMetrics(SM_CYSCREEN)
        if width <= 0 or height <= 0:
            raise Win32ScreenshotError("invalid desktop dimensions")

        hwnd = user32.GetDesktopWindow()
        hdc = user32.GetWindowDC(hwnd)
        if not hdc:
            raise Win32ScreenshotError("failed to get desktop DC")

        memdc = gdi32.CreateCompatibleDC(hdc)
        if not memdc:
            user32.ReleaseDC(hwnd, hdc)
            raise Win32ScreenshotError("failed to create memory DC")

        hbmp = gdi32.CreateCompatibleBitmap(hdc, width, height)
        if not hbmp:
            gdi32.DeleteDC(memdc)
            user32.ReleaseDC(hwnd, hdc)
            raise Win32ScreenshotError("failed to create compatible bitmap")

        old_obj = gdi32.SelectObject(memdc, hbmp)
        if not gdi32.BitBlt(memdc, 0, 0, width, height, hdc, 0, 0, SRCCOPY):
            gdi32.SelectObject(memdc, old_obj)
            gdi32.DeleteObject(hbmp)
            gdi32.DeleteDC(memdc)
            user32.ReleaseDC(hwnd, hdc)
            raise Win32ScreenshotError("BitBlt failed while copying screen")

        header = BITMAPINFOHEADER(
            biSize=c.sizeof(BITMAPINFOHEADER),
            biWidth=width,
            biHeight=-height,
            biPlanes=1,
            biBitCount=32,
            biCompression=0,
            biSizeImage=width * height * 4,
            biXPelsPerMeter=0,
            biYPelsPerMeter=0,
            biClrUsed=0,
            biClrImportant=0,
        )
        bmi = BITMAPINFO(bmiHeader=header)

        buf_size = width * height * 4
        pixel_buf = c.create_string_buffer(buf_size)
        got = gdi32.GetDIBits(
            memdc,
            hbmp,
            0,
            height,
            c.byref(pixel_buf),
            c.byref(bmi),
            DIB_RGB_COLORS,
        )
        if got != height:
            gdi32.SelectObject(memdc, old_obj)
            gdi32.DeleteObject(hbmp)
            gdi32.DeleteDC(memdc)
            user32.ReleaseDC(hwnd, hdc)
            raise Win32ScreenshotError("GetDIBits returned unexpected row count")

        out_path.parent.mkdir(parents=True, exist_ok=True)

        # Pillow is used to encode PNG while keeping capture path via Win32.
        try:
            from PIL import Image
        except Exception as exc:
            gdi32.SelectObject(memdc, old_obj)
            gdi32.DeleteObject(hbmp)
            gdi32.DeleteDC(memdc)
            user32.ReleaseDC(hwnd, hdc)
            raise Win32ScreenshotError(
                "Pillow is required for PNG encoding; install with `pip install pillow`."
            ) from exc

        image = Image.frombuffer("RGBA", (width, height), pixel_buf, "raw", "BGRA", 0, 1)
        image.save(out_path, format="PNG")

        gdi32.SelectObject(memdc, old_obj)
        gdi32.DeleteObject(hbmp)
        gdi32.DeleteDC(memdc)
        user32.ReleaseDC(hwnd, hdc)
        return out_path


class VisionValidationError(RuntimeError):
    pass


class VisionValidator:
    """Use Anthropic vision (Claude) to validate screenshot content."""

    def __init__(self, api_key: str | None, model: str = "claude-3-opus-20240229") -> None:
        self.model = model
        self.api_key = api_key
        self.client = None

        if api_key:
            try:
                import anthropic

                self.client = anthropic.Anthropic(api_key=api_key)
            except Exception as exc:
                raise VisionValidationError("Anthropic SDK import failed; install with `pip install anthropic`.") from exc

    def _build_prompt(self, prompt: str, criteria: Any) -> str:
        block = json.dumps(criteria, indent=2, sort_keys=True) if criteria else "{}"
        return (
            "You are a strict visual regression checker for an end-to-end automation pipeline. "
            "Evaluate the screenshot against the exact criteria and return only JSON."
            "The JSON shape must be: "
            '{"passes": bool, "reason": "text", "confidence": 0.0-1.0}.'
            "\\n"
            f"Scenario prompt: {prompt}\\n"
            f"Expected criteria:\\n{block}"
        )

    def validate(self, image_path: Path, prompt: str, criteria: Any) -> Dict[str, Any]:
        if self.client is None:
            raise VisionValidationError("Anthropic client unavailable (missing API key)")
        if not image_path.exists():
            raise VisionValidationError(f"screenshot not found: {image_path}")

        image_data = base64.b64encode(image_path.read_bytes()).decode("ascii")

        msg = self.client.messages.create(
            model=self.model,
            max_tokens=1024,
            messages=[
                {
                    "role": "user",
                    "content": [
                        {"type": "text", "text": self._build_prompt(prompt, criteria)},
                        {
                            "type": "image",
                            "source": {
                                "type": "base64",
                                "media_type": "image/png",
                                "data": image_data,
                            },
                        },
                    ],
                }
            ],
        )

        full = ""
        for block in msg.content:
            if getattr(block, "type", "") == "text":
                full += getattr(block, "text", "")
            elif isinstance(block, str):
                full += block

        if not full.strip():
            return {"passes": False, "reason": "empty vision model response"}

        match = re.search(r"\{.*\}", full, re.S)
        if not match:
            return {"passes": False, "reason": f"vision response not json: {full[:180]}"}

        try:
            parsed = json.loads(match.group(0))
        except Exception as exc:
            return {"passes": False, "reason": f"vision json parse failed: {exc}"}

        return parsed if isinstance(parsed, dict) else {"passes": False, "reason": "vision response was not an object"}


def _telemetry_value(payload: Dict[str, Any], name: str) -> Any:
    key = name.strip()
    if key in payload:
        return payload[key]
    # snake_case fallback
    snake = re.sub(r"(?<!^)(?=[A-Z])", "_", key).lower()
    return payload.get(snake)


def _compare(lhs: float, op: str, rhs: float) -> bool:
    if op in {"==", "eq", "equals"}:
        return lhs == rhs
    if op in {"!=", "ne", "not_equals"}:
        return lhs != rhs
    if op in {"<", "lt"}:
        return lhs < rhs
    if op in {"<=", "lte", "le"}:
        return lhs <= rhs
    if op in {">", "gt"}:
        return lhs > rhs
    if op in {">=", "gte", "ge"}:
        return lhs >= rhs
    raise ValueError(f"unknown operator: {op}")


def _assert_telemetry(step: Dict[str, Any], client: BridgeClient) -> Tuple[bool, str]:
    payload = client.telemetry()
    if not isinstance(payload, dict):
        return False, "telemetry response was invalid"
    checks = step.get("checks") or []
    if not isinstance(checks, list):
        return False, "checks must be a list"

    for check in checks:
        if not isinstance(check, dict):
            return False, "each check must be an object"
        field = check.get("field")
        op = str(check.get("op", "eq")).lower()
        raw = check.get("value")
        if field is None or raw is None:
            return False, "check requires field and value"

        actual = _telemetry_value(payload, str(field))
        if actual is None:
            return False, f"missing telemetry field: {field}"
        try:
            actual_n = _coerce_number(actual)
            target_n = _coerce_number(raw)
        except Exception:
            return False, f"non-numeric check field/value for {field}"

        if not _compare(actual_n, op, target_n):
            return False, f"{field} check failed: actual={actual_n} op={op} target={target_n}"

    return True, "telemetry checks passed"


def _execute_scenario(scenario: Dict[str, Any], client: BridgeClient, screenshot: Win32Capture, validator: VisionValidator | None) -> Dict[str, Any]:
    steps = scenario.get("steps") or []
    if not isinstance(steps, list):
        raise ValueError("steps must be a list")

    results: List[Dict[str, Any]] = []
    screenshot_root = Path(scenario.get("artifacts", "artifacts")).expanduser()
    artifact_root = Path(scenario.get("output_dir", screenshot_root))
    default_delay = float(scenario.get("default_wait_seconds", 0.0))

    if default_delay > 0:
        time.sleep(default_delay)

    for index, raw_step in enumerate(steps, start=1):
        if not isinstance(raw_step, dict):
            raise ValueError(f"step #{index} is not a mapping")

        action = str(raw_step.get("action") or raw_step.get("type") or "").strip()
        if not action:
            raise ValueError(f"step #{index} missing action")

        action = action.lower()
        if action == "load_save":
            slot = int(raw_step.get("slot", raw_step.get("save_slot", 0)))
            if slot < 0:
                raise ValueError(f"load_save step #{index} requires non-negative slot")
            payload = client.load_save(slot)
            ok = bool(payload.get("ok"))
            details = payload
        elif action == "wait_n_frames":
            frames = int(raw_step.get("frames", raw_step.get("count", 0)))
            fps = float(raw_step.get("fps", raw_step.get("frame_rate", 30)))
            _run_wait_n_frames(frames, fps)
            ok, details = True, {"frames": frames, "fps": fps}
        elif action == "toggle_flag":
            key = str(raw_step.get("key") or raw_step.get("setting"))
            value = _to_bool(raw_step.get("value", False))
            if not key:
                raise ValueError(f"toggle_flag step #{index} missing key")
            payload = client.toggle_flag(key, value)
            ok = bool(payload.get("ok"))
            details = payload
        elif action == "screenshot":
            path_raw = raw_step.get("path")
            if not path_raw:
                path_raw = f"{scenario.get('name', 'playcua')}-step-{index}.png"
            screenshot_path = Path(path_raw)
            if not screenshot_path.is_absolute():
                screenshot_path = artifact_root / screenshot_path
            img_path = screenshot.capture(screenshot_path)
            ok = True
            details = {"path": str(img_path)}

            vision = raw_step.get("vision") or {}
            if vision:
                prompt = vision.get("prompt", "Check whether this screenshot satisfies the expected criteria.")
                criteria = vision.get("criteria")
                required = bool(vision.get("required", True))
                if validator is None:
                    if required:
                        raise RuntimeError("screenshot vision check required but no Anthropic client available")
                    details["vision"] = {"skipped": True, "reason": "validator unavailable"}
                else:
                    result = validator.validate(img_path, prompt, criteria)
                    details["vision"] = result
                    ok = bool(result.get("passes", False))
                    if required and not ok:
                        details["error"] = result.get("reason", "vision criteria failed")
        elif action == "assert_telemetry":
            required = bool(raw_step.get("required", True))
            ok, details = _assert_telemetry(raw_step, client)
            if required and not ok:
                raise AssertionError(f"assert_telemetry step #{index} failed: {details}")
        else:
            raise ValueError(f"unknown action at step #{index}: {action}")

        results.append(
            {
                "index": index,
                "action": action,
                "ok": ok,
                "details": details,
            }
        )

    return {
        "name": scenario.get("name"),
        "status": "ok",
        "steps": results,
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run WorldSphereMod3D PlayCUA scenario(s)")
    parser.add_argument("scenario", help="Path to YAML scenario")
    parser.add_argument("--host", default="127.0.0.1", help="BridgeRPC host")
    parser.add_argument("--port", type=int, default=8766, help="BridgeRPC port")
    parser.add_argument("--bridge-timeout", type=float, default=8.0, help="HTTP timeout seconds")
    parser.add_argument("--no-healthcheck", action="store_true", help="Skip bridge healthcheck")
    parser.add_argument(
        "--report",
        default="Tools/wsm3d-playcua/.reports/latest.json",
        help="Report output path",
    )
    parser.add_argument(
        "--anthropic-model",
        default="claude-3-opus-20240229",
        help="Claude model name for vision checks",
    )
    parser.add_argument(
        "--anthropic-key",
        default=os.getenv("ANTHROPIC_API_KEY", ""),
        help="Anthropic API key (or ANTHROPIC_API_KEY env)",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    scenario_path = Path(args.scenario)
    scenario = _load_yaml(str(scenario_path))

    client = BridgeClient(args.host, args.port, args.bridge_timeout)
    if not args.no_healthcheck:
        health = client.health()
        if not isinstance(health, dict) or not health.get("ok", True):
            print("Bridge health check failed:", health)
            return 1

    capture = Win32Capture()
    validator = None
    if args.anthropic_key:
        try:
            validator = VisionValidator(args.anthropic_key, args.anthropic_model)
        except Exception as exc:
            print(f"warning: vision disabled ({exc})")
            validator = None

    run = _execute_scenario(scenario, client, capture, validator)
    ok = all(step["ok"] for step in run["steps"])
    run["overall_ok"] = bool(ok)

    report_path = Path(args.report)
    report_path.parent.mkdir(parents=True, exist_ok=True)
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(run, f, indent=2)

    print(json.dumps(run, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
