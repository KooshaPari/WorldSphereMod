# Error Handling + Logging Audit

Scope: `WorldSphereMod/Code/`

## 1) Silent failures
- `WorldSphereMod/Code/Tools.cs:186-189` swallows exceptions in `ViewPortToRay()` and returns `false` with no log. This is the clearest silent failure in `Code/`.
- `WorldSphereMod/Code/AutoTest.cs:191-201` catches exceptions in `GetFirstActorPos()` and returns `"<error:...>"` without logging. This is intentional-ish test plumbing, but still a swallowed failure.

## 2) Noisy logs
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:237-248` emits `[WSM3D][DIAG]` logs every `Flush()` when any actor submissions were recorded. There is no settings gate; in active scenes this can print every frame.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:215-234` is properly gated by `Core.savedSettings.ProfilerDump`, so the `[PERF]` lines are acceptable.
- `WorldSphereMod/Code/Perf/FrameProfiler.cs:50-58` is also gated by `ProfilerDump` and logs once per 1 s window, so it is disciplined.

## 3) Tag consistency
- The main scheme is mostly consistent: `[WSM3D]` for normal logs, `[WSM3D][PERF]` for performance, `[WSM3D][DIAG]` for diagnostics, and `[WSM3D][MATERIAL]` for shader/material tracing.
- Two outliers break the pattern:
  - `WorldSphereMod/Code/Perf/FrameProfiler.cs:56-58` uses `[WSM-PROF]` instead of `[WSM3D][PERF]`.
  - `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs:129` uses `[SpriteVoxelizer]` instead of a `[WSM3D]...` tag.
- Net: tagging is mostly consistent within the WSM3D subsystem, but not uniform across all logging code.

## 4) Error vs warning severity
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:182-191` uses `Debug.LogError` for a recoverable fallback: `DrawMeshInstanced` fails, then the code switches to per-instance `Graphics.DrawMesh`. This should likely be `Debug.LogWarning`.
- `WorldSphereMod/Code/WorldSphereAPI.cs:61-65` logs a failed setting lookup with `Debug.Log` rather than a warning. That under-reports a real failure path.
- I did not find a clear case where a `Debug.LogWarning` should be escalated to `Debug.LogError`; the warning paths I found are mostly recoverable.
