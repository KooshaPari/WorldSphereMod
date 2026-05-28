# ADR-0010 — Voxel Actor Visibility

**Status:** Resolved (alpha.8, 2026-05-21)
**Date:** 2026-05-25
**Supersedes:** ADR-0011, ADR-0015 (which cover subsets of the same root-cause chain)

## Context

Phase 1 replaces WorldBox's 2D `SpriteRenderer` billboard actors with 3D
voxel meshes produced by `SpriteVoxelizer`, batched through
`MeshInstanceBatcher`, and submitted per-frame from Harmony postfixes in
`VoxelRender.ActorVoxelEmit` / `BuildingVoxelEmit` / `DropVoxelEmit` /
`ProjectileVoxelEmit`. Across seven alpha iterations (alpha.1 through
alpha.8), voxel actors were intermittently or permanently invisible despite
telemetry reporting non-zero submit counts. This ADR consolidates every
root cause found and fixed, the current fix state, and remaining gaps.

## Root Causes (billboard-vs-voxel)

### RC-1: Sub-pixel mesh scale (alpha.1 -- alpha.5)

Sprite render_data scales are ~0.1 (the sprite-scale WorldBox uses for
billboard quads). The original voxel emit multiplied that by
`VoxelScaleMultiplier=4.0`, producing meshes ~0.4 world-units tall against
tiles of height 1.0+. Effectively invisible at strategy-zoom altitude.

**Fix:** `VoxelScaleMultiplier` default raised from 4.0 to 8.0 in
`SavedSettings.cs`. The emit path in `ActorVoxelEmit` and
`BuildingVoxelEmit` multiplies `scl *= Core.savedSettings.VoxelScaleMultiplier`
before building the TRS matrix.

**Regression risk:** Changing the multiplier retroactively (8 -> lower)
re-hides actors. The setting is exposed in the in-game tab and persisted to
JSON, so a stale JSON file with the old value (4.0) silently overrides the
code default. See ADR-0014 for the settings-staleness lifecycle problem.

### RC-2: Emission = BLACK under Standard shader (alpha.5 -- alpha.7)

WorldBox's scene has no directional or ambient light reaching the voxel
layer. The `Standard` shader computes `diffuse * lightContribution`, which
is zero without lights, so every voxel fragment renders as black regardless
of vertex color or `_Color`.

**Fix chain:**
1. `_EMISSION` keyword enabled, `_EmissionColor` set to 0.5 grey (compromise
   between self-lit and allowing per-instance `_Color` tints).
2. Raised to 1.5 super-bright after grey still read as dark against terrain.
3. `MeshInstanceBatcher` bakes a per-instance `_EmissionColor` of 0.15 via
   `MaterialPropertyBlock` to lift the floor without washing out vertex colors.
4. Inline `WSM3D/OpaqueVertexColor` shader (when baked via AssetBundle) replaces
   Standard entirely, making emission moot.

**Current state:** `EnsureMaterial()` tries the inline shader first. If the
AssetBundle is not loaded, falls back to Standard with the emission overrides
above. The shader fallback chain in `VoxelRender.EnsureMaterial()` documents
the full priority order and the per-shader pitfalls in inline comments.

### RC-3: AlphaTest discarding all fragments (alpha.4 -- alpha.6)

Standard shader's `_ALPHATEST_ON` keyword samples `tex2D(_MainTex, uv).a`.
Without a `_MainTex` assigned, the texture sample returns alpha=0, which
fails the `_Cutoff=0.5` test, discarding 100% of fragments.

**Fix:** `_ALPHATEST_ON` keyword explicitly disabled. `_Cutoff` set to 0.0.
`_MainTex` set to `Texture2D.whiteTexture` as a belt-and-suspenders measure
so any residual alpha-test path gets alpha=1.

### RC-4: renderQueue in transparent pass (alpha.5)

Default `AlphaTest` queue (2450) placed voxel meshes after all transparent
passes. Depth buffer interactions at that queue made meshes invisible at
camera altitude.

**Fix:** `renderQueue` forced to `Geometry + 1` (2001) so voxel meshes
render in the opaque pass immediately after terrain (2000), avoiding
z-fight ties with biome cubes.

### RC-5: Unreadable sprite atlas textures (alpha.6 -- alpha.7)

`SpriteVoxelizer.Build()` called `GetPixels32()` on atlas textures imported
without Read/Write enabled. The call threw silently, returning an empty mesh
that `VoxelMeshCache` cached permanently. Subsequent frames retrieved the
cached 0-vertex mesh and submitted it, counting as a draw call in telemetry
but producing no geometry.

**Fix:** `SpriteVoxelizer.Build()` now detects `!sprite.texture.isReadable`
and falls back to `RenderTexture` + `Graphics.Blit` + `ReadPixels` to
produce a readable copy. Both `VoxelMeshCache.Get()` and all emit sites
now guard `vertexCount > 0`.

### RC-6: 2D position vs 3D frustum cull (alpha.7)

`render_data.positions[i]` has `z=0` raw (2D sprite-space).
`FrustumCuller.IsVisible()` tests against 3D camera frustum planes. Without
lifting positions via `To3DTileHeight(false)` before the cull test, every
actor at z=0 failed the frustum check and was culled.

