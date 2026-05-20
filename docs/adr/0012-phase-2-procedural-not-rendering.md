# ADR-0012 — Phase 2 procedural-mesh path silent no-op

## Status

Active diagnosis (2026-05-19).

## Context

Phase 1 voxel actors landed visibly (ADR-0011). The `[Phase("ProceduralBuildings")]` Postfix in `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs` was wired correctly — `PhasePatchManager.ApplyPhaseToggle("ProceduralBuildings", true)` patches the type into Harmony at toggle time. But AutoTest's per-phase telemetry shows `drawCalls=0` for the ProceduralBuildings phase even when the world contains tens of thousands of buildings (save2: `visBuildings=43143`).

## Diagnostic methodology

We rejected "guess the cause and patch" in favor of **instrument the funnel and read the dump**. A Postfix that filters a million inputs to zero outputs has a single chokepoint somewhere on the path — naming the chokepoint is cheaper than guessing it.

### Instrumentation pattern

Add per-gate counters at the top of the Postfix and at every `continue` or early return inside the for-loop. Dump on a short cadence so AutoTest's 3-second on-window produces multiple dumps:

```csharp
static int _diagEntryCount;
static int _diagSeenAtFlagOn;
static int _diagFiltPerp;
static int _diagFiltCull;
static int _diagSubmitted;
static int _diagLastReport;

public static void EmitMeshes(BuildingManager __instance)
{
    _diagEntryCount++;
    if (_diagEntryCount - _diagLastReport >= 15)
    {
        _diagLastReport = _diagEntryCount;
        Debug.Log($"[WSM3D] ProcMeshEmit diag entries={_diagEntryCount} seenAtFlagOn={_diagSeenAtFlagOn} filtPerp={_diagFiltPerp} filtCull={_diagFiltCull} submitted={_diagSubmitted} ...");
    }

    if (!Core.IsWorld3D) return;
    if (!Core.savedSettings.ProceduralBuildings) return;
    _diagSeenAtFlagOn++;

    // ... for each building ...
    if (Constants.PerpBuildings.ContainsKey(b.asset.id)) { _diagFiltPerp++; continue; }
    if (!FrustumCuller.IsVisible(cullPos, radius)) { _diagFiltCull++; continue; }
    // ... submit ...
    if (submitted) { _diagSubmitted++; ... }
}
```

This separates four failure modes:

1. **`entries == 0`** → Postfix never runs. Bug is in patch registration (`PhasePatchManager.ApplyPhaseToggle` HashSet sync vs init filter).
2. **`entries > 0` but `seenAtFlagOn == 0`** → flag stays false despite AutoTest's `SetPhase(true)`. Bug is in `Core.savedSettings.ProceduralBuildings` plumbing or `Core.IsWorld3D`.
3. **`seenAtFlagOn > 0` but `submitted == 0` with `filtPerp` or `filtCull` rising** → the named filter is eating all input. Fix the filter or move past it.
4. **`seenAtFlagOn > 0`, all filters at 0, `submitted == 0`** → builds inside the per-building branch (`BuildingRules.Resolve` → no-shape, `ProcGenCache.GetOrGenerate` returns null, `VoxelRender.Submit` returns false) — instrument those next.

### Why short dump cadence matters

AutoTest cycles each phase ON for 3 seconds. At ~20-60 Postfix entries/s, that's 60-180 entries during the on-window. A 60-entry cadence (one dump per second of the wall clock) gives 3 dumps inside the on-window — enough to confirm the flag stayed on. Dropped to 15-entry cadence on the second iteration to catch shorter on-windows and slow-frame moments.

## Consequences

1. The Phase 2 invisibility bug will be named in concrete terms (e.g. "FrustumCuller rejects all building positions because `cullPos.z=0` collapses the bounding sphere"), not guessed at.
2. The same instrumentation pattern applies to any future phase-postfix that submits 0 with no obvious error. Phases 5 (HighShadows), 8 (DayNightCycle) are the next candidates needing this pattern.
3. Granular counters add a few cycles per render frame; remove them once the bug is named (or move behind a `Core.savedSettings.DebugVerbosePhase2` flag).

## Outcome (17:30 capture)

```
entries=15 seenAtFlagOn=14 filtPerp=728 filtCull=603260 submitted=0 flag=True visBuildings=43142
entries=30 seenAtFlagOn=27 filtPerp=1404 filtCull=1163430 submitted=0 flag=False ...
```

Identified the chokepoint at the first dump. In 14 Postfix iterations
with the flag on (~604k building checks), `FrustumCuller` rejected
603,260 — **>99.8%**. The other 728 hit the `PerpBuildings` filter
upstream of the cull test.

**Root cause:** `rd.positions[i]` returns the raw 2D tile-space position
(`z == 0`). The camera frustum is in 3D world space. A `Bounds(pos,
(1,1,1))` AABB at `z=0` against a frustum looking at the lifted
`z ≈ 100` ground plane fails for every building. The lift via
`To3DTileHeight(false)` happened inside the per-tier (`Impostor` /
voxel / procedural) branches — **after** the cull check.

**Fix** (commit `fa4f130`):

```csharp
Vector3 cullPos = rd.positions[i];
if (cullPos.z == 0f)
{
    cullPos = cullPos.To3DTileHeight(false);
}
float radius = 2f; // was 0.5f — too small for multi-tile buildings
if (!FrustumCuller.IsVisible(cullPos, radius)) { ... continue; }
```

Same lift logic that the impostor branch already applied, just hoisted
above the cull test. Bumped `radius` to 2.0 — a 1×1×1 box was too small
to be meaningful for buildings spanning multiple tiles.

## Linked

- ADR-0011 (Phase 1 visibility postmortem)
- ADR-0005 (Default-on flags per phase ship gate)
