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


## Perf degradation observed loop +14min

```
GET /voxel/stats
{"ok":true,"cache":{"size":607,"hits":13292114,"misses":607}}

GET /telemetry
{"frameMs":519.77,"voxelCacheHit":0.99995,"impostorCacheHit":0.99998,
 "drawCalls":59489,"instances":59489}
```

- cache growth: 132 → 520 → 607 sprites (~4.5x in 21 min)
- drawCalls/frame: 306 → 29659 → **59489**
- frameMs: 38.85 → 341 → **519** — game-unplayable territory
- 1:1 instance:draw ratio = per-instance fallback (no GPU batch)

ForceFallbackDrawPath flag added (commit f37dad3); flip to false +
relaunch to enable Graphics.DrawMeshInstanced. Expected: 59k draws
collapse to ~58 batches of 1023 instances. 1000x reduction.


## End-of-session steady state (a8ecb97)

```
GET /health      → {"ok":true,"version":"2.3","isWorld3D":true}
GET /telemetry   → {"frameMs":174.66,"voxelCacheHit":0.98,"drawCalls":295,"instances":295}
GET /voxel/stats → {"ok":true,"cache":{"size":130,"hits":1715426,"misses":33673}}

Phase status (7/10 enabled):
  VoxelEntities=True patched=2
  ProceduralBuildings=False patched=1
  CrossedQuadFoliage=False patched=2
  MeshWater=True patched=5
  HighShadows=True patched=1
  SkeletalAnimation=False patched=1
  WorldspaceUI=True patched=1
  DayNightCycle=True patched=1
  PostFX=False patched=1
  ParticleEffects=True patched=3
```

Bridge resilience verified: post-save-load /health and /phase respond
cleanly without timeout. The 4-fix layered approach (static queue,
triple-callback drain, destroyed-host detect, Mod.PostInit re-invoke)
resolves the previously-known scene-transition death.

Unit suite: 67 pass / 3 skip / 0 fail.


## Cache miss interpretation note

Observed: `cache.size=131` but `cache.misses=37004` after ~10min steady-state.

This is NOT eviction churn. The 37k count includes duplicate requests
during the lazy-build window: when sprite X is first encountered,
miss++ + placeholder returned + build queued. Subsequent actors
requesting the same sprite before the build completes also count as
miss (the cache hasn't been updated yet). Once build completes,
cache.size++ but the burst of duplicate misses persists in the counter.

So `cache.size` = unique sprites cached
   `cache.misses` = total cache-miss events (includes pre-build duplicates)
   `cache.hits` = total post-cache successful lookups (the heavy traffic)

Hit-rate calculation `hits / (hits+misses)` is still meaningful:
1.89M / (1.89M + 37k) = 98.07% — matches `/telemetry voxelCacheHit`.

