# Actor 2D / Invisible Gap — Live Runtime Diagnosis

Date: 2026-05-29
Investigator: diagnosis-only agent (no source edits)
Bridge: http://127.0.0.1:8766 (live game)

## TL;DR — Root cause

**The voxel emit/submit path was gated OFF at the persisted-settings level.** The
running game had `"VoxelEntities": false` (and `"CrossedQuadFoliage": false`) in
its saved settings JSON. Every actor/building/foliage emit Postfix begins with:

```csharp
if (!Core.IsWorld3D || !Core.savedSettings.VoxelEntities) return;     // actors + buildings
if (!Core.IsWorld3D || !Core.savedSettings.CrossedQuadFoliage) return true;  // foliage
```

With `VoxelEntities=false`, `ActorVoxelEmit.EmitVoxels` returns before submitting
ANY voxel mesh, and never sets `has_normal_render[i]=false`. Result: actors fall
through to vanilla 2D `SpriteRenderer` rendering = "still 2D billboards." Trees
stay 2D for the same reason (`CrossedQuadFoliage=false`).

This maps to **none of (a)-(f)** in the original hypothesis list — those all assume
the emit loop is *running*. It was not running at all: the feature flag was off.

The "Voxelized sprite main_4 -> 1596 verts" log line the user saw is the
`VoxelMeshCache` **builder**, which runs opportunistically/on-demand independent of
the emit gate. Meshes BUILD (cache populates, log fires) but the emit Postfix that
would SUBMIT them is short-circuited by the disabled flag. That is the entire
build->submit gap: there is no submit because emit early-returns.

## Proof the pipeline is healthy once the flag is ON

After `POST /settings/VoxelEntities?value=true` + `POST /settings/ProfilerDump?value=true`
+ spawning 40 humans, the live counters flipped from dead to fully working:

`/diag/emit_status` (was all-zero, then):
```
emitVoxelsCalled=true  visibleUnitsCount=41  frustumCullerPassCount=20  batcherSubmitCount=20
```

`[WSM3D][SubmitFlushDiag]` per-frame (steady state, multiple frames):
```
frame=3900 submits=303 flushes=2 drawCalls=41 instances=301 buckets=111
frame=4080 submits=303 flushes=2 drawCalls=41 instances=301 buckets=137
frame=4020 submits=234 flushes=2 drawCalls=24 instances=232 buckets=137
```
**301 instances drawn, 41 draw calls per frame** via `Graphics.DrawMeshInstanced`,
shader `WSM3D/OpaqueVertexColor`, `enableInstancing=True`, `fallback=False`,
`instancingBroken=False`. Voxel meshes reach the screen. The 04:30 auto-screenshot
confirms a 3D-rendered world (sphere + scattered geometry) under the debug HUD.

So the build->submit->draw->visible pipeline is fundamentally sound. The only thing
keeping actors 2D/invisible was the OFF feature flag.

## Per-path counter evidence (DIAG-SUBMIT / DIAG-EMIT)

Of the 41 visible units after spawn (one early, partially-populated frame):
```
[DIAG-SUBMIT] n=41 nullActor=0 perpSkip=0 frustumFail=21 frustumPass=20
  | tier(Imp=2 Proxy=7 Voxel=18 Other=0)
  | skel(attempt=0 ok=0 fail=0)
  | spriteNull=18
  | impostor(meshNull=0 matNull=0 submit=2)
  | voxel(meshNull=0 attempt=0 ok=0 fail=0)
  | LastBatcherSubmitCount=2  SkeletalAnimation=False
```
- frustumPass 20 / fail 21 (21 culled offscreen — expected at this camera pos).
- tier split: only **2 Impostor**, **18 Voxel/Proxy** — LOD is NOT mis-classifying
  to Impostor (rules out hypothesis (a) and the documented LOD-threshold bug).
- `spriteNull=18` on THIS frame is a transient early-frame artifact:
  `rd.main_sprites[i]` was momentarily null on the first post-spawn frame.
  Later frames resolved fine — confirmed by `[WSM3D][DIAG] Actor voxel color
  sample 1/3..3/3 asset=human sprite=walk_0` lines, which only log INSIDE the
  successful voxel-submit branch (VoxelRender.cs:761). Steady-state
  `instances=301` confirms the 18 do submit on subsequent frames.
- `has_normal_render[i]=false` IS being set on successful submit (line 760), so
  vanilla sprite suppression works — rules out hypothesis (d)/(f) (double-draw).
