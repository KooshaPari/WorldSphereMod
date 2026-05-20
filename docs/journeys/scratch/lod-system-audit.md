# LOD System Audit

Scope: `WorldSphereMod/Code/LOD/LodSelector.cs` and the call sites that feed it.

## 1) Threshold sanity

The tier cuts are not literal world distances; they are screen-size thresholds:

- `VoxelThreshold = 0.08f`
- `ProxyThreshold = 0.025f`
- Distance cutoffs are derived from `entityHeight * LODScale / (threshold * tan(fov/2))` in `Select()` ([LodSelector.cs:12-13](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/LodSelector.cs#L12-L13), [LodSelector.cs:44-50](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/LodSelector.cs#L44-L50)).

That means both cutoffs scale with camera FOV and `Core.savedSettings.LODScale`, so the values are internally consistent rather than arbitrary magic numbers. The proxy cutoff is about 3.2x farther than the voxel cutoff, which is a reasonable separation for a 3-tier LOD stack. The only caveat is that the constants are still hard-coded defaults, so scene-scale tuning remains external.

## 2) Hysteresis / flicker

Hysteresis is per instance id and requires 3 consecutive frames of the same proposed tier before switching current tier ([LodSelector.cs:15-22](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/LodSelector.cs#L15-L22), [LodSelector.cs:70-97](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/LodSelector.cs#L70-L97)).

Behavior near the voxel/proxy or proxy/impostor boundary:

- A one-frame oscillation will not flip tiers.
- If an object stays on the other side of the boundary for 3 frames, it transitions once and then stays there until the proposed tier changes again.
- There is no geometric deadband; this is frame-debounce, not distance hysteresis.

So the selector should not flicker back and forth every frame, but it can lag by up to 3 frames when crossing a boundary.

## 3) Determinism

`Select(worldPos, instanceId)` is deterministic only if you include selector history. The pure geometric part is deterministic for the same `worldPos`, camera, `LODScale`, and thresholds ([LodSelector.cs:36-68](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/LodSelector.cs#L36-L68)). But the returned tier also depends on `_hyst[instanceId]`, which stores `current`, `pending`, and `pendingFrames` ([LodSelector.cs:15-22](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/LodSelector.cs#L15-L22), [LodSelector.cs:70-97](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/LodSelector.cs#L70-L97)). So it is not a pure function of `(pos, hashCode)`.

## 4) Per-actor / per-building state

Yes: the selector keeps per-entity state in `_hyst` keyed by `instanceId` ([LodSelector.cs:22,70-72](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/LodSelector.cs#L22-L22), [LodSelector.cs:70-72](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/LOD/LodSelector.cs#L70-L72)).

Cleanup is partial:

- World unload clears the whole dictionary via `ResetHysteresis()` ([WorldUnloadPatch.cs:17-27](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs#L17-L27)).
- `WorldUIRenderer.UnregisterActor()` removes the actor entry when a visible actor is reaped ([WorldUIRenderer.cs:136-144](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs#L136-L144)).
- I did not find a comparable building-specific eviction path for entries created from `b.GetHashCode()` in `BuildingProcRender` / `BuildingVoxelEmit` ([BuildingProcRender.cs:51](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/ProcGen/BuildingProcRender.cs#L51-L51), [VoxelRender.cs:470](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Voxel/VoxelRender.cs#L470-L470)).

Net: state is indeed per actor/building, but buildings appear to rely mostly on world-unload reset rather than lifecycle eviction.
