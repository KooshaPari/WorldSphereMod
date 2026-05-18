"""Settings tools: get, set, toggle SavedSettings.json."""

import json
import logging
from wsm3d_mcp.paths import get_saved_settings_path

logger = logging.getLogger("wsm3d_mcp")


async def settings_get(key: str | None = None) -> dict:
    """
    Read SavedSettings.json.
    If key is None, return full dict.
    If key is provided, return that field value.
    Returns {settings: dict | any, error: str | None}
    """
    try:
        path = get_saved_settings_path()
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)

        if key is None:
            return {"settings": data}

        if key in data:
            return {"settings": {key: data[key]}}

        return {"settings": None, "error": f"Key not found: {key}"}
    except Exception as e:
        logger.error(f"settings_get error: {e}")
        return {"settings": None, "error": str(e)}


async def settings_set(key: str, value: str | int | bool | float) -> dict:
    """
    Set a field in SavedSettings.json.
    Validates field exists before writing.
    Returns {ok: bool, old: any, new: any}
    """
    try:
        path = get_saved_settings_path()
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)

        if key not in data:
            return {"ok": False, "error": f"Key not found in settings: {key}"}

        old = data[key]
        data[key] = value

        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)

        logger.info(f"Set {key} = {value} (was {old})")
        return {"ok": True, "old": old, "new": value}
    except Exception as e:
        logger.error(f"settings_set error: {e}")
        return {"ok": False, "error": str(e)}


async def settings_toggle(phase: str) -> dict:
    """
    Toggle a phase setting (bool field).
    Accepts snake_case OR camelCase.
    Returns {ok: bool, old: bool, new: bool}
    """
    try:
        path = get_saved_settings_path()
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)

        # Try both case variants
        key = None
        for k in data:
            if k.lower() == phase.lower():
                key = k
                break

        if key is None:
            return {"ok": False, "error": f"Phase not found: {phase}"}

        if not isinstance(data[key], bool):
            return {"ok": False, "error": f"Phase {key} is not a boolean"}

        old = data[key]
        data[key] = not old

        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)

        logger.info(f"Toggled {key}: {old} → {data[key]}")
        return {"ok": True, "old": old, "new": data[key]}
    except Exception as e:
        logger.error(f"settings_toggle error: {e}")
        return {"ok": False, "error": str(e)}
