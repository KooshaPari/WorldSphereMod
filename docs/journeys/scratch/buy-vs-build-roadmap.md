# Buy vs Build Roadmap

**Last updated:** 2026-05-23

Scored from the landed `replace-*` research set in `docs/journeys/scratch/`, plus subsystem audits. Rankings below are **hypothetical buy leverage** (LOC eliminated if swapped); paired research for all five top areas is now on disk.

`replace-frustum-lod-research.md` is a build-not-buy result, so it is excluded from the swap ranking. `replace-journeys-research.md` also says to keep `phenotype-journeys` and add optional tooling around it, not replace it.

## Top decisions — status (2026-05-23)

| Rank | Area | Research | Verdict | 2026-05-23 status |
|------|------|----------|---------|-------------------|
| 1 | Rig stack (`RigDriver` + `RigCache` + `HumanoidRig`) | [`replace-rig-driver-research.md`](replace-rig-driver-research.md) | **Build** | Custom path live; humanoid skinning on, non-humanoid → static voxel (`SkeletalRigVariantInvariantsTests`) |
| 2 | Sprite voxelizer (`SpriteVoxelizer`) | [`replace-sprite-voxelizer-research.md`](replace-sprite-voxelizer-research.md) | **Build** | Production mesher unchanged; depth-extrusion + cache invariants green |
| 3 | Decal / particle stack | [`replace-decal-particle-research.md`](replace-decal-particle-research.md) | **Build** | Runtime pools + Shuriken bursts; VFX Graph / URP decal ruled out for built-in pipeline |
| 4 | Mesh instance batching (`MeshInstanceBatcher`) | [`replace-mesh-instance-batcher-research.md`](replace-mesh-instance-batcher-research.md) | **Build** | Queue + `DrawMeshInstanced` default; BRG / indirect path deferred unless perf proves need |
| 5 | Water rendering | [`replace-water-research.md`](replace-water-research.md) | **Build** | Phase 4 mesh water default ON (`MeshWater`); Crest only if a forced package swap |

**Test guard (2026-05-23):** `dotnet test WorldSphereMod.sln` — 276 passed, 3 skipped, 0 failed (E2E 107, Unit 144, Integration 25).

## Top 5 Highest-Leverage Library Swaps

1. **Rig stack** - `RigDriver` + `RigCache` + `HumanoidRig`
   - **Estimated LOC eliminated:** ~635
   - **Risk:** High. This is the most stateful swap: GPU/CPU fallback, cache invalidation, unload teardown, and current humanoid assumptions all have to survive.
   - **Prerequisite work:** Pick a managed skeletal skinning path that can still consume WorldBox sprite-derived rigs; define a stable cache key and eviction policy; wire unload cleanup before swapping the submit path.

2. **Sprite voxelizer** - `SpriteVoxelizer`
   - **Estimated LOC eliminated:** ~452
   - **Risk:** Medium-high. The current code bakes alpha thresholding, color-preserving greedy meshing, atlas-aware pixel reads, and pivot conventions into one path.
   - **Prerequisite work:** Preserve sprite-atlas reads, voxel depth rules, vertex-color output, and cache warmup semantics so actor/building meshes do not regress visually.

3. **Decal / particle stack** - `DecalPool` + `ParticleEffectLibrary` + `EffectPatches9`
   - **Estimated LOC eliminated:** ~449
   - **Risk:** Medium. The pool logic is straightforward, but the replacement has to keep lifetime caps, world-unload cleanup, and the voxel-cube particle effect behavior.
   - **Prerequisite work:** Map the current pool caps and fire/reclaim flow to the replacement; verify unload destroys runtime meshes; keep the current effect IDs and suppression hooks intact.

4. **Mesh instance batching** - `MeshInstanceBatcher`
   - **Estimated LOC eliminated:** ~357
   - **Risk:** Medium. This is central infrastructure, so a bad swap would spread across voxel, building, foliage, and FX submission.
   - **Prerequisite work:** Support both worker-thread queueing and main-thread fast paths, camera resolution, property-block marshaling, and the current 1023-instance chunking behavior.

5. **Water rendering** - `WaterSurface` + `WaterRender` + `WaterMaskBuffer`
   - **Estimated LOC eliminated:** ~363
   - **Risk:** Medium-high. Water is isolated, but it is tightly coupled to shader availability, render order, and the world-sphere mesh surface.
   - **Prerequisite work:** Keep first-use shader loading from hitching, preserve mesh/material lifecycle, and verify the replacement still respects transparent/background ordering.

## Not Ranked

- **Sky / lighting** (`ProceduralSky` + `SunDriver` + `TimeOfDay` + `ShadowCascadeConfig`) is the first runner-up. It is a real code sink, but it is more coupled to shadows and runtime toggles than the five above, so I would not buy it before water or batching.
- **Cull + LOD** stays custom for now. The landed research says `CullingGroup` is only an adapter candidate, `LODGroup` is a poor fit, and Burst/jobification is an implementation detail rather than a replacement API.
- **`MeshSmoother`** is too small to crack the top five on leverage alone.
