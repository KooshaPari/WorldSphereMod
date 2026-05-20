# Voxel-size perf analysis

WSM3D voxels are much smaller than Minecraft blocks, so the problem is not just “more detail.” It is higher instance count, more visibility bookkeeping, and more CPU work per visible screen area.

## 1) Draw-call density

`MeshInstanceBatcher` still flushes through `Graphics.DrawMeshInstanced`, which hard-splits batches at 1023 instances. That means 28k visible instances is not 28k draw calls, but it is still a lot of CPU-side packing, bucket management, and repeated instanced submissions. The current path already groups by `(mesh, material)`, so the win from changing the API is smaller than the win from reducing how many instances reach the flush.

`BatchRendererGroup` can replace `DrawMeshInstanced`, but it is not a drop-in upgrade. In Unity 2022.3 it is a renderer rewrite path: you own instance metadata, culling input, and lifetime through the BRG callback model, and it only makes sense if the project is already committed to SRP-style rendering. For this mod, BRG is a long-term option, not the first lever to pull.

**Recommendation:** keep `DrawMeshInstanced` for now. Optimize bucket fragmentation and culling first. Prototype BRG only if profiling shows the batcher itself is the dominant cost after coarse culling.

## 2) Texture atlas vs per-actor texture

A shared atlas is the right model. Per-actor textures scale badly in both memory and state changes. A 4K atlas is comfortable on modern GPUs. An 8K atlas is still viable on desktop-class hardware, but it is no longer “free”: an RGBA32 8K atlas is about 256 MiB before mipmaps, and the real limit becomes working-set pressure, not just API support.

Unity exposes the actual hardware ceiling through `SystemInfo.maxTextureSize`, so the safe pattern is to atlas by pack/biome/material class, then clamp to the device limit at runtime.

**Recommendation:** use shared atlases, not per-actor textures. Target 4K as the normal case, allow 8K as an upper bound on capable GPUs, and fall back smaller when the device limit or memory budget says so.

## 3) LOD impostor distances

Do not scale impostor distance by another 8 just because WSM3D voxels are smaller than MC blocks. `LodSelector` already bakes an 8x world-size factor into `_entityHeight = 0.5f * 8.0f`, so multiplying the distance again would double-count the size change.

The real tuning knob is perceived screen size, not the Minecraft ratio. If the current voxel tier pops too early, adjust `LODScale`, `VoxelThreshold`, and `ProxyThreshold` from playtest/profiler data rather than applying a second 8x multiplier.

**Recommendation:** keep the current scale math, then tune thresholds empirically. Do not jump from 32 to 256 world units on ratio alone.

## 4) Frustum culling granularity

Yes, per-voxel frustum culling is wasteful at this scale. `DrawMeshInstanced` only culls the batch as a whole, so the best CPU win is to reject invisible groups before you build matrices and colors. Chunk-level culling maps well to this mod because the world already has chunk-style spatial structure.

**Recommendation:** cull at chunk level first, using 16x16 tile groups or the closest existing world chunk size. Only run per-voxel LOD/submit inside visible chunks. If a chunk is still too large, add sub-chunk bins before considering BRG.

## Bottom line

Best near-term path: chunk-level culling, shared atlases, and the current instanced draw path.

Best long-term path: if profiling still shows the renderer as the bottleneck after coarse culling, then trial `BatchRendererGroup` as a separate renderer path, not as a narrow swap for `DrawMeshInstanced`.
