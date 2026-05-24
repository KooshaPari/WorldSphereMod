# ADR-0010 — 3D cloud rendering for WorldBox weather sprites

**Status:** Proposed  
**Date:** 2026-05-19  
**Author:** KooshaPari

## Context

Current rendering for clouds is still driven by the effect/sprite stack:
- `WorldSphereMod/Code/Constants.cs:29` maps `fx_cloud` to `new EffectData(false, true, 21, false)`, which marks it as a separated sprite effect.
- `WorldSphereMod/Code/Effects.cs` (`ShouldSeperateSprite`, `SeperateSprite`, `UpdateCloud`) routes cloud logic to `SpriteRenderer` management, with a dedicated `Cloud.update` Harmony postfix (`[HarmonyPatch(typeof(Cloud), nameof(Cloud.update))`).
- No cloud-specific mesh path exists in the current checked-in code; only sprite transforms/shadow updates occur in the cloud patch.

No in-repo screenshot captures clouds as 3D yet; there is no local screenshot reference in docs. Use a fresh capture at validation time (e.g., `docs/journeys/assets/phase8/` or a new `phase10-clouds` asset path) as baseline.

## Decision

Adopt **crossed-quad cloud renderers as the default implementation** (reusing the Phase 3/`Foliage` crossed-quad machinery), with a **future optional follow-up** for volumetric cloud meshes when a volumetric billboard budget is needed.

### Chosen approach

- **Primary now:** build each cloud as two or three low-cardinality crossed quads in 3D (same vertex/material path as foliage, with cloud-specific shading) and submit through `MeshInstanceBatcher`.
- **Fallback path:** keep `WorldSphereMod` sprite cloud rendering behind `CloudMeshes == false` (default for safety).
- **Future optional:** volumetric meshes can be introduced behind the same `CloudMeshes` gate by adding an alternate `CloudMeshGenerator` in the same cache/pipeline.

### Why not volumetric first

- cross-quad path is materially lower risk and matches existing Phase 3 infrastructure.
- existing assets are thin sprite art; volumetric extrusion requires extra shading/alignment work.
- phased rollout is aligned with the feature-flag contract (`SavedSettings` default false until smoke-test).

## Consequences

### Positive

- Minimal integration surface: mostly reuse current effect lifecycle patches and mesh batching (`MeshInstanceBatcher`).
- Preserves 60Hz target better than immediate per-cloud volumetric reconstruction.
- Gives consistent visual direction with Phase 3 foliage while still making clouds non-billboarded in the 3D scene.

### Negative

- Crossed-quads will still look stylized and thin versus fully volumetric cumulus forms.
- Per-instance wind/alpha animation currently needs a cloud-specific shader variant (not currently implemented).
- Needs cache invalidation + world-unload cleanup to avoid stale mesh instances.

### Neutral

- Keep upstream cloud spawn logic in place; only rendering representation is swapped.
- Existing effect animation data remains untouched, so spawn timing and lifecycle are preserved.

## Implementation outline

1. Add `SavedSettings` flag `CloudMeshes` (default `false`) and gate all cloud mesh wiring behind it.
2. Add cloud-specific effect metadata in `Constants.cs` / `EffectData` (small explicit marker for `fx_cloud`, e.g., `EmitCrossedQuad` or `UseCloudMesh`) so behavior is explicit and doesn't depend on `id` checks in runtime hot paths.
3. Add a `CloudMeshMesher` utility under `WorldSphereMod/Code/Foliage` (or new `WorldSphereMod/Code/Fx/Clouds`) that generates crossed-quad cloud mesh meshes and optional atlas UV/pseudo-depth data.
4. Add `CloudMeshCache` adjacent to other caches (`CrossedQuadMeshCache`/`VoxelMeshCache`) with life-cycle methods (`GetOrBuild`, `Tick`, `DrainPendingDestroy`, `InvalidateAll`).
5. Add/extend cloud shader in `WorldSphereMod/Resources/Shaders/` for soft alpha falloff and depth-aware fade near horizon; keep it in the same shader family as other lightweight world effects.
6. Implement `CloudMesherMaterial` helper (or reuse a new lightweight `WorldSphereMod.Fx.CloudMaterial`) that ensures a material is created/owned and swapped for mesh submit.
7. Patch `Cloud.update` with a `HarmonyPostfix` to replace sprite submission with mesh submission when `CloudMeshes && Core.IsWorld3D`, disabling / hiding `sprite_renderer` to prevent double-draw and preserving position/height alignment behavior from `EffectManager`.
8. Add lifecycle guards: `BaseEffect.deactivate`, `BaseEffectController.clear`, and world-unload hooks should hide/cleanup spawned cloud mesh instances so toggling phase off does not leak render state.
9. Add `Clouds` section to docs/screenshots or journey manifests and phase-8/10 smoke checks: off-state cloud screenshot vs on-state cloud mesh screenshot.
10. Add minimal API/profiling counters: active cloud mesh count, generated mesh count, and fallback count when a cloud sprite is not mesh-convertible.

## Open questions

1. Does WorldBox expose a cloud animation vector (drift/scale) we can reuse, or is cloud drift entirely driven by world camera/culling timing in this codebase?
2. How large is the cloud sprite catalog in vanilla/active modded content (one-off set vs many variants), and is the catalog stable enough to make a mesh cache useful without extra hashing pressure?

## Patch surface (single source of truth)

Primary patch point for replacing render representation: `Cloud.update` (`[HarmonyPatch(typeof(Cloud), nameof(Cloud.update))]` in `WorldSphereMod/Code/Effects.cs`).
This method is already active and is the best candidate for a postfix that swaps sprite-driven drawing for mesh-driven drawing in 3D mode.

## SavedSettings proposal

`CloudMeshes` (bool, default `false`)

## References

- `WorldSphereMod/Code/Effects.cs:29`, `:262`, `:264`
- `WorldSphereMod/Code/Constants.cs:29`
- `docs/phase3-architecture.md` (cloud refactor note around lines ~15, ~198, ~242, ~255)
- `docs/adr/0001-hybrid-sprite-to-3d-strategy.md`
