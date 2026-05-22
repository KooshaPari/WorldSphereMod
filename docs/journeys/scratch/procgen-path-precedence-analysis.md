# Procgen path precedence check (Phase 2)

Scope: `WorldSphereMod/Code/ProcGen/`.

Findings:

1. `SavedSettings` keeps `ProceduralBuildings = true` and `BuildingStyleProcgen = false` by default.
2. The `BuildingProcRender` hook is only patched when the phase toggle is `ProceduralBuildings` (`[Phase(nameof(SavedSettings.ProceduralBuildings))]`), and `EmitMeshes` exits immediately unless `Core.savedSettings.ProceduralBuildings` is true.
3. The only use of `BuildingStyleProcgen` is inside the `BuildingShape == Procgen` branch to choose mesh source:
   - `BuildingStyleProcgen == true` → `ProcGenCache.GetOrGenerate(...)` (legacy/procgen-style architecture branch).
   - `BuildingStyleProcgen == false` → `VoxelMeshCache.Get(...)` (new voxel proc-mesh branch).
4. Therefore, with `ProceduralBuildings = true` and `BuildingStyleProcgen = false`, the new voxel proc-mesh path is selected (for `Procgen`-shaped assets).

Conclusion: The precedence behavior is correct in-code: `ProceduralBuildings` gates the phase; `BuildingStyleProcgen` only selects between architecture variants after that gate, and it already defaults to false.

