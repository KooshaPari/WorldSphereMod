# Harmony patch inventory (autonomous build, gameperf-04 substitute)

**Date:** 2026-05-19
**Built by:** manual grep + python script after codex gameperf-04 worker
disconnected at 75k tokens. See [harmony_inventory.py](C:/Users/koosh/.claude/jobs/b012a2c2/harmony_inventory.py)
for the regex + classifier.

## Totals

- **91** `[HarmonyPatch(typeof(...))]` attributes total across WorldSphereMod/Code/
- **15** ALWAYS-ON + HOT (per-frame methods, no `[Phase(...)]` guard)
- **11** Phase-gated (gated by some `SavedSettings.<Phase>` via `[Phase(nameof(...))]`)
- **65** ALWAYS-ON + cold (one-shot setup, draw-root, dist-helpers, world load/unload, etc)

## The 15 always-on hot patches

These run every frame regardless of any 3D-phase SavedSettings flag.

```
MoveCamera.update                            3DCamera.cs:81    camera per-frame update
MoveCamera.updateMouseCameraDrag             3DCamera.cs:264   mouse drag handling
MoveCamera.updateVelocity                    3DCamera.cs:455   velocity smoothing
BaseEffect.update                            Effects.cs:253    base effect per-frame
Cloud.update                                 Effects.cs:262    cloud per-frame
SpriteShadow.LateUpdate                      Effects.cs:289    shadow placement
SpriteAnimation.update                       Effects.cs:326    sprite anim ticking
Crabzilla.update                             General.cs:357    crabzilla AI
QuantumSpriteLibrary.drawProjectiles         QuantumSprites.cs:367  per-frame projectile draw
QuantumSpriteLibrary.drawStatusEffectFor     QuantumSprites.cs:382  per-frame status icon draw
QuantumSpriteLibrary.drawBuildings           QuantumSprites.cs:395  per-frame building draw
QuantumSpriteLibrary.drawQuantumSprite       QuantumSprites.cs:406  the per-sprite draw root
ActorManager.precalculateRenderDataParallel  QuantumSprites.cs:448  Voxel + skeletal + impostor postfix gate (also has VoxelEntities-gated twin)
BuildingManager.precalculateRenderDataParallel  QuantumSprites.cs:607  building render data postfix
ZoneCamera.update                            TileMapToSphere.cs:12  zone camera positioning
```

## Phase-gated patches (well-behaved)

- `VoxelEntities`: `VoxelRender.ActorVoxelEmit` (line 285), `VoxelRender.BuildingVoxelEmit` (line 437)
- `ProceduralBuildings`: `BuildingProcRender.EmitMeshes` (line 15)
- Other phase-gated hooks live under `Foliage/`, `Water/`, `Worldspace/SelectionHooks.cs`

## Recommendations (ranked)

1. **Audit the 4 `QuantumSpriteLibrary.*` always-on draw patches** — they form
   the per-sprite draw root and run for every visible entity per frame even
   when 3D rendering is disabled. If they internally short-circuit on
   `!Core.IsWorld3D`, fine; if not, they're paying upstream cost for nothing.
   Worth a single one-time read of QuantumSprites.cs:367-410 to confirm.

2. **`ActorManager.precalculateRenderDataParallel` has TWO patches** — one
   ALWAYS-ON at QuantumSprites.cs:448 (the upstream-driven 3D conversion
   path) and one VoxelEntities-gated at VoxelRender.cs:285. The first is
   load-bearing for the 3D world; not a candidate for gating.

3. **The 3 MoveCamera patches** at 3DCamera.cs:81/264/455 fire every frame
   on camera control. They're the price of admission for the 3D camera and
   not easily Phase-gated.

4. **Effects.cs always-on patches** (BaseEffect.update, Cloud.update,
   SpriteShadow.LateUpdate, SpriteAnimation.update) — these all dispatch
   through SetEffect3D path. Check whether they internally early-out when
   `Core.IsWorld3D == false`; if not, they're paying full conversion cost
   while the player is in 2D mode.

5. **`Crabzilla.update`** at General.cs:357 only matters when Crabzilla is
   spawned. Effectively self-gating; no action.

## Methodology notes

- Phase detection: walks back up to 8 lines from each `[HarmonyPatch]` attribute
  looking for `[Phase(nameof(SavedSettings.X))]`.
- Hot detection: regex match against per-frame method names (Update / LateUpdate
  / FixedUpdate / drawProjectiles / drawBuildings / drawStatusEffect /
  drawUnitsAvatars / drawQuantumSprite / precalculateRenderData / RotateToCamera).
- Both heuristics are intentionally conservative — file false negatives over
  false positives, since wrongly flagging a one-shot patch as "hot" is more
  expensive (someone investigates a non-issue) than the inverse.

## Follow-up

- Codex worker gameperf-04 disconnected after Reconnecting... 5/5 retries with
  75,868 tokens consumed (network error: stream disconnected). If a deeper
  per-target cost-ranking is needed later, re-dispatch the worker with a
  smaller scope (e.g., just the 4 QuantumSpriteLibrary patches) to fit inside
  one connection window.