**Fix:** All emit sites (`ActorVoxelEmit`, `BuildingVoxelEmit`) now call
`cullPos = cullPos.To3DTileHeight(false)` before `FrustumCuller.IsVisible`.
The same lift is applied to the final position used for TRS matrix
construction.

### RC-7: LOD threshold vs actual mesh scale (alpha.7)

`LodSelector._entityHeight` was 0.5 (default sprite size).
`VoxelScaleMultiplier=8` made meshes 8x larger, but the LOD selector still
used the old height, causing every actor to fall to `Impostor` tier at
strategy-view altitude when it should have been `Voxel` tier.

**Fix:** LOD thresholds recalibrated to account for the actual world-space
mesh height after scale multiplication.

### RC-8: Settings staleness -- VoxelEntities=false in JSON (alpha.3+)

The code default for `VoxelEntities` was changed from `false` to `true`,
but existing on-disk JSON files still contained `"VoxelEntities": false`.
Newtonsoft deserialization overwrites the code default with the JSON value,
silently disabling the entire voxel pipeline. This was the single most
frequently re-encountered bug (5+ incidents across sessions).

**Fix:** `ApplySchemaVersionMigration()` now resets all `[Phase]`-gated
boolean fields to their code defaults on version mismatch. But see ADR-0014
for why this is insufficient as a long-term solution.

### RC-9: Late-patching vs init-patching

`PhasePatchManager.ApplyPhaseToggle()` can hot-patch Harmony hooks at
runtime when a setting changes. But `VoxelRender.EnsureMaterial()` runs
lazily on first `Submit()`. If `VoxelEntities` is toggled on after init,
the material may not have been resolved yet, causing the first frame's
submissions to silently fail (`_material == null`).

**Current state:** `EnsureMaterial()` is called at the top of each emit
postfix, so the first successful postfix invocation resolves the material.
The risk is a single lost frame of submissions between toggle-on and
material resolution. Acceptable for alpha; Phase 5's shader bake removes
the lazy resolution entirely.

## Current Fix State (alpha.8)

All nine root causes are fixed. The 7-step audit checklist for Phase 1+
shader-side regressions (from the alpha.8 victory session):

1. Transpiler guards: `[Phase]` attribute present on all emit classes.
2. renderQueue: forced to `Geometry + 1` (2001).
3. AlphaTest: `_ALPHATEST_ON` disabled, `_Cutoff=0.0`.
4. Y-lift: `pos.y += halfHeight` in all emit paths.
5. VoxelScaleMultiplier: default 8.0, applied via `scl *= multiplier`.
6. Per-instance `_Color`: brightness floor in `MeshInstanceBatcher.Submit()`
   clamps `tint` to white when `r+g+b < 0.6`.
7. Frustum cull: `To3DTileHeight(false)` before `FrustumCuller.IsVisible`.

## Remaining Gaps

1. **Standard shader emission is a workaround.** The inline
   `OpaqueVertexColor` shader (Phase 5 AssetBundle bake) is the real fix.
   Until the bundle is baked and loaded, the Standard+emission path is
   fragile: emission values were tuned by trial and error across 4 commits.

2. **Per-instance color clamping loses night/shadow tinting.** The
   `brightness < 0.6 => white` guard in `MeshInstanceBatcher.Submit()`
   prevents black actors but also prevents intentional dark tints from
   day/night cycle or shadow coverage. Phase 8 (day/night) will need to
   replace this with proper ambient lighting.

3. **Building scale clamp is ad-hoc.** `BuildingVoxelEmit` clamps voxel
   building scale to `BuildingMaxScale=3.0f` to prevent oversized sprite
   extrusion. Phase 2's procedural building meshes replace this path
   entirely, but the clamp has no principled basis if Phase 2 is disabled.

4. **No per-sprite VoxelScaleMultiplier.** All actors share one global
   multiplier. Sprites with very different pixel densities (e.g., 8x8 vs
   32x32) end up at very different world-space sizes. Phase 5+ should
   introduce per-asset or per-resolution scale curves.

5. **Proxy tier is a stub.** `SpriteVoxelizer.BuildProxy()` returns null.
   `LodTier.Proxy` falls through to the full voxel path. Phase 10 needs
   the half-res downsample proxy mesh to hit its performance target.

## Verification Criteria

- `[WSM3D] Settings sanity: VoxelEntities loaded=True default=True` in
  Player.log at startup.
- `[WSM3D] Voxel material resolved via 'WSM3D/OpaqueVertexColor'` (preferred)
  or `via 'Standard'` (fallback) in Player.log.
- `[WSM3D][Telemetry] instances=N` with N > 0 within 10s of world load.
- `[WSM3D] First-actor pos: raw=... lifted=... scl=...` with scl components
  in the range [0.5, 5.0] (not sub-pixel, not giant).
- Visual confirmation: voxel actors visible at strategy zoom, colored (not
  black), standing on terrain surface (not embedded).

## Linked ADRs

- ADR-0005: Default-on flags per phase ship gate
- ADR-0009: Voxel lit material chain
- ADR-0011: Phase 1 visibility postmortem (sub-pixel scale subset)
- ADR-0014: Settings lifecycle (staleness problem)
- ADR-0015: Actor invisibility final root causes (atlas + camera subset)
- ADR-0016: Phase 1 victory chain
- ADR-0018: Default-on flag cascade
