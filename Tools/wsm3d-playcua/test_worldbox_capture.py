#!/usr/bin/env python3
"""Unit-style checks for WorldBox screenshot targeting heuristics (no Win32 calls)."""

from __future__ import annotations

import unittest

from main import (
    is_worldbox_process_name,
    is_worldbox_window_title,
    matches_worldbox_window,
)


class WorldboxCaptureHeuristicTests(unittest.TestCase):
    def test_process_name_matches_worldbox_exe_variants(self) -> None:
        self.assertTrue(is_worldbox_process_name("worldbox.exe"))
        self.assertTrue(is_worldbox_process_name("WorldBox.exe"))
        self.assertTrue(is_worldbox_process_name("worldbox"))
        self.assertTrue(is_worldbox_process_name("WorldBox"))

    def test_process_name_rejects_unrelated_processes(self) -> None:
        self.assertFalse(is_worldbox_process_name("steam.exe"))
        self.assertFalse(is_worldbox_process_name("chrome.exe"))
        self.assertFalse(is_worldbox_process_name("Diplomacy is Not an Option.exe"))

    def test_window_title_matches_worldbox(self) -> None:
        self.assertTrue(is_worldbox_window_title("WorldBox"))
        self.assertTrue(is_worldbox_window_title("WorldBox - God Simulator"))
        self.assertFalse(is_worldbox_window_title("Diplomacy is Not an Option"))
        self.assertFalse(is_worldbox_window_title("Steam"))

    def test_matches_worldbox_window_accepts_title_or_process(self) -> None:
        self.assertTrue(matches_worldbox_window("WorldBox", "steam.exe"))
        self.assertTrue(matches_worldbox_window("", "worldbox.exe"))
        self.assertFalse(matches_worldbox_window("Steam Store", "steam.exe"))


if __name__ == "__main__":
    unittest.main()
