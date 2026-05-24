# ADR 0007 — NML precompiled detection follow-up for WorldSphere install layout

**Status:** Proposed

**Date:** 2026-05-18

## Question

Investigate installed WorldBox/NML mods that include both `Code/` and `Assemblies/*.dll` and identify which marker makes NML treat them as precompiled.

## Findings

1. In `C:/Program Files (x86)/Steam/steamapps/common/worldbox/Mods` there are only two mods that match the requested shape (`Code/` exists and `Assemblies` has at least one `.dll`):  
   - `THE_3D_WORLDBOX_MOD`
   - `WorldSphereMod3D`

2. For both mods, the root manifest is essentially the same schema:
   - `name`, `author`, `version`, `description`, `GUID`, `iconPath`.
   - no `precompiled` key and no alternate source marker.

3. No assembly-level marker exists in source:
   - no `AssemblyInfo.cs` files.
   - no `[assembly: ...]` lines in any `.cs` file under either `Code/` tree.

4. Namespace layout does not show a differentiating precompiled signal:
   - both mods define the same `WorldSphereMod.*` namespaces.
   - both expose the same runtime dependency shape (`Assemblies/CompoundSpheres.dll` + `.pdb`) and contain the same top-level `Code/Mod.cs` entrypoint shape.

5. NML does create compiled-cache artifacts under:
   - `C:/Program Files (x86)/Steam/steamapps/common/worldbox/worldbox_Data/StreamingAssets/Mods/NML/CompiledMods/THE_3D_WORLDBOX_MOD.dll`
   - `.../CompiledMods/WORLDSPHERE3D_FORK.dll`
   (names normalized from `GUID`).

## Conclusion

No manifest key, namespace, or assembly attribute was found that clearly flags these as “precompiled” ahead of runtime load. The observable signal is the presence of NML’s own compiled cache entry, not a source-level marker in the mod folder.

## Recommendation

- Keep `install.ps1` unchanged for now (per instruction), but when we change install behavior, gate precompiled intent explicitly:
  1. add an explicit manifest marker (example: `"precompiled": true`) in `mod.json` and
  2. change install/layout handling for that mode to avoid ambiguous mixed source+assembly setups (e.g., either source-disabled mode with `Source/`/`Code` policy or clear runtime-only precompiled mode).
- If this signal is accepted as insufficient, add a loader-facing manifest convention documented in `docs/` and test with a single mod first before changing installation policy broadly.
