# Consumer-Side Patch vs Own-the-Substrate Balance

**Generated:** 2026-06-02  
**Branch:** wip/robustness

## Current Balance

| Approach | Count | Files |
|----------|-------|-------|
| Harmony prefix/postfix methods | 78 | 21 files |
| Owned MonoBehaviour components | 28 | — |
| `[HarmonyPatch]` class attributes | 2 (explicit) | — |

The mod is **patch-heavy by necessity** — it must intercept WorldBox internals (Camera, MapBox,
ActorManager, SaveManager) that cannot be subclassed. This is the correct approach for an NML mod.

## What Is Being Patched (consumer-side)

```
WorldBox internals patched:
  Camera.orthographicSize (get/set)      — 3D camera projection override
  MoveCamera.update / move / resetZoom   — 3D camera movement
  ZoneCamera.update                      — zone camera 3D conversion
  WorldAgeEffects.fitTheCamera           — camera fit override
  ControllableUnit.updateMovement*       — actor movement in 3D
  PixelDetector.IntersectsSprite         — 2D→3D hit-test override
  MapBox.renderStuff                     — per-frame voxel tick injection
  ActorManager.precalculateRenderDataParallel — voxel emit hook
  SaveManager.loadWorld                  — post-load world setup
  PlayerControl.clickedFinal             — input capture
  PowerButtonSelector.setSelectedPower   — input capture
  (+ ~65 more in Voxel, ProcGen, Foliage, Rig, Worldspace, UI)
```

## What Is Owned (substrate)

```
28 MonoBehaviour components owned by WSM3D:
  BridgeServer, BridgePerFrameTick       — HTTP server + tick driver
  VoxelFrameDriver                       — voxel render loop driver
  SunDriver, TimeOfDay, SunRig           — lighting
  ProceduralSky, PostFxController        — visual effects
  HealthBar, NameplateWorld, etc.        — worldspace UI
  WeatherDriver, WindSwayDriver          — environment
  ProfilerFrameDriver                    — telemetry
  (+ others)
```

## Risk Assessment

**Low risk (by design):**  
Harmony patches on WorldBox internals are the only way to inject 3D rendering into a 2D engine.
The `PhasePatchGate` pattern (`[Phase]` attribute + conditional application) ensures patches only
apply when the relevant phase is enabled, limiting blast radius.

**Elevated risk (watch list):**  
- `MapBox.renderStuff` postfix (BridgePerFrameTick) — this fires every frame; any exception here
  silently swallows per-frame updates. Has try/catch, but exception rate is not surfaced in telemetry.
- `ActorManager.precalculateRenderDataParallel` postfix — runs in a parallel context; thread-safety
  of any mutable state touched here must be carefully maintained.
- Camera patches (21 methods in 3DCamera.cs) — large surface area; a WorldBox update that renames or
  changes camera method signatures will silently break 3D rendering without a compile error (Harmony
  resolves at runtime).

**Upstreaming candidates:**  
None — all patches are on WorldBox internals which WSM3D does not own. The correct long-term
path is for WorldBox to expose extension points (an event system or mod API). Until then,
Harmony patches remain the only viable approach.

## Recommendation

The current balance is appropriate for the mod architecture. No changes recommended.
Focus stability efforts on:
1. Adding runtime detection when key patches fail to apply (Harmony.GetPatchInfo smoke-test in PostInit).
2. Surfacing MapBox.renderStuff exception rate in bridge telemetry.
3. Maintaining a compile-time check list of patched method signatures (so WorldBox updates that
   break them are caught immediately rather than silently at runtime).