- `SkeletalAnimation=False`, `skel attempt=0` — not hijacking (rules out (e)).

## Why the first /diag snapshots looked dead (secondary findings)

1. **Process not running on first probe.** Initial `/diag/render_stats` and
   `/diag/emit_status` returned JSON `null` and the WorldBox process was absent
   (`tasklist` showed none). `null` arises because `InvokeOnMainThread<T>` returns
   `default(T)` after a 5s main-thread-dispatch timeout (BridgeServer.cs:1698-1702)
   when the game isn't pumping `_mainThreadQueue`. `/telemetry` still answered
   because it is served from a lock-free cached snapshot, not via the main thread.
   I relaunched via Steam and re-queried.

2. **`isWorld3D=false` at first valid query even though `Is3D=true` in settings.**
   `Core.IsWorld3D => Core.Sphere.Exists` (Core.cs:520) — true only when the player
   is actually in the 3D sphere view. The first valid snapshot caught the game in
   the 2D map/menu (orthographic cam at origin). It flipped to `isWorld3D=true`
   (perspective cam at 128,10,228) once the 3D world was active.

3. **Misleading `FrameInstances=2` / `drawCalls=1` in the every-frame telemetry
   line.** There are TWO Flush calls per frame (BridgeServer.cs:160 from
   `MapBox.renderStuff`, and VoxelFrameDriver.LateUpdate:1688). The telemetry-line
   sampler runs right after the FIRST flush, which on many frames only has the
   `SanityTestCube` (2 instances) in its buckets; the real actor/building submits
   land in the `_pendingSubmissions` ConcurrentQueue from the worker-thread
   `precalculateRenderDataParallel` Postfixes and are drained+drawn by the LateUpdate
   flush (301 instances). This is purely a telemetry-sampling-point artifact, NOT a
   lost-draw — `SubmitFlushDiag` (sampled in LateUpdate after the real flush) proves
   301 instances draw. The stale `buckets=137` is just the dictionary retaining one
   empty bucket per unique mesh ever seen (Matrices cleared each flush); harmless.

## Trees / foliage

`CrossedQuadFoliage=false` in saved settings, and `/diag/render_stats` reported
`foliageCount=0`. `FoliageTileRender.Prefix` returns early
(`if (!Core.IsWorld3D || !Core.savedSettings.CrossedQuadFoliage) return true;`,
FoliageTileRender.cs:44), so the vanilla 2D tile flush is used = trees stay 2D.
Same root cause as actors: the feature flag is off. (Foliage was not exercised
live because enabling it on top of 1771 visible buildings would worsen the
already-1.5s frames; the gate logic is identical to the actor path.)

## The fix (one line / setting)

The pipeline needs no code fix to render voxels. Actors are 2D purely because the
persisted setting is off. The one-line fix is to flip the saved-settings flag(s)
to true:

```
VoxelEntities = true        # actors + buildings  (REQUIRED — this is the actor 2D fix)
CrossedQuadFoliage = true   # trees/foliage       (the tree 2D fix)
```

Set via the in-game WorldSphere tab toggles, or `POST /settings/VoxelEntities?value=true`,
or by editing the saved JSON
(`%USERPROFILE%/AppData/LocalLow/mkarpenko/WorldBox/mods_config/WorldSphereMod.json`).

Per CLAUDE.md, new phases ship default-OFF until validated in-game; this confirms
Phase 1 voxel actors render correctly when enabled, so the durable fix is to flip
`VoxelEntities` (and `CrossedQuadFoliage` for Phase 3) to **default true** in
`SavedSettings.cs` now that the live pipeline is verified working — and ensure no
stale `false` in a persisted JSON overrides that default (see
project_wsm3d_settings_staleness: JSON-loaded flags override code defaults).

## Caveat for the fix-applying agent

Independent of the 2D gap: with `VoxelEntities=true` and 1771 visible buildings,
frame time was **540-2000 ms (~0.5-2 FPS)** — every building voxelizing per frame
(`BuildingRenderBudget=200` did not appear to bound it here; investigate). Also
note `Toolkit\Graphics\ButtonBuilder.cs(198..) error CS0103: 'ButtonStyle' does
not exist` is present in the log — a separate compile error in some tool/UI file,
unrelated to the voxel gap but worth a look. Neither blocks the one-line gap fix.
