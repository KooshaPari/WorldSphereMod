#!/usr/bin/env python3
"""Unit-style checks for vision prompt building and response JSON parsing (no API calls)."""

from __future__ import annotations

import json
import unittest

from vision import build_vision_prompt, parse_vision_response


class VisionParseTests(unittest.TestCase):
    def test_parse_plain_json_object(self) -> None:
        raw = json.dumps({"passes": True, "reason": "ok", "confidence": 0.91})
        result = parse_vision_response(raw)
        self.assertTrue(result["passes"])
        self.assertEqual(result["reason"], "ok")
        self.assertAlmostEqual(result["confidence"], 0.91)

    def test_parse_json_embedded_in_markdown(self) -> None:
        raw = (
            "Here is the evaluation:\n"
            "```json\n"
            '{"passes": false, "reason": "missing HUD", "confidence": 0.4}\n'
            "```"
        )
        result = parse_vision_response(raw)
        self.assertFalse(result["passes"])
        self.assertEqual(result["reason"], "missing HUD")

    def test_parse_json_embedded_in_plain_text(self) -> None:
        raw = 'Final answer: {"passes": true, "reason": "scene is clear", "confidence": 0.75}'
        result = parse_vision_response(raw)
        self.assertTrue(result["passes"])
        self.assertEqual(result["reason"], "scene is clear")

    def test_parse_empty_response(self) -> None:
        result = parse_vision_response("   \n  ")
        self.assertFalse(result["passes"])
        self.assertIn("empty", result["reason"])

    def test_parse_non_json_text(self) -> None:
        result = parse_vision_response("The screenshot looks fine.")
        self.assertFalse(result["passes"])
        self.assertTrue(
            "not json" in result["reason"] or "parse failed" in result["reason"],
            msg=result["reason"],
        )

    def test_parse_textual_passes_fallback(self) -> None:
        result = parse_vision_response("Vision result: passes true. No blocking overlays.")
        self.assertTrue(result["passes"])
        self.assertAlmostEqual(result["confidence"], 0.5)

    def test_parse_invalid_json_fragment(self) -> None:
        result = parse_vision_response("{passes: true}")
        self.assertFalse(result["passes"])
        self.assertIn("parse failed", result["reason"])

    def test_parse_non_object_json(self) -> None:
        result = parse_vision_response("[true, false]")
        self.assertFalse(result["passes"])
        self.assertIn("not an object", result["reason"])

    def test_build_prompt_includes_criteria(self) -> None:
        prompt = build_vision_prompt("Check bridge UI", {"no_error_dialog": True})
        self.assertIn("Check bridge UI", prompt)
        self.assertIn("no_error_dialog", prompt)
        self.assertIn("passes", prompt)


if __name__ == "__main__":
    unittest.main()
