# ADR-0021: SOTA Chunked + LOD + Foveated + GPU-Driven Renderer Architecture

**Status:** Proposed
**Date:** 2026-05-30
**Author:** Claude / KooshaPari
**Stakeholders:** WorldSphereMod3D, CompoundSpheres (External submodule), phenotype-voxel, MeshInstanceBatcher, LOD subsystem, Civis (path-dep on phenotype-voxel)

## Context

### Problem Statement

`Redraw3DTiles` rebuilt every tile in every 1024 zones (~42,004 tiles) every frame on Unity's main thread, producing 3.2-second frames. The band-aid fix (commit 8d7a0c87) introduced an 8ms round-robin budget throttle across zones, making the game playable but leaving the RTX 3090 Ti idle, 1 of 8 CPU cores pegged, and 63 GB RAM untouched. The architecture is wrong at the root: the render loop has no spatial coherence, no dirty tracking, no off-thread work, and issues draw calls from a per-tile flat loop rather than a GPU-driven path.

### Existing Code Anchors

| File | Role | Salvage verdict |
|---|---|---|
| `External/Compound-Spheres/CompoundSpheres/SphereManager.cs` | Owns 4 GraphicsBuffers + indirect-args; `DrawTiles(CameraX)` per-row via SphereRow | Keep as terrain back-end; replace DrawTiles dispatch |
| `External/Compound-Spheres/CompoundSpheres/SphereRow.cs:56-59` | `RenderMeshIndirect` per row, no cull | Replace with chunk-level indirect draw |
| `WorldSphereMod/Code/CompoundSphereScripts.cs` | Tile pos/rot/scale/color delegates; RenderRange | Keep delegates; add chunk coord math |
| `WorldSphereMod/Code/LOD/FrustumCuller.cs` | Per-frame cached Plane[6], IsVisible(worldPos,radius) | REUSE for chunk AABB; callers must lift positions[i].z via To3DTileHeight(false) first (gotcha) |
| `WorldSphereMod/Code/LOD/LodSelector.cs` | 3-tier hysteresis, FOV+VoxelScaleMultiplier thresholds | REUSE for actor/building LOD; extend for terrain chunk rings |
| `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs` | Thread-safe submit queue + DrawMeshInstanced 1023-batch | REUSE for entity draws |
| `WorldSphereMod/Code/Voxel/MeshInstanceBatcherBRG.cs` | BatchRendererGroup init stub; OnPerformCulling placeholder | COMPLETE this as Phase 4 GPU-driven path |
| `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs` | Greedy meshing (greedy_pertexel), atlas cache | Keep; terrain face mesher reuses greedy quad-merge |
| `WorldSphereMod/Code/Voxel/VoxelDiskCache.cs` | SQLite mesh persistence | Keep; extend schema for chunk meshes |
| `WorldSphereMod/Code/Terrain/TerrainSmoothing.cs` | MountainSlopeSurface bilinear overlay; full-map cliff scan | DELETE after Phase 3; reuse ComputeAnalyticNormals; dirty-coalesce pattern is the model |
| `WorldSphereMod/Code/Renderer/WSM3DRenderer.cs` | Forward+ scaffold; DepthPrepass stub | EXTEND Phase 4 for depth prepass |
| `WorldSphereMod/Code/Perf/FrameProfiler.cs` | Per-key rolling 1s Stopwatch | REUSE; add ChunkBuild/ChunkCull/ChunkDraw/FoveatedSelect keys |
| `WorldSphereMod/Code/SavedSettings.cs` | Feature flag registry | Add new flags per phase |

### Forces

- Unity 2022.3 + net48: `Unity.Jobs` (IJob/IJobParallelFor) + `Unity.Collections` (NativeArray) ship with Unity and are reachable from NML Roslyn compile. **Burst is NOT usable from mod source** (ships AOT DLLs, not Roslyn-friendly). Use plain IJobParallelFor (still 4-8x over single main-thread). If Burst needed, put job structs in a pre-compiled DLL in `WorldSphereMod/Assemblies/`.
- Standard map 461x720 = 331,920 tiles. At 32x32: 15x23 = **345 chunks**. At 16x16: 1,305. 32x32 wins (cheaper cull, fewer draws, 1024-tile rebuild fits one IJob).
- `Constants.ZDisplacement=100`: all frustum/LOD math must lift z via To3DTileHeight (documented gotcha, see BuildingProcRender.cs:57-64).
- `SphereManager.IsReady` gate pattern → extend to `TerrainChunkManager.IsReady`.
- Mesh creation / SetVertices is main-thread only. Jobs output NativeArray; main thread uploads.
- phenotype-voxel is Rust — cannot consume as C# DLL directly (see decision below).

