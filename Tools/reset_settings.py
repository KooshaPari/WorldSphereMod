"""Reset WorldSphereMod settings JSON to lightweight Phase-1 defaults.

Sets all phase booleans to false EXCEPT VoxelEntities=true.
Preserves Is3D=true, CurrentShape=1, VoxelScaleMultiplier=8.0.
"""
import json
import os
import sys

SETTINGS_PATH = os.path.join(
    os.environ["USERPROFILE"],
    "AppData", "LocalLow", "mkarpenko", "WorldBox",
    "mods_config", "WorldSphereMod.json",
)

PHASES_OFF = {
    "ProceduralBuildings": False,
    "CrossedQuadFoliage": False,
    "BiomeBlending": False,
    "MeshWater": False,
    "WorldspaceHealth3D": False,
    "MountainSlopeSmoothing": False,
    "HighShadows": False,
    "HdrSkybox": False,
    "ColorGradingLut": False,
    "SkeletalAnimation": False,
    "WorldspaceUI": False,
    "WorldspaceLabel3D": False,
    "DayNightCycle": False,
    "PostFX": False,
    "SSAOEnabled": False,
    "SSGIEnabled": False,
    "ParticleEffects": False,
    "WeatherRain": False,
    "DebugSanityCube": False,
    "ProfilerDump": False,
    "BloomEnabled": False,
    "ACESTonemapping": False,
}

KEEP = {
    "VoxelEntities": True,
    "Is3D": True,
    "CurrentShape": 1,
    "VoxelScaleMultiplier": 8.0,
}


def main():
    if not os.path.isfile(SETTINGS_PATH):
        print(f"ERROR: settings file not found at {SETTINGS_PATH}", file=sys.stderr)
        sys.exit(1)

    with open(SETTINGS_PATH, "r", encoding="utf-8") as f:
        settings = json.load(f)

    for key, val in PHASES_OFF.items():
        if key in settings:
            settings[key] = val
        else:
            print(f"WARN: key '{key}' not present in JSON, adding it")
            settings[key] = val

    for key, val in KEEP.items():
        settings[key] = val

    with open(SETTINGS_PATH, "w", encoding="utf-8") as f:
        json.dump(settings, f, indent=2, ensure_ascii=False)
        f.write("\n")

    print(f"OK: wrote {SETTINGS_PATH}")
    print("Phases OFF:", ", ".join(PHASES_OFF.keys()))
    print("Kept ON/pinned:", ", ".join(f"{k}={v}" for k, v in KEEP.items()))


if __name__ == "__main__":
    main()
