"""Vision validation backends for PlayCUA screenshot checks."""

from __future__ import annotations

import base64
import json
import re
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any, Dict, Protocol


class VisionValidationError(RuntimeError):
    pass


def build_vision_prompt(prompt: str, criteria: Any) -> str:
    block = json.dumps(criteria, indent=2, sort_keys=True) if criteria else "{}"
    return (
        "You are a strict visual regression checker for an end-to-end automation pipeline. "
        "Evaluate the screenshot against the exact criteria and return only JSON. "
        "The JSON shape must be: "
        '{"passes": bool, "reason": "text", "confidence": 0.0-1.0}.'
        "\n"
        f"Scenario prompt: {prompt}\n"
        f"Expected criteria:\n{block}"
    )


def parse_vision_response(full: str) -> Dict[str, Any]:
    """Parse model text into {passes, reason, confidence} contract."""
    if not full.strip():
        return {"passes": False, "reason": "empty vision model response"}

    text = full.strip()
    candidates = [text]
    match = re.search(r"\{.*\}", text, re.S)
    if match:
        candidates.append(match.group(0))

    last_exc: Exception | None = None
    for candidate in candidates:
        try:
            parsed = json.loads(candidate)
        except Exception as exc:
            last_exc = exc
            continue
        if not isinstance(parsed, dict):
            return {"passes": False, "reason": "vision response was not an object"}
        return parsed

    if last_exc is not None:
        return {"passes": False, "reason": f"vision json parse failed: {last_exc}"}
    return {"passes": False, "reason": f"vision response not json: {full[:180]}"}


class VisionValidatorProtocol(Protocol):
    def validate(self, image_path: Path, prompt: str, criteria: Any) -> Dict[str, Any]: ...


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
                raise VisionValidationError(
                    "Anthropic SDK import failed; install with `pip install anthropic`."
                ) from exc

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
                        {"type": "text", "text": build_vision_prompt(prompt, criteria)},
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

        return parse_vision_response(full)


class OmniRouteVisionValidator:
    """OpenAI-compatible chat completions via OmniRoute for vision checks."""

    def __init__(
        self,
        api_key: str | None,
        base_url: str = "http://127.0.0.1:20128/v1",
        model: str | None = None,
        timeout_s: float = 120.0,
    ) -> None:
        self.api_key = (api_key or "").strip()
        self.base_url = base_url.rstrip("/")
        self.model = (model or "").strip()
        self.timeout_s = timeout_s

        if not self.api_key:
            raise VisionValidationError("OmniRoute API key unavailable (set OMNROUTE_API_KEY)")
        if not self.model:
            raise VisionValidationError(
                "OmniRoute vision model unavailable (set OMNROUTE_VISION_MODEL or OMNROUTE_VISION_COMBO)"
            )

    def validate(self, image_path: Path, prompt: str, criteria: Any) -> Dict[str, Any]:
        if not image_path.exists():
            raise VisionValidationError(f"screenshot not found: {image_path}")

        image_data = base64.b64encode(image_path.read_bytes()).decode("ascii")
        payload = {
            "model": self.model,
            "max_tokens": 1024,
            "messages": [
                {
                    "role": "user",
                    "content": [
                        {"type": "text", "text": build_vision_prompt(prompt, criteria)},
                        {
                            "type": "image_url",
                            "image_url": {"url": f"data:image/png;base64,{image_data}"},
                        },
                    ],
                }
            ],
        }

        url = f"{self.base_url}/chat/completions"
        request = urllib.request.Request(
            url,
            data=json.dumps(payload).encode("utf-8"),
            method="POST",
            headers={
                "Content-Type": "application/json",
                "Accept": "application/json",
                "Authorization": f"Bearer {self.api_key}",
            },
        )

        try:
            with urllib.request.urlopen(request, timeout=self.timeout_s) as resp:
                body = resp.read().decode("utf-8")
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")[:240]
            raise VisionValidationError(f"OmniRoute HTTP {exc.code}: {detail}") from exc
        except urllib.error.URLError as exc:
            raise VisionValidationError(f"OmniRoute request failed: {exc}") from exc

        try:
            data = json.loads(body) if body else {}
        except json.JSONDecodeError as exc:
            raise VisionValidationError(f"OmniRoute non-JSON response: {body[:180]}") from exc

        if not isinstance(data, dict):
            raise VisionValidationError("OmniRoute response was not a JSON object")

        choices = data.get("choices")
        if not isinstance(choices, list) or not choices:
            raise VisionValidationError(f"OmniRoute missing choices: {str(data)[:180]}")

        message = choices[0].get("message") if isinstance(choices[0], dict) else None
        if not isinstance(message, dict):
            raise VisionValidationError("OmniRoute choice missing message")

        content = message.get("content", "")
        if isinstance(content, list):
            parts: list[str] = []
            for block in content:
                if isinstance(block, dict) and block.get("type") == "text":
                    parts.append(str(block.get("text", "")))
                elif isinstance(block, str):
                    parts.append(block)
            full = "".join(parts)
        else:
            full = str(content)

        return parse_vision_response(full)
