# Performance profiling guide

Scope: WorldSphereMod3D fork, branch `claude/research-ultraplan-fork-DdgI5`.

## 1. Hot paths per phase

| Phase | CPU hot path | GPU hot path |
|---|---|---|
| 1 (voxel actors) | Harmony Postfix on `ActorManager.precalculateRenderDataParallel`, looping all visible actors at 60Hz, building `Matrix4x4.TRS` per actor and calling `MeshInstanceBatcher.Submit`. `VoxelMeshCache.Get` first-time cost = `SpriteVoxelizer.Build` (alpha read + greedy mesh). | `Graphics.DrawMeshInstanced` count = `ceil(actors / 1023)` per unique `(mesh, material)` bucket. Per-frame instance vector array upload (`SetVectorArray(_InstanceColor)`). |
| 2 (procgen buildings) | Same Postfix pattern for buildings. First-touch cost is `BuildingMeshGen.Generate` (silhouette extrude + roof inference + door/window detection). `Mesh.CombineMeshes` inside `BuildingMeshGen.Combine` is the dominant one-shot cost when more than ~4 sub-meshes are merged into the final asset mesh. | Same bucketed `DrawMeshInstanced` path. Larger meshes — more vertex shader load per instance. |
| 3 (foliage) | Crossed-quad mesh cache populate; per-frame top-tile sweep. | Wind-shader vertex displacement on every leaf instance. |
| 4 (water) | `WaterSurface.RebuildMesh` rebuilds the whole water mesh on *any* tile change (no dirty-rect tracking). Per-frame Gerstner uniforms upload to shader globals. | Fullscreen-ish water shader with reflection sample + Fresnel. |
| 7 (skeletal anim) | Per-actor bone transform compute, matrix palette upload to skinning compute shader. | Skinning compute pass. |

## 2. What to log when `ProfilerDump = true`

Currently the flag exists at `SavedSettings.cs:50` but has no consumer. When wired, dump once per second from a dedicated `MonoBehaviour` driver (Update gated by `Time.unscaledTime` accumulator):

```
[WSM-PROF] sys=<name> ms=<frame_avg> dc=<draw_calls> cache=<count> hit=<pct>
```

Required systems (`<name>`): `voxel`, `procgen`, `foliage`, `water`, `rig`, `worldui`, `total`. Pull `MeshInstanceBatcher.FrameDrawCalls` / `.FrameInstances`, `VoxelMeshCache.Count`, `ProcGenCache.Count`. Hit rate is tracked by incrementing two counters inside `Get` / `GetOrGenerate`.

## 3. Unity Profiler workflow

1. Launch WorldBox with `-force-vulkan` (Linux) or default (Windows) and `--profile-mode` console flag (Phase 10 will wire it).
2. In Unity Editor 2022.3 LTS open *Window → Analysis → Profiler*, click the connection dropdown, select the running `worldbox.exe` PID. Requires player build with `Development Build` checked — WorldBox release builds expose the autoconnect profiler only on the local machine.
3. Capture 600 frames around a high-load scene (5k actors mid-combat).
4. NeoModLoader (NML) does not currently expose profiler hooks. Add custom `ProfilerMarker`s (e.g. `static readonly ProfilerMarker s_actorVoxel = new("WSM.ActorVoxel");`) in hot Postfix bodies.
5. Burst is **not** used yet — when adding Burst-compiled greedy meshing (see §5), Burst markers appear automatically under *Jobs* in the timeline.

## 4. Known perf risks

- **Greedy meshing** in `SpriteVoxelizer.GreedyMesh` is `O(W·H·D·6)` per sprite. Fine for 16×16×8 = 12 288 ops, but a 96×96 boss sprite at depth 16 = 884 736 ops × 6 = ~5 M ops; expect a multi-millisecond stall on first sight of a boss. Mitigate by prebuilding boss meshes on world load.
- **`Tools.PixelsFromSpriteAtlas`** reads the *full atlas* into a `Color32[]`, then slices. Cost scales with full atlas size (often 2 048²), not sprite size. Repeated calls during a voxelization burst will allocate megabytes per call. Cache the atlas pixel arrays per-atlas, not per-sprite.
- **`Mesh.CombineMeshes`** in `BuildingMeshGen.Combine` allocates intermediate vertex/index buffers; with 100+ unique building assets generating in a single frame this drives GC.
- **`WaterSurface.RebuildMesh`** is full-rebuild on any water mask change. A single shoreline edit = full O(tiles) walk.
- **MaterialPropertyBlock array reallocation** in `MeshInstanceBatcher.Flush` allocates `new Matrix4x4[n]` and `new Vector4[n]` every flush — these should be pooled.

## 5. Optimization opportunities (not yet implemented)

- Burst-compile the greedy meshing pass; expected 5–10× speedup, allows boss meshes on demand.
- SIMD `float4x4` math (Unity.Mathematics) for the per-actor `Matrix4x4.TRS` loop — avoids the `Quaternion.Euler` trig per actor.
- Variance reduction: collapse near-duplicate materials (same shader + same `_BaseMap`) into one shared `Material` so more actors land in the same batcher bucket. Bigger buckets = fewer 1023-instance flushes.
- Dirty-tracked water rebuild: track changed tile IDs in a `HashSet<int>`, only re-emit affected quads.
- Pool the `Matrix4x4[]` / `Vector4[]` temp arrays in `MeshInstanceBatcher.Flush`.

## 6. Profile baseline numbers (placeholders)

Fill after first smoke test on reference hardware (RTX 3060 / 5600X for primary, Intel UHD 620 for fallback).

| Target | Source | Reference | Fallback | Captured |
|---|---|---|---|---|
| 60 fps, 500 actors | PLAN.md:74 | ≤ 16.6 ms total, voxel ≤ 4 ms | n/a | TBD |
| 1000 buildings ≤ 5 ms | PLAN.md:92 | procgen ≤ 5 ms | n/a | TBD |
| 5k trees ≤ 3 ms | PLAN.md:105 | foliage ≤ 3 ms | n/a | TBD |
| 60 fps, 5000 actors | PLAN.md:189 | ≤ 16.6 ms total | n/a | TBD |
| 60 fps fallback | PLAN.md:196 | n/a | Intel UHD 620, impostor path ≤ 16.6 ms | TBD |

Expected draw-call ceiling at 5k actors with a single mesh+material bucket = `ceil(5000/1023) = 5` calls — anything materially higher means bucket fragmentation.

## 7. `ProfilerDump` log line format

```
[WSM-PROF] t=<sec.ms> sys=<id> ms=<float.2> dc=<int> inst=<int> cache=<int> hit=<float.2>
```

- `t` — `Time.realtimeSinceStartup`, two decimals.
- `sys` — one of `voxel|procgen|foliage|water|rig|worldui|total`.
- `ms` — rolling 1 s mean frame budget for that system.
- `dc` — draw calls submitted by the system this interval.
- `inst` — instance count submitted.
- `cache` — `*Cache.Count` for the system (0 where N/A).
- `hit` — cache hit ratio over the interval, percent.

One line per system, emitted contiguously, terminated with the `total` line. Parser-friendly (whitespace-delimited `k=v`).
