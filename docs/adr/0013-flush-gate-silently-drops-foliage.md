# ADR-0013 — MeshInstanceBatcher Flush gate silently dropped Phase 3+ submits

## Status

Resolved (commit `a0ad72c`, 2026-05-19).

## Context

`MeshInstanceBatcher.Submit(mesh, material, matrix, color)` is the central
entry point for any phase that wants to draw a 3D mesh through the WSM3D
batcher. Submissions accumulate in per-(mesh, material) buckets. Buckets are
emptied + actually drawn by `MeshInstanceBatcher.Flush()`, which is called
once per frame from `VoxelFrameDriver.LateUpdate()`.

The driver had a gate:

```csharp
if (Core.savedSettings.VoxelEntities || Core.savedSettings.ProceduralBuildings)
{
    VoxelRender.Flush();
    VoxelMeshCache.DrainPendingDestroy();
}
```

This was correct for Phase 1 (`VoxelEntities`) and Phase 2 (`ProceduralBuildings`), but it failed to include other phases that also submit through the batcher:

- `FoliageTileRender.OnRender` — `[Phase("CrossedQuadFoliage")]`
- `WallTileRender.drawWallType` — `[Phase("CrossedQuadFoliage")]`
- `SanityTestCube.Draw` — `[DebugSanityCube]`

When `VoxelEntities` and `ProceduralBuildings` were both off but `CrossedQuadFoliage` was on, foliage code would call `Submit`, the buckets would fill up, but the driver's gate would short-circuit and `Flush` would never run. The submissions were silently dropped — the bucket cleared on the next gate-pass without ever being drawn.

## How we noticed

We added a per-phase peak-drawCalls window to `AutoTestDriver` (commit `3f3e263`). The expectation: each phase row reports its own peak draw load. The observed pattern:

```
phase=VoxelEntities       peakDrawCalls=531 peakInstances=29692
phase=ProceduralBuildings peakDrawCalls=235 peakInstances=14494
phase=CrossedQuadFoliage  peakDrawCalls=232 peakInstances=14435  ← stuck
phase=MeshWater           peakDrawCalls=232 peakInstances=14435  ← stuck
phase=HighShadows         peakDrawCalls=232 peakInstances=14435  ← stuck
...   (all subsequent identical)
```

The identical `232/14435` across eight unrelated phases was the smoking gun. `MeshInstanceBatcher.FrameDrawCalls` is reset to 0 at the **top** of `Flush`. If `Flush` never runs, the field never resets. The value freezes at whatever the last actual flush produced — in this case Phase 2's tail.

## Fix

Widen the gate to include every phase that can submit:

```csharp
bool anyEmitPhaseOn = Core.savedSettings.VoxelEntities
    || Core.savedSettings.ProceduralBuildings
    || Core.savedSettings.CrossedQuadFoliage
    || Core.savedSettings.DebugSanityCube;
if (anyEmitPhaseOn) { VoxelRender.Flush(); VoxelMeshCache.DrainPendingDestroy(); }
```

A blanket `Flush` every frame would also work — empty buckets iterate in microseconds — but the gate documents the intent and avoids touching the path on no-op frames.

## Why this happened

Phase 3 (`CrossedQuadFoliage`) was the first phase to land that submitted through the batcher *outside* of `VoxelRender.cs` / `BuildingProcRender.cs`. The Flush gate was written when `VoxelRender.Flush()` itself owned both Submit and Flush — coupling the gate to the same flag set the Submit codepath checks made local sense. Once foliage was added in another file with its own `[Phase]` attribute, the gate became a hidden coupling no one noticed.

## Consequences

1. **Phase 3 was never actually rendering** through this code path despite being marked default-on. The screenshots that showed "3D trees" were vanilla WorldBox foliage, not the crossed-quad mesher. ⚠️ Phase 3's "landed" status needs re-verification post-fix.
2. **Phase 5/8/9** when their AssetBundle bakes ship will also Submit through this path — verify the gate covers them before flipping their flags on.
3. **The peak-telemetry pattern is now reliable** — Phase 3+ rows reflect their own work. Use the same AutoTest cycle to validate any future phase before flipping its default-on flag.

## Followup

- Re-run AutoTest cycle on save2 post-fix; capture peak-drawCalls for Phase 3 specifically. Expect non-zero, distinct from Phase 1/2.
- Audit any future `Phase` attribute landing for a Flush-gate update in `VoxelFrameDriver.LateUpdate`.
- Consider replacing the explicit OR list with a single `MeshInstanceBatcher.HasPendingSubmissions` getter so the gate auto-tracks new phases.

## Linked

- ADR-0011 (Phase 1 visibility postmortem)
- ADR-0012 (Phase 2 procedural diagnosis methodology)
