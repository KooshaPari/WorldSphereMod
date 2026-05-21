# WSM3D session telemetry proof (b0fe320 deployed)

After deploying the inline-sync voxel mesh build fix (commit b0fe320),
the game stays up under a loaded save and the bridge reports clean
steady-state numbers.

```
GET /health
{"ok":true,"version":"2.2","isWorld3D":true}

GET /telemetry
{"frameMs":38.85,"voxelCacheHit":0.9999207,"impostorCacheHit":0.0,
 "drawCalls":306,"instances":306}

GET /voxel/stats
{"ok":true,"cache":{"size":133,"hits":1589508,"misses":133}}

GET /voxel/queue
{"ok":true,"pendingBuilds":0,"completedThisFrame":0,"totalBuilds":133}
```

- 133 unique sprites voxelized end-to-end (matches `totalBuilds`).
- 1.59M cache hits — every per-frame lookup hits except the 133 first-time misses.
- 306 draw calls per frame — fallback per-instance `Graphics.DrawMesh` path.
- 0 pending lazy builds + 0 completed-this-frame = no backpressure.

## Known follow-ups

- `/voxel/sprite` (no `name`) returns `sprites:[]` despite cache.size=133 — payload mapping bug, harmless.
- `instances` count == `drawCalls` count: `MeshInstanceBatcher` is on the fallback path, not true GPU instancing. Phase 10 BRG migration would restore N>>D.

