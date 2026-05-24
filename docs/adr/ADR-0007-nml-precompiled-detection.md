# ADR-0007: NML precompiled detection signal

**Status:** Proposed  
**Date:** 2026-05-19  
**Author:** WorldSphereMod agent  
**Stakeholders:** WorldSphereMod install/build pipeline, NeoModLoader integration

---

## Context

The user requested a definitive signal for the `[NML]: <uid> detected as precompiled, compilation phase will be skipped` log line to decide whether a mod must be considered precompiled by NML before shipping runtime assemblies from `install.ps1`.

## Findings

- In current logs under:
  - `C:/Users/koosh/AppData/LocalLow/mkarpenko/WorldBox/Player.log`
  - `C:/Users/koosh/AppData/LocalLow/mkarpenko/WorldBox/logs/*.log`

  entries consistently appear as:
  - `[NML]: KEYMASTERER_BUILDINGANDRACECOMPATIBILITYFIX detected as precompiled, compilation phase will be skipped on it!`
  - `[NML]: KEYMASTERER__DON_NIKON_POWERBOX detected as precompiled, compilation phase will be skipped on it!`

- I decompiled `NeoModLoader.dll` type `NeoModLoader.services.ModCompileLoadService` and found:
  - `compileMod` checks `Directory.GetFiles(pModNode.mod_decl.FolderPath).Any(file => file.EndsWith(".dll"))`.
  - If true, it logs exactly `"<uid> detected as precompiled, compilation phase will be skipped on it!"` and sets mod type to `ModTypeEnum.COMPILED_NEOMOD`.
  - Otherwise it compiles `.cs` sources and writes output DLL/PDB into `CompiledMods`.
- Scanning `C:/Program Files (x86)/Steam/steamapps/common/worldbox/worldbox_Data/StreamingAssets/Mods` shows:
  - `BuildingAndRaceCompatibilityFix` has a root `BuildingAndRaceCompatibilityFix.dll` and `mod.json`.
  - `NML/CompiledMods/KEYMASTERER__IFAILEDLIFE__DON_NIKON_POWERBOX.dll` exists and has a matching `mod_compile_records.json` entry.
- `mod.json` fields in the scanned case did not affect the branch condition directly; precompiled detection is based on filesystem contents, not `startPoint`, dependencies, or ID matching.
- `NeoModLoader metadata` currently resolves as:
  - Assembly: `NeoModLoader, Version=1.2.0.1`
  - File version: `1.2.0.1`

## Decision

NML treats a mod as precompiled for the startup compile step when **any `.dll` file exists in the mod’s install folder**. The signal is a filesystem condition on `mod_decl.FolderPath`, not a `mod.json` field match.

## Recommended next steps

1. Keep current source-first install behavior (do not ship source `dll` copies alongside `.cs`) until `mod_type` policy is explicitly changed.
2. If future behavior requires shipping precompiled artifacts, only do so after a dedicated ADR/plan that documents collision risks with NeoModLoader’s own runtime compile cache (`CompiledMods`) and duplicate load behavior.
3. Optionally add a small debug assertion in install tooling or CI to detect installed mods that contain root `.dll` files and fail fast with an explicit “precompiled by design” note.

