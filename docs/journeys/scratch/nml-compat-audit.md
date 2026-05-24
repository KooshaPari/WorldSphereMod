# NML Compatibility Audit

Scope: `WorldSphereMod3D` NeoModLoader surface, with the upstream co-install claim checked against the fork docs and the current installed NML metadata.

## Verdict

- `mod.json` is valid for the current NML loader shape I found. The manifest only uses the standard keys already documented for this forked install layout: `name`, `author`, `version`, `description`, `GUID`, `iconPath` ([WorldSphereMod/mod.json](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/mod.json#L2), [docs/adr/ADR-0007-nml-precompiled-detection-followup.md](C:/Users/koosh/Dev/WorldSphereMod/docs/adr/ADR-0007-nml-precompiled-detection-followup.md#L17)). The installed `NeoModLoader.dll` metadata is `1.2.0.1`, matching the repo’s current NML notes ([docs/adr/ADR-0007-nml-precompiled-detection.md](C:/Users/koosh/Dev/WorldSphereMod/docs/adr/ADR-0007-nml-precompiled-detection.md#L32)).
- I did not find a deprecated-NML-API smell in the source I checked. The mod uses the standard loader contracts (`IMod`, `IStagedLoad`, `ILocalizable`, `ModDeclare`) plus the usual helpers (`Paths.ModsConfigPath`, `ResourcesFinder`, `TabManager`) ([WorldSphereMod/Code/Mod.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Mod.cs#L2), [WorldSphereMod/Code/WorldSphereTab.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/WorldSphereTab.cs#L1), [WorldSphereMod/Code/Core.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L5)).
- Load-order assumptions are deliberate: `OnLoad` only captures `ModDeclare`/`GameObject` and applies the hardware gate; `Init` does settings load, UI setup, patching, and deferred driver creation; `PostInit` is where `Sphere.Prepare()` runs after all mods are loaded ([WorldSphereMod/Code/Mod.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Mod.cs#L17), [WorldSphereMod/Code/Core.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L65), [WorldSphereMod/Code/Core.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L109)). The explicit comment says assets are loaded after mods so other mods can add tiles first.
- Co-installable on disk, but not safe to enable together. The fork’s GUID is `worldsphere3d.fork` and the docs explicitly say it can sit beside upstream `WorldSphereMod`, but only one should patch at a time ([WorldSphereMod/mod.json](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/mod.json#L6), [docs/journeys/upgrade-from-upstream.md](C:/Users/koosh/Dev/WorldSphereMod/docs/journeys/upgrade-from-upstream.md#L18), [docs/HANDOFF.md](C:/Users/koosh/Dev/WorldSphereMod/docs/HANDOFF.md#L165), [docs/CONTRIBUTING.md](C:/Users/koosh/Dev/WorldSphereMod/docs/CONTRIBUTING.md#L32)).

## Collision Review

- There is no literal shared C# `static` storage between two separately loaded assemblies; the fork and upstream will each get their own static fields.
- The real collision risk is global identity, not memory sharing:
  - Harmony ID is hard-coded as `"WorldSphereMod"` ([WorldSphereMod/Code/Core.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L114)).
  - The settings file name is hard-coded as `WorldSphereMod.json` ([WorldSphereMod/Code/Core.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L30)).
  - The UI tab and many resource paths still use the upstream `"WorldSphereMod"` prefix (`TabManager.CreateTab("WorldSphereMod", ...)`, `Resources.Load("WorldSphereMod/...")`) ([WorldSphereMod/Code/WorldSphereTab.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/WorldSphereTab.cs#L56), [WorldSphereMod/Code/WorldSphereTab.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/WorldSphereTab.cs#L58)).
  - Global registries are mutated with the same keys (`Perspective`, `Is3D`, phase toggle ids), so enabling both mods would double-register UI and patch/global state even though the GUIDs differ ([WorldSphereMod/Code/Core.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L86), [WorldSphereMod/Code/WorldSphereTab.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/WorldSphereTab.cs#L179)).

## Bottom Line

- Current `mod.json`: okay.
- Deprecated NML APIs: none obvious in the audited surface.
- Load-order assumption: `PostInit` must happen after all mods and world data are ready; that assumption is explicit and consistent.
- Upstream coexistence: install side-by-side is fine, enable one at a time only.
