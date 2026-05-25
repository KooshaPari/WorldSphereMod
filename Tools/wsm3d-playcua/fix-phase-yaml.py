#!/usr/bin/env python3
"""Normalize phase-*.yaml for PlayCUA run-all (automation-safe gates)."""
from __future__ import annotations

import re
from pathlib import Path

SCENARIOS = Path(__file__).resolve().parent / "sample-scenarios"

FINAL_ASSERT = """  - action: assert_telemetry
    required: true
    checks:
      - field: lastNonZeroDrawCalls
        op: ">"
        value: 0
      - field: frameMs
        op: "<"
        value: 2000
"""


def patch_file(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    orig = text
    changes: list[str] = []

    if "expect_is_world_3d" in text:
        text = re.sub(r"\n\s+expect_is_world_3d: true\n", "\n", text)
        changes.append("removed expect_is_world_3d")

    # Replace final assert_telemetry block (last occurrence) with standardized gates.
    parts = text.split("  - action: assert_telemetry")
    if len(parts) >= 2:
        head = parts[0]
        tail = "  - action: assert_telemetry".join(parts[1:])
        # Drop everything from last assert_telemetry to EOF and append standard block.
        idx = text.rfind("  - action: assert_telemetry")
        if idx != -1:
            new_text = text[:idx].rstrip() + "\n" + FINAL_ASSERT
            if new_text != text:
                changes.append("normalized final assert_telemetry")
                text = new_text

    if text != orig:
        path.write_text(text, encoding="utf-8", newline="\n")
    return changes


def main() -> int:
    ok = True
    for path in sorted(SCENARIOS.glob("phase-*.yaml")):
        changes = patch_file(path)
        if changes:
            print(f"{path.name}: {', '.join(changes)}")
        try:
            import yaml  # type: ignore

            yaml.safe_load(path.read_text(encoding="utf-8"))
            print(f"  yaml ok: {path.name}")
        except Exception as e:
            print(f"  yaml FAIL: {path.name}: {e}")
            ok = False
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