## phenotype-voxel Reuse Decision

**Verdict: Do not consume directly. Port algorithmic concepts as C#; reference Rust source as spec.** Rust→C# interop in NML/Unity needs a native DLL (platform matrix, unsafe) or reimplementation. The mod already has greedy meshing (SpriteVoxelizer) and LOD policy (LodSelector) paralleling phenotype-voxel's GreedyMesher/LodPolicy. Port only what's missing: `TerrainChunk` (≈ Chunk<T>+DirtyChunkEvent), `TerrainChunkDirtyQueue` (≈ write-seq queue), `ChunkLodSelector` (extends LodSelector). Annotate C# counterparts `// SPEC: phenotype-voxel/<file>.rs`. Structured so a future C#/P-Invoke binding slots in at the chunk-mesher boundary. **Forking phenotype-voxel to add a C# binding is an allowed happy path.**

## Architecture (summary)

1. **Chunking** — 32x32 tile chunks (345 on standard map). `TerrainChunk` owns cached Mesh per LOD + IsDirty + TileDirty[1024] + world AABB + ChunkId=(cx<<16)|cy. `TerrainChunkManager : MonoBehaviour` owns `TerrainChunk[,]`, ConcurrentQueue dirty queue, IsReady gate, MarkTileDirty, DrawVisibleChunks. **Static chunks (no dirty event since last build) are never rebuilt** — kills the 42k-tiles-every-frame pathology. Dual-path gated on `SavedSettings.UseChunkedTerrain` (default false).
2. **Dirty tracking** — `TerrainChunkDirtyQueue` (ConcurrentQueue<uint> + dedup set). Harmony postfixes on WorldTilemap.redrawTiles / WorldTile.setHeight / SetTileType → MarkTileDirty. Actor moves do NOT dirty terrain (separate draw path).
3. **Frustum cull** — reuse FrustumCuller.IsVisible per chunk AABB; lift WorldBoundsCenter.z via To3DTileHeight. Est. 60-85% cull.
4. **LOD rings** — `ChunkLodSelector.SelectTerrainLod`: near<40u=Full(33x33), <120u=Coarse(17x17), else Impostor(9x9). Stride sampling. **Skirt geometry** on chunk edges to avoid T-junction cracks between LOD levels. Actor LOD (LodSelector) unchanged.
5. **Foveated** — `GetFoveationBias`: screen-space distance from center (or gaze if UseGazeForFoveation); beyond FoveationRadius (0.25) bias coarsens LOD one tier at periphery. Applied AFTER distance LOD (can only coarsen). Gaze override hook for VR/eye-tracking.
6. **Worker-thread meshing** — `ChunkMeshBuildJob : IJobParallelFor` computes NativeArray verts/indices/colors off-thread (no Unity Object access); main thread Mesh.SetVertices(NativeArray) + recycle via NativeArrayPool. ~2ms for 8 chunks on 8 cores. Frustum-aware deferral: in-frustum jobs Complete() this frame, out-of-frustum defer.
7. **GPU-driven (Phase 4)** — complete MeshInstanceBatcherBRG: register chunk LOD meshes as BatchIDs, per-chunk transforms in persistent GraphicsBuffer, OnPerformCulling does AABB-vs-planes off-main-thread + writes draw commands → 1-3 indirect draws total. Gate on CanUseBRG (supportsInstancing && !GLES). WSM3DRenderer depth prepass for early-Z.

## New Files

`Chunk/TerrainChunk.cs`, `Chunk/TerrainChunkManager.cs`, `Chunk/TerrainChunkDirtyQueue.cs`, `Chunk/TerrainChunkMeshBuilder.cs`, `Chunk/ChunkMeshBuildJob.cs`, `Chunk/ChunkLodSelector.cs`, `Chunk/NativeArrayPool.cs`, `Chunk/ChunkHarmonyPatches.cs`.

## Modified Files

`SavedSettings.cs` (flags), `General.cs` (DrawTiles dual-path), `Terrain/TerrainSmoothing.cs` (obsolete P1→delete P3), `Voxel/MeshInstanceBatcherBRG.cs` (P4), `Renderer/WSM3DRenderer.cs` (P4 depth prepass), `Perf/FrameProfiler.cs` (keys), `LOD/FrustumCuller.cs` (IsVisibleAABB overload), `Mod.cs` (mount manager, wire Become3D/2D).

