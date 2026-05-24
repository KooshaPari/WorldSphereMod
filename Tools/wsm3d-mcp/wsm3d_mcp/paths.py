"""Path constants and discovery for WorldSphereMod3D."""

import json
import logging
import os
from pathlib import Path

logger = logging.getLogger("wsm3d_mcp")

# Repo root
_HERE = Path(__file__).resolve().parent.parent.parent
REPO_ROOT = _HERE.resolve()

# Game install — uses WORLDBOX_PATH env var or default Steam path
_DEFAULT_GAME_DIR = Path(
    "C:/Program Files (x86)/Steam/steamapps/common/worldbox"
    if os.name == "nt"
    else Path.home() / ".steam/steam/steamapps/common/worldbox"
)
GAME_DIR = Path(os.environ.get("WORLDBOX_PATH", str(_DEFAULT_GAME_DIR)))
GAME_EXE = GAME_DIR / "worldbox.exe"
MOD_INSTALL_DIR = GAME_DIR / "Mods/WorldSphereMod3D"

# Player log — discovers via home directory (cross-platform)
PLAYER_LOG = Path.home() / "AppData/LocalLow/mkarpenko/WorldBox/Player.log"

# Save settings JSON (cached after first discovery)
_SAVED_SETTINGS_PATH: Path | None = None


def get_saved_settings_path() -> Path:
    """
    Locate SavedSettings.json by searching %USERPROFILE%/AppData/LocalLow/mkarpenko/WorldBox/.
    Caches result after first discovery.
    """
    global _SAVED_SETTINGS_PATH
    if _SAVED_SETTINGS_PATH:
        return _SAVED_SETTINGS_PATH

    base = Path.home() / "AppData/LocalLow/mkarpenko/WorldBox"
    if not base.exists():
        raise FileNotFoundError(f"WorldBox appdata not found: {base}")

    found = list(base.glob("**/WorldSphereMod.json"))
    if not found:
        raise FileNotFoundError(
            f"SavedSettings (WorldSphereMod.json) not found under {base}"
        )

    _SAVED_SETTINGS_PATH = found[0]
    logger.info(f"Discovered SavedSettings at {_SAVED_SETTINGS_PATH}")
    return _SAVED_SETTINGS_PATH


def validate_paths() -> dict[str, bool]:
    """Validate key paths exist. Returns {path_name: exists}."""
    return {
        "game_exe": GAME_EXE.exists(),
        "game_dir": GAME_DIR.exists(),
        "player_log": PLAYER_LOG.exists(),
        "repo_root": REPO_ROOT.exists(),
    }
