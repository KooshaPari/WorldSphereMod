# WorldSphereMod.csproj Audit

1. **Target framework**
   - Pass. The project targets `net48` in [WorldSphereMod.csproj](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod.csproj#L4).
   - This matches the NML expectation called out in [CLAUDE.md](/C:/Users/koosh/Dev/WorldSphereMod/CLAUDE.md#L84).

2. **Assembly references**
   - Pass for the required runtime refs:
   - `UnityEngine` is covered by the Unity module refs, including `UnityEngine.CoreModule` and `UnityEngine.UI` in [WorldSphereMod.csproj](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod.csproj#L90).
   - `NeoModLoader` is explicitly referenced in [WorldSphereMod.csproj](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod.csproj#L69).
   - `CompoundSpheres` is explicitly referenced in [WorldSphereMod.csproj](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod.csproj#L39).

3. **Preview / experimental features**
   - No preview/experimental build switches are enabled in the project file.
   - The only language/runtime knobs present are `LangVersion=9.0`, `Nullable=annotations`, and `AllowUnsafeBlocks=true` in [WorldSphereMod.csproj](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod.csproj#L4).
   - I did not find `LangVersion=preview`, `EnablePreviewFeatures`, or similar flags that would make upgrades surprising.

4. **Packaging artifacts**
   - Not handled by the `.csproj`.
   - There are no `Content` items or `CopyToOutputDirectory` rules for `mod.json`, `AssetBundles`, `Locales`, or `GameResources` in [WorldSphereMod.csproj](/C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod.csproj#L12).
   - The repo’s install flow copies those folders/files from `WorldSphereMod/` into the WorldBox mod folder in [Tools/install.ps1](/C:/Users/koosh/Dev/WorldSphereMod/Tools/install.ps1#L69).
   - So: packaging is present at install time, but not as a build output concern in this project file.
