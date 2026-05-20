# Perf roadmap (synthesized from 6 codex gameperf workers)

**Date:** 2026-05-19
**Source:** `.codex-gameperf-{01..06}.out` (gameperf-04 substituted by manual
inventory after worker disconnect at 75k tokens — see
[harmony-patch-inventory.md](./harmony-patch-inventory.md)).

## TL;DR

5 of 6 perf workers returned. Most actionable finding (an alloc/frame
bug in BuildingRulesRegistry.Resolve) is already fixed in commit
`aa37382`. Three concrete diagnostics fixes also landed (`561c177`
LogAllCameras gate, `7527350` hit/miss counters). The remaining
roadmap is mostly **architectural** — bigger refactors that need an
in-game profiler trace before committing to them.

## Shipped already from worker findings

| Commit  | Worker | Fix |
|---------|--------|-----|
| `561c177` | 06 | one-shot gate on `MeshInstanceBatcher.LogAllCameras` (was logging every Flush) |
| `aa37382` | 02 | memoize `BuildingRulesRegistry.Resolve` auto-routes via `ConcurrentDictionary.GetOrAdd` (~1k allocs/frame at 1k visible foliage) |
| `7527350` | 05 | `HitCount`/`MissCount` counters on `VoxelMeshCache` + `ImpostorBillboard` for empirical cache-cap tuning |

## Tier 1 — concrete next wins (each 1-3 commits, evidence-backed)

### 1. Hoist `ignore_generic_render` gate (gameperf-01)
**Where:** `WorldSphereMod/Code/QuantumSprites.cs:468-500`
**What:** `tActor.asset.ignore_generic_render` is checked AFTER
`updatePos()` + `Get3DRot()`. Move the gate to *before* those calls so
non-rendered actors don't pay position+rotation cost.
**Expected impact:** depends on the share of always-skip actors in a
typical world — likely 5-15% of the actor render-data Postfix when
many "invisible" actors exist (dead sprites, kingdom owners off-screen,
etc).
**Risk:** the upstream code is opaque; need to verify `updatePos()`
doesn't have side effects required by other patches.

### 2. Frame-stable actor render-state cache (gameperf-01)
**Where:** `QuantumSprites.cs:473-500, 556-599`
**What:** `calculateMainSprite()`, `getAnimationFrameData()`, and
`getRenderedItemSprite()` run per actor per frame even when the actor
sprite/item state hasn't changed. Cache the result keyed by `(actor,
last_modified_frame)`.
**Expected impact:** biggest single "redundant work" bucket per the
worker. Material for a 5-10% frame-time win at 2k actors if sprite
state is stable across most frames.
**Risk:** invalidation. Anything that changes an actor's sprite needs
to bump the dirty stamp. Worth a profiler trace before sinking time.

### 3. Wire hit/miss counters into RuntimeStatsOverlay (gameperf-05 follow-up)
**Where:** `WorldSphereMod/Code/Worldspace/RuntimeStatsOverlay.cs:97-110`
**What:** counters from commit `7527350` now exist on `VoxelMeshCache`
and `ImpostorBillboard` but aren't displayed. Add two lines showing
`VoxelCache: H={hits} M={misses} ratio={pct}` to the overlay so we can
empirically validate the 4096 LRU cap.
**Expected impact:** observability — no perf win, but unblocks tuning
of the cap itself.

### 4. Eviction policy for ImpostorBillboard cache (gameperf-05)
**Where:** `WorldSphereMod/Code/LOD/ImpostorBillboard.cs`
**What:** `_atlas` has no eviction; every unique sprite lives until
world unload. At 6406 sprites worst case → ~70 MiB GPU memory just for
impostor quads if all are ever seen. Mirror VoxelMeshCache's LRU+cap.
**Expected impact:** memory ceiling. No perf win until you actually hit
the working-set boundary; defensive.
**Risk:** low — same pattern as VoxelMeshCache already proves out.

### 5. Phase 3+ cull-lift fix (audit doc from earlier this session)
**Where:** `BuildingProcRender.cs:95`, `VoxelRender.cs:379`,
`VoxelRender.cs:505` (see [phase3-cull-lift-audit.md](./phase3-cull-lift-audit.md))
**What:** 3 TRS sites read raw `rd.positions[i]` (z=0) after the cull
pass that already lifted `cullPos`. Add the same lift guard to the TRS
path.
**Expected impact:** *correctness*, not perf. Currently masked by
`VoxelScaleMultiplier=8.0`; surfaces when scale is restored for alpha.8.

## Tier 2 — architectural (need profiler evidence first)

- **Tier-partitioned building Postfix** (gameperf-02): split impostor /
  mesh batches up front to remove tier branch from inner loop. Only
  wins when one tier dominates AND scene is large enough that branch
  mispredict matters. Defer until in-game trace confirms.

- **Fast-path direct bucket-add in `MeshInstanceBatcher.Submit`** when
  caller is on main thread (gameperf-06): skip queue, write directly.
  Worker-thread callers still queue. ~30-50% reduction in queue overhead
  if main-thread share is high.

- **Tools.GetTileHeightSmooth caching** (gameperf-03): 2× `World.world.GetTile()` +
  `Vector2.Distance` sqrt per call, ~10-12k calls/frame at stress.
  Could memoize at the call site using `rd.positions[i]` keys, but the
  helpers themselves are clean.

- **Pre-warm `VoxelMeshCache`** selectively on world load (gameperf-05):
  build first-visible/high-frequency sprites before gameplay unpauses.
  Don't try to warm all 6406 (~188 MiB).

## Tier 3 — rejected suggestions

- Cylindrical-wrap branch hoist in `Tools.Dist` (gameperf-03): rejected —
  branch is a single predictable compare, dwarfed by sqrt/atan2.
- ZDisplacement sentinel compare elision (gameperf-03): trivial cost,
  leave alone.
- LOD-before-cull reorder (gameperf-01): already in the right order;
  `LodSelector.Select` already caches threshold math + hysteresis.

## Audit observations (no fix yet)

- **15 always-on hot Harmony patches** documented in
  [harmony-patch-inventory.md](./harmony-patch-inventory.md). Need to
  verify each QuantumSpriteLibrary draw patch early-outs when
  `Core.IsWorld3D == false`; if not, they pay full conversion cost
  while the player is in 2D mode.
- **Synchronous first-use stalls**: `SpriteVoxelizer.Build` miss,
  `Resources.Load` in ProceduralSky / WaterSurface / RigDriver /
  FoliageMaterial. Either gated behind loading screens or worth
  background prep before the first frame that needs them.

## Token budget consumed

5 of 6 workers reported back cleanly. Combined token consumption
~140k across `gpt-5.4-mini` (fell back from `gpt-5.3-codex-spark`
after quota hit at 11:06 PM). gameperf-04 spent 75k before disconnect.
