#!/usr/bin/env python3
"""
WorldSphereMod3D PlayCUA runner.

Executes YAML scenario scripts that drive the in-game BridgeRPC server and validate
both telemetry and screenshot content with OmniRoute or Anthropic vision backends.

Supported step actions:
  - health
  - load_save
  - wait_n_frames
  - toggle_flag
  - set_setting
  - screenshot
  - assert_telemetry
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any, Dict, List, Tuple

if sys.platform == "win32":
    from ctypes import wintypes as _wintypes
else:
    _wintypes = None  # type: ignore[assignment]

def _load_env_file(filename: str) -> None:
    env_file = Path(__file__).resolve().parent.parent / filename
    if not env_file.is_file():
        return
    for line in env_file.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or "=" not in stripped:
            continue
        key, value = stripped.split("=", 1)
        key = key.strip()
        value = value.strip()
        if key and key not in os.environ:
            os.environ[key] = value


_load_env_file("omniroute-vision.env")
_load_env_file("fireworks-vision.env")

from vision import (
    FireworksVisionValidator,
    OmniRouteVisionValidator,
    VisionValidationError,
    VisionValidator,
    VisionValidatorProtocol,
)

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
        retriable_get = method.upper() == "GET" and path in ("/health", "/telemetry")
        attempts = 2 if retriable_get else 1
        last_error: urllib.error.URLError | None = None

        for attempt in range(attempts):
            request = urllib.request.Request(
                self._url(path, params),
                method=method.upper(),
            )
            request.add_header("Accept", "application/json")
            try:
                with urllib.request.urlopen(request, timeout=self.timeout) as resp:
                    body = resp.read().decode("utf-8")
                    data = json.loads(body) if body else {}
                    if data is None:
                        return {"ok": False, "error": "null_response", "raw": body[:120]}
                    if not isinstance(data, dict):
                        return {"ok": False, "error": f"non-dict response: {body[:120]}"}
                    return data
            except urllib.error.URLError as exc:
                last_error = exc
                if not retriable_get or attempt >= attempts - 1:
                    raise
                time.sleep(1.0)

        if last_error is not None:
            raise last_error
        raise RuntimeError("request failed without error")

    def health(self) -> Dict[str, Any]:
        return self._request_json("GET", "/health")

    def telemetry(self) -> Dict[str, Any]:
        return self._request_json("GET", "/telemetry")

    def load_save(self, slot: int) -> Dict[str, Any]:
        return self._request_json("POST", "/actions/load_save", {"slot": slot})

    def toggle_flag(self, key: str, value: bool) -> Dict[str, Any]:
        return self._request_json("POST", f"/settings/{key}", {"value": str(value).lower()})

    def set_setting(self, key: str, value: str) -> Dict[str, Any]:
        return self._request_json("POST", f"/settings/{urllib.parse.quote(key, safe='')}", {"value": value})


def _run_wait_n_frames(frames: int, fps: float) -> None:
    frame_delay = 1.0 / max(float(fps), 1.0)
    for _ in range(max(int(frames), 0)):
        time.sleep(frame_delay)


def _wait_bridge_alive(client: "BridgeClient", timeout_s: float = 90.0) -> bool:
    deadline = time.time() + max(timeout_s, 1.0)
    while time.time() < deadline:
        try:
            health = client.health()
            if health.get("ok") and health.get("bridgeAlive"):
                return True
        except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError):
            pass
        time.sleep(2.0)
    return False


class Win32ScreenshotError(RuntimeError):
    pass


def is_worldbox_process_name(process_name: str) -> bool:
    """Return True for WorldBox process names (worldbox.exe / WorldBox)."""
    normalized = process_name.strip().lower()
    if normalized.endswith(".exe"):
        normalized = normalized[:-4]
    return normalized == "worldbox"


def is_worldbox_window_title(title: str) -> bool:
    return "worldbox" in title.strip().lower()


def matches_worldbox_window(title: str, process_name: str) -> bool:
    return is_worldbox_window_title(title) or is_worldbox_process_name(process_name)


class Win32Capture:
    """Capture screenshots using native Win32 GDI APIs."""

    SRCCOPY = 0x00CC0020
    DIB_RGB_COLORS = 0
    GW_OWNER = 4
    PW_CLIENTONLY = 0x00000001
    TH32CS_SNAPPROCESS = 0x00000002

    def __init__(self) -> None:
        try:
            import ctypes
            self.ctypes = ctypes
        except Exception as exc:
            raise Win32ScreenshotError("ctypes not available in this Python runtime") from exc
        self.last_capture_target = "desktop"

    def capture(self, out_path: Path) -> Path:
        if sys.platform == "win32":
            hwnd = self._find_worldbox_hwnd()
            if hwnd:
                try:
                    self.last_capture_target = "worldbox_window"
                    return self._capture_window_client(hwnd, out_path)
                except Win32ScreenshotError:
                    pass
        self.last_capture_target = "desktop"
        return self._capture_desktop(out_path)

    def _load_dlls(self) -> Tuple[Any, Any]:
        c = self.ctypes
        return (
            c.WinDLL("user32", use_last_error=True),
            c.WinDLL("gdi32", use_last_error=True),
        )

    def _bitmap_structures(self) -> Tuple[Any, Any, Any]:
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
            _fields_ = [
                ("rgbBlue", c.c_ubyte),
                ("rgbGreen", c.c_ubyte),
                ("rgbRed", c.c_ubyte),
                ("rgbReserved", c.c_ubyte),
            ]

        class BITMAPINFO(c.Structure):
            _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", RGBQUAD * 1)]

        return BITMAPINFOHEADER, RGBQUAD, BITMAPINFO

    def _find_worldbox_process_ids(self, kernel32: Any) -> set[int]:
        c = self.ctypes
        wintypes = _wintypes

        class PROCESSENTRY32W(c.Structure):
            _fields_ = [
                ("dwSize", wintypes.DWORD),
                ("cntUsage", wintypes.DWORD),
                ("th32ProcessID", wintypes.DWORD),
                ("th32DefaultHeapID", c.c_size_t),
                ("th32ModuleID", wintypes.DWORD),
                ("cntThreads", wintypes.DWORD),
                ("th32ParentProcessID", wintypes.DWORD),
                ("pcPriClassBase", wintypes.LONG),
                ("dwFlags", wintypes.DWORD),
                ("szExeFile", wintypes.WCHAR * 260),
            ]

        snapshot = kernel32.CreateToolhelp32Snapshot(self.TH32CS_SNAPPROCESS, 0)
        if snapshot in (-1, 0xFFFFFFFF):
            return set()

        entry = PROCESSENTRY32W()
        entry.dwSize = c.sizeof(PROCESSENTRY32W)
        pids: set[int] = set()
        try:
            if not kernel32.Process32FirstW(snapshot, c.byref(entry)):
                return pids
            while True:
                if is_worldbox_process_name(entry.szExeFile):
                    pids.add(int(entry.th32ProcessID))
                if not kernel32.Process32NextW(snapshot, c.byref(entry)):
                    break
        finally:
            kernel32.CloseHandle(snapshot)
        return pids

    def _find_worldbox_hwnd(self) -> int | None:
        c = self.ctypes
        wintypes = _wintypes
        user32, _ = self._load_dlls()
        kernel32 = c.WinDLL("kernel32", use_last_error=True)

        class RECT(c.Structure):
            _fields_ = [
                ("left", c.c_long),
                ("top", c.c_long),
                ("right", c.c_long),
                ("bottom", c.c_long),
            ]

        worldbox_pids = self._find_worldbox_process_ids(kernel32)
        best_hwnd: int | None = None
        best_area = 0

        def _read_window_title(hwnd: int) -> str:
            length = user32.GetWindowTextLengthW(hwnd)
            if length <= 0:
                return ""
            buf = c.create_unicode_buffer(length + 1)
            user32.GetWindowTextW(hwnd, buf, length + 1)
            return buf.value

        @c.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
        def enum_proc(hwnd: int, _lparam: int) -> bool:
            nonlocal best_hwnd, best_area
            if not user32.IsWindow(hwnd):
                return True
            if user32.GetWindow(hwnd, self.GW_OWNER):
                return True
            if user32.IsIconic(hwnd):
                return True

            pid = wintypes.DWORD()
            user32.GetWindowThreadProcessId(hwnd, c.byref(pid))
            title = _read_window_title(hwnd)
            is_worldbox_pid = pid.value in worldbox_pids
            if not is_worldbox_pid and not is_worldbox_window_title(title):
                return True

            rect = RECT()
            if not user32.GetWindowRect(hwnd, c.byref(rect)):
                return True
            width = max(0, rect.right - rect.left)
            height = max(0, rect.bottom - rect.top)
            area = width * height
            if area <= 0:
                return True
            if area > best_area:
                best_area = area
                best_hwnd = int(hwnd)
            return True

        user32.EnumWindows(enum_proc, 0)
        return best_hwnd

    def _write_png(self, width: int, height: int, pixel_buf: Any, out_path: Path) -> Path:
        out_path.parent.mkdir(parents=True, exist_ok=True)
        try:
            from PIL import Image
        except Exception as exc:
            raise Win32ScreenshotError(
                "Pillow is required for PNG encoding; install with `pip install pillow`."
            ) from exc

        image = Image.frombuffer("RGBA", (width, height), pixel_buf, "raw", "BGRA", 0, 1)
        image.save(out_path, format="PNG")
        return out_path

    def _read_bitmap_bits(
        self,
        gdi32: Any,
        memdc: Any,
        hbmp: Any,
        width: int,
        height: int,
        BITMAPINFOHEADER: Any,
        BITMAPINFO: Any,
    ) -> Any:
        c = self.ctypes
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
        pixel_buf = c.create_string_buffer(width * height * 4)
        got = gdi32.GetDIBits(
            memdc,
            hbmp,
            0,
            height,
            c.byref(pixel_buf),
            c.byref(bmi),
            self.DIB_RGB_COLORS,
        )
        if got != height:
            raise Win32ScreenshotError("GetDIBits returned unexpected row count")
        return pixel_buf

    def _capture_window_client(self, hwnd: int, out_path: Path) -> Path:
        c = self.ctypes
        user32, gdi32 = self._load_dlls()
        BITMAPINFOHEADER, _, BITMAPINFO = self._bitmap_structures()

        class RECT(c.Structure):
            _fields_ = [
                ("left", c.c_long),
                ("top", c.c_long),
                ("right", c.c_long),
                ("bottom", c.c_long),
            ]

        client_rect = RECT()
        if not user32.GetClientRect(hwnd, c.byref(client_rect)):
            raise Win32ScreenshotError("failed to get WorldBox client rect")
        width = client_rect.right - client_rect.left
        height = client_rect.bottom - client_rect.top
        if width <= 0 or height <= 0:
            raise Win32ScreenshotError("WorldBox client bounds were empty")

        hdc_window = user32.GetWindowDC(hwnd)
        if not hdc_window:
            raise Win32ScreenshotError("failed to get WorldBox window DC")

        memdc = gdi32.CreateCompatibleDC(hdc_window)
        if not memdc:
            user32.ReleaseDC(hwnd, hdc_window)
            raise Win32ScreenshotError("failed to create memory DC")

        hbmp = gdi32.CreateCompatibleBitmap(hdc_window, width, height)
        if not hbmp:
            gdi32.DeleteDC(memdc)
            user32.ReleaseDC(hwnd, hdc_window)
            raise Win32ScreenshotError("failed to create compatible bitmap")

        old_obj = gdi32.SelectObject(memdc, hbmp)
        copied = bool(user32.PrintWindow(hwnd, memdc, self.PW_CLIENTONLY))
        if not copied:
            copied = bool(
                gdi32.BitBlt(memdc, 0, 0, width, height, hdc_window, 0, 0, self.SRCCOPY)
            )
        if not copied:
            gdi32.SelectObject(memdc, old_obj)
            gdi32.DeleteObject(hbmp)
            gdi32.DeleteDC(memdc)
            user32.ReleaseDC(hwnd, hdc_window)
            raise Win32ScreenshotError("failed to copy WorldBox window pixels")

        try:
            pixel_buf = self._read_bitmap_bits(
                gdi32, memdc, hbmp, width, height, BITMAPINFOHEADER, BITMAPINFO
            )
            return self._write_png(width, height, pixel_buf, out_path)
        finally:
            gdi32.SelectObject(memdc, old_obj)
            gdi32.DeleteObject(hbmp)
            gdi32.DeleteDC(memdc)
            user32.ReleaseDC(hwnd, hdc_window)

    def _capture_desktop(self, out_path: Path) -> Path:
        c = self.ctypes
        user32, gdi32 = self._load_dlls()
        BITMAPINFOHEADER, _, BITMAPINFO = self._bitmap_structures()

        SM_CXSCREEN = 0
        SM_CYSCREEN = 1

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
        if not gdi32.BitBlt(memdc, 0, 0, width, height, hdc, 0, 0, self.SRCCOPY):
            gdi32.SelectObject(memdc, old_obj)
            gdi32.DeleteObject(hbmp)
            gdi32.DeleteDC(memdc)
            user32.ReleaseDC(hwnd, hdc)
            raise Win32ScreenshotError("BitBlt failed while copying screen")

        try:
            pixel_buf = self._read_bitmap_bits(
                gdi32, memdc, hbmp, width, height, BITMAPINFOHEADER, BITMAPINFO
            )
            return self._write_png(width, height, pixel_buf, out_path)
        finally:
            gdi32.SelectObject(memdc, old_obj)
            gdi32.DeleteObject(hbmp)
            gdi32.DeleteDC(memdc)
            user32.ReleaseDC(hwnd, hdc)


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


def _assert_health(raw_step: Dict[str, Any], client: BridgeClient) -> Tuple[bool, Dict[str, Any]]:
    payload = client.health()
    details: Dict[str, Any] = {"health": payload}

    if not isinstance(payload.get("ok"), bool):
        details["error"] = "health ok field missing or non-boolean"
        return False, details
    if not payload.get("ok", False):
        details["error"] = "health returned ok=false"
        return False, details

    expect_3d = raw_step.get("expect_is_world_3d", raw_step.get("isWorld3D"))
    if expect_3d is not None and _to_bool(expect_3d):
        if not _to_bool(payload.get("isWorld3D", False)):
            details["error"] = "health isWorld3D check failed"
            return False, details

    version = str(payload.get("version") or "").strip()
    if not version:
        details["error"] = "health version missing"
        return False, details

    details["isWorld3D"] = payload.get("isWorld3D")
    details["version"] = version
    return True, details


def _execute_scenario(
    scenario: Dict[str, Any],
    client: BridgeClient,
    screenshot: Win32Capture,
    validator: VisionValidatorProtocol | None,
    vision_backend: str = "off",
) -> Dict[str, Any]:
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
        optional = bool(raw_step.get("optional", False))
        if action == "health":
            required = bool(raw_step.get("required", True))
            ok, details = _assert_health(raw_step, client)
            if required and not ok:
                raise AssertionError(f"health step #{index} failed: {details}")
        elif action == "load_save":
            slot = int(raw_step.get("slot", raw_step.get("save_slot", 0)))
            if slot < 0:
                raise ValueError(f"load_save step #{index} requires non-negative slot")
            payload = client.load_save(slot)
            ok = bool(payload.get("ok"))
            details = dict(payload)
            if ok and payload.get("queued"):
                settle_frames = int(raw_step.get("settle_frames", 150))
                settle_fps = float(raw_step.get("settle_fps", raw_step.get("fps", 30)))
                _run_wait_n_frames(settle_frames, settle_fps)
                details["settle_frames"] = settle_frames
                details["settle_fps"] = settle_fps
                bridge_timeout = float(raw_step.get("bridge_wait_seconds", 90))
                details["bridge_alive"] = _wait_bridge_alive(client, bridge_timeout)
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
        elif action == "set_setting":
            key = str(raw_step.get("key") or raw_step.get("setting"))
            raw_value = raw_step.get("value")
            if not key:
                raise ValueError(f"set_setting step #{index} missing key")
            if raw_value is None:
                raise ValueError(f"set_setting step #{index} missing value")
            if isinstance(raw_value, bool):
                value_text = str(raw_value).lower()
            elif isinstance(raw_value, (int, float)):
                value_text = format(_coerce_number(raw_value), "g")
            else:
                value_text = str(raw_value)
            payload = client.set_setting(key, value_text)
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
            details = {
                "path": str(img_path),
                "capture_target": screenshot.last_capture_target,
            }

            vision = raw_step.get("vision") or {}
            if vision:
                prompt = vision.get("prompt", "Check whether this screenshot satisfies the expected criteria.")
                criteria = vision.get("criteria")
                required = bool(vision.get("required", True))
                if validator is None:
                    if required and vision_backend != "off":
                        raise RuntimeError("screenshot vision check required but no vision backend available")
                    details["vision"] = {
                        "skipped": True,
                        "reason": "vision backend off"
                        if vision_backend == "off"
                        else "validator unavailable",
                    }
                else:
                    try:
                        result = validator.validate(img_path, prompt, criteria)
                    except VisionValidationError as exc:
                        details["vision"] = {
                            "ok": False,
                            "required": required,
                            "status": "failed" if required else "skipped",
                            "reason": str(exc),
                        }
                        if required:
                            raise
                    else:
                        details["vision"] = {
                            "ok": bool(result.get("passes", False)),
                            "required": required,
                            "status": "passed" if result.get("passes", False) else "failed",
                            "result": result,
                        }
                        if required:
                            ok = bool(result.get("passes", False))
                            if not ok:
                                details["error"] = result.get("reason", "vision criteria failed")
        elif action == "assert_telemetry":
            required = bool(raw_step.get("required", True))
            ok, details = _assert_telemetry(raw_step, client)
            if required and not ok:
                raise AssertionError(f"assert_telemetry step #{index} failed: {details}")
        else:
            raise ValueError(f"unknown action at step #{index}: {action}")

        if optional and not ok:
            ok = True
            if isinstance(details, dict):
                details = {**details, "skipped": True, "reason": "optional step did not pass"}
            else:
                details = {"skipped": True, "reason": "optional step did not pass", "details": details}

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


def _default_vision_backend() -> str:
    explicit = (os.getenv("PLAYCUA_VISION_BACKEND") or "").strip().lower()
    if explicit in {"fireworks", "omniroute", "anthropic", "off"}:
        return explicit
    if os.getenv("FIREWORKS_API_KEY", "").strip():
        return "fireworks"
    if os.getenv("OMNROUTE_API_KEY", "").strip():
        return "omniroute"
    if os.getenv("ANTHROPIC_API_KEY", "").strip():
        return "anthropic"
    return "off"


def _fireworks_model_from_env() -> str:
    return (os.getenv("FIREWORKS_VISION_MODEL") or "accounts/fireworks/models/kimi-k2p5").strip()


def _omniroute_model_from_env() -> str:
    return (os.getenv("OMNROUTE_VISION_MODEL") or os.getenv("OMNROUTE_VISION_COMBO") or "").strip()


def _create_vision_validator(args: argparse.Namespace) -> VisionValidatorProtocol | None:
    backend = args.vision_backend or _default_vision_backend()
    if backend == "off":
        return None
    if backend == "omniroute":
        return OmniRouteVisionValidator(
            api_key=args.omniroute_key,
            base_url=args.omniroute_base_url,
            model=args.omniroute_model,
            timeout_s=args.omniroute_timeout,
        )
    if backend == "fireworks":
        return FireworksVisionValidator(
            api_key=args.fireworks_key,
            base_url=args.fireworks_base_url,
            model=args.fireworks_model,
            timeout_s=args.fireworks_timeout,
        )
    if backend == "anthropic":
        if not args.anthropic_key:
            raise VisionValidationError("Anthropic vision selected but ANTHROPIC_API_KEY is missing")
        return VisionValidator(args.anthropic_key, args.anthropic_model)
    raise ValueError(f"unknown vision backend: {backend}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run WorldSphereMod3D PlayCUA scenario(s)")
    parser.add_argument("scenario", help="Path to YAML scenario")
    parser.add_argument("--host", default="127.0.0.1", help="BridgeRPC host")
    parser.add_argument("--port", type=int, default=8766, help="BridgeRPC port")
    parser.add_argument("--bridge-timeout", type=float, default=30.0, help="HTTP timeout seconds")
    parser.add_argument("--no-healthcheck", action="store_true", help="Skip bridge healthcheck")
    parser.add_argument(
        "--report",
        default="Tools/wsm3d-playcua/.reports/latest.json",
        help="Report output path",
    )
    parser.add_argument(
        "--vision-backend",
        choices=["fireworks", "omniroute", "anthropic", "off"],
        default=None,
        help=(
            "Vision provider (default: fireworks if FIREWORKS_API_KEY set, "
            "else omniroute if OMNROUTE_API_KEY set, else anthropic, else off)"
        ),
    )
    parser.add_argument(
        "--fireworks-base-url",
        default=os.getenv("FIREWORKS_BASE_URL", "https://api.fireworks.ai/inference/v1"),
        help="Fireworks OpenAI-compatible base URL (or FIREWORKS_BASE_URL)",
    )
    parser.add_argument(
        "--fireworks-key",
        default=os.getenv("FIREWORKS_API_KEY", ""),
        help="Fireworks API key (or FIREWORKS_API_KEY env / User scope on Windows)",
    )
    parser.add_argument(
        "--fireworks-model",
        default=_fireworks_model_from_env(),
        help="Fireworks vision model (FIREWORKS_VISION_MODEL, default kimi-k2p5)",
    )
    parser.add_argument(
        "--fireworks-timeout",
        type=float,
        default=120.0,
        help="HTTP timeout seconds for Fireworks vision requests",
    )
    parser.add_argument(
        "--omniroute-base-url",
        default=os.getenv("OMNROUTE_BASE_URL", "http://127.0.0.1:20128/v1"),
        help="OmniRoute OpenAI-compatible base URL (or OMNROUTE_BASE_URL)",
    )
    parser.add_argument(
        "--omniroute-key",
        default=os.getenv("OMNROUTE_API_KEY", ""),
        help="OmniRoute API key (or OMNROUTE_API_KEY env)",
    )
    parser.add_argument(
        "--omniroute-model",
        default=_omniroute_model_from_env(),
        help="OmniRoute model or combo (OMNROUTE_VISION_MODEL or OMNROUTE_VISION_COMBO)",
    )
    parser.add_argument(
        "--omniroute-timeout",
        type=float,
        default=300.0,
        help="HTTP timeout seconds for OmniRoute vision requests",
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
        if (
            not isinstance(health, dict)
            or "ok" not in health
            or not isinstance(health.get("ok"), bool)
            or not health.get("ok")
        ):
            print("Bridge health check failed:", health)
            return 1

    capture = Win32Capture()
    backend = args.vision_backend or _default_vision_backend()
    try:
        validator = _create_vision_validator(args)
    except Exception as exc:
        print(f"warning: vision disabled for backend={backend} ({exc})")
        validator = None

    run = _execute_scenario(scenario, client, capture, validator, vision_backend=backend)
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
