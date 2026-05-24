# TODO / FIXME / HACK Audit

Generated: 2026-05-21 18:50:13  
Closed: 2026-05-23 (source scan: 0 open matches under `WorldSphereMod/Code/`)

| File | Line | Original snippet | Status | Resolution |
| --- | --- | --- | --- | --- |
| WorldSphereMod/Code/Water/WaterRender.cs | 100 | TODO Phase 4 polish: dirty-track per-tile | **Closed** | Intentional deferral: full mask + mesh rebuild on `UpdateBaseLayer` only (tile-edit events, bounded mesh size). Per-tile dirty tracking reserved for Phase 4 polish if profiling shows need. |
| WorldSphereMod/Code/Voxel/VoxelRender.cs | 43 | TODO: wire from world-reload Postfix | **Closed** | `VoxelRender.Reset()` called from `WorldUnloadPatch.OnFinish` (`Core.Sphere.Finish` Prefix). Doc comment documents the hook. |
| WorldSphereMod/Code/Voxel/VoxelRender.cs | 237 | Phase 5 TODO (AssetBundle shader) | **Closed** | `TryCompileInlineVoxelShader` resolves via `Core.Sphere.LoadedShaders` then `Shader.Find("WSM3D/OpaqueVertexColor")`; falls back to built-in shader chain with bake instructions in comment. |
| WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs | 15 | TODO: Postfix ordering non-deterministic | **Closed** | `[HarmonyPriority(Priority.Last)]` on Prefix; comment documents deterministic ordering. `VoxelMeshCache.DrainPendingDestroy()` runs before `Clear()`. |

Total open matches in mod source (`WorldSphereMod/Code/`): **0**.