## Phased Build Plan (disjoint file ownership for parallel agents)

**P1 Chunked Dirty Tracking** (no visual change; target strat-zoom ≤30ms, quiescent=0 rebuilds). Files: TerrainChunk, TerrainChunkManager (synchronous RebuildChunk), TerrainChunkDirtyQueue, TerrainChunkMeshBuilder, ChunkHarmonyPatches, SavedSettings(flags), General.cs, Mod.cs. Accept: 0 builds on quiescent world, ChunkDraw<1ms, 345 chunks no holes, flag-off reverts to legacy.

**P2 Frustum Cull + LOD Rings** (target ≤16ms strat / ≤30ms close). Files: ChunkLodSelector, FrustumCuller(overload), TerrainChunkManager(cull), TerrainChunkMeshBuilder(LOD1/2+skirts), SavedSettings(dist), TerrainSmoothing(disable when chunked). Accept: ≤120 visible chunks, cull≥60%, no LOD cracks, MountainSlope off when chunked.

**P3 Foveated + Worker-Thread Meshing** (target ≤10ms strat; 7/8 cores). Files: ChunkMeshBuildJob, NativeArrayPool, ChunkLodSelector(foveation), TerrainChunkManager(job dispatch), TerrainChunkMeshBuilder(refactor to job), SavedSettings(foveation), DELETE TerrainSmoothing.cs. Accept: 8 cores active on brush stroke, JobComplete<2ms, peripheral coarsening, 0 GC steady-state.

**P4 GPU-Driven BRG** (target ≤5ms terrain draw, GPU≥60%). Files: MeshInstanceBatcherBRG(full), TerrainChunkManager(BRG reg), WSM3DRenderer(depth prepass), MeshInstanceBatcher(UseBRG route). Accept: GPU≥60%, terrain draws≤3, FrameDrawCalls≤25, GLES/no-instancing falls back gracefully.

## Performance NFR Targets

| View | Current | P1 | P2 | P3 | P4 |
|---|---|---|---|---|---|
| Strategy quiescent | ~16ms* | ≤30 | ≤16 | ≤10 | ≤5 |
| Strategy brush | ~200ms | ≤40 | ≤20 | ≤12 | ≤8 |
| Ground perspective | ~50ms | ≤40 | ≤20 | ≤16 | ≤10 |
| CPU cores | 1/8 | 1/8 | 1/8 | 7/8 | 7/8 |
| GPU util | ~5% | ~10% | ~20% | ~40% | ~70% |
| Terrain draws | 461/row | 345/chunk | ≤120 | ≤120 | ≤3 |

(*pre-fix un-throttled was 3,200ms.) HW: Ryzen 8-core / RTX 3090 Ti / 64GB / DX12 / Unity 2022.3 / 461x720 flat map.

## Critical Implementation Details

- NativeArray dispose in try/finally → NativeArrayPool.Return in finally. Pool uses Allocator.Persistent (outlive frame); TempJob for within-frame temporaries.
- Mesh.SetVertices throws if length mismatches existing — Mesh.Clear() before resizing (see MountainSlopeSurface.RebuildMesh:352).
- BRG OnPerformCulling runs off-main-thread — only NativeArray read + BatchCullingOutput write allowed.
- Reset chunk dirty + dispose NativeArrays in OnWorldUnload (WorldUnloadPatch pattern).
- ChunkLodSelector keys on chunk index (bounded) not instanceId — avoids LodSelector._hyst unbounded-growth.
- No `unsafe` needed; ScheduleParallel public API is managed.

## References

CompoundSpheres SphereManager/SphereRow; FrustumCuller; LodSelector; MeshInstanceBatcher(BRG); SpriteVoxelizer greedy; VoxelDiskCache SQLite; TerrainSmoothing.ComputeAnalyticNormals; BuildingProcRender.cs:57-64 (To3DTileHeight lift); WSM3DRenderer; FrameProfiler; SavedSettings UseHeightFieldTerrain dual-path; ADR-0015 (perf/cull history); ADR-0017 (terrain/LOD table); phenotype-voxel src/{lod,greedy_mesher,chunk}.rs; Unity 2022.3 IJobParallelFor / Mesh.SetVertices(NativeArray) / BatchRendererGroup.OnPerformCulling.
