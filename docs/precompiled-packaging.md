# Precompiled Packaging

`WorldSphereMod3D` can be installed in two layouts:

- source layout: `WorldSphereMod/Code/*.cs` is copied into the WorldBox mod folder and NML Roslyn-compiles it at startup.
- precompiled layout: `WorldSphereMod3D.dll` is copied into the mod root and `Code/` is omitted so NML treats the mod as precompiled.

## Detection rule

The current NML build in this repo's audit follows a filesystem trigger:

- if any `.dll` exists in the mod folder root, NML logs the mod as precompiled and skips the runtime compile phase.
- `mod.json` does not provide a precompiled toggle in the observed install.
- `CompiledMods/` and `mod_compile_records.json` are NML's compiled cache, not the trigger for this branch.

Observed supporting evidence:

- `docs/adr/ADR-0007-nml-precompiled-detection.md`
- `docs/framework-config-audit.md`

## Install usage

Source install remains the default:

```powershell
./Tools/install.ps1
```

Precompiled install is opt-in:

```powershell
./Tools/install.ps1 -Precompiled
```

That mode:

- builds `bin/Release/net48/WorldSphereMod3D.dll`
- copies `WorldSphereMod3D.dll` to the mod root
- omits `Code/` so NML does not compile the source tree again
- keeps `Assemblies/CompoundSpheres.dll` and the other non-source assets in place

## Validation criteria

Treat the precompiled packaging as successful only if the next WorldBox launch shows all of the following:

- `"[NML]: ... detected as precompiled, compilation phase will be skipped on it!"`
- `Init Mod WorldSphereMod3D`
- `Post-Init Mod WorldSphereMod3D`
- no compile-phase error for `WorldSphereMod3D`
- no duplicate-type or double-load errors

If NML still emits `Compile Mod WorldSphereMod3D`, the install path is still source-based and the precompiled layout is not taking effect.

## Notes

- The precompiled mode is intentionally opt-in because the source layout remains useful for day-to-day development.
- The DLL must live in the mod root. Shipping only `Assemblies/WorldSphereMod3D.dll` is not the same thing and does not match the observed trigger.
