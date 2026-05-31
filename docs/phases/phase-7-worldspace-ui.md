# Phase 7 — Worldspace UI Kickoff

**Branch:** `feat/phase-7-ui-kickoff`
**Depends on:** Phase 6 (VAT animation) — landed
**Status:** Kickoff / planning

---

## Goal

Replace all 2D HUD overlays that follow actors (nameplates, health bars, damage
numbers, faction badges) with **billboarded 3D worldspace elements** rendered
at actor head positions, scaled and faded by view distance, and culled by the
existing `LodSelector` / `FrustumCuller` pipeline.

**Chosen approach — World Space Canvas + mesh quads.**
Unity's `Canvas` in `RenderMode.WorldSpace` handles text (via TextMeshPro) with
full batching; health bars and faction badges are thin `MeshRenderer` quads
submitted through `MeshInstanceBatcher` (same path as voxel actors). Damage
numbers are pooled WorldSpace TMP popups driven by `NameplatePool`. No screen-
space canvas is used in the new path; the legacy HUD stays as a
`SavedSettings`-gated fallback.

---

## Functional Requirements

| ID | Requirement |
|----|-------------|
| FR-WSM3D-UI-001 | An actor nameplate displaying the actor's name (or kingdom tag when name is empty) appears above the actor's head in 3D world space, billboarded to the camera, and fades to transparent beyond `NameplateFadeDistance` world units. |
| FR-WSM3D-UI-002 | A health bar quad appears above the nameplate; its fill fraction maps linearly to `actor.stats.hp / actor.stats.maxHp`; color transitions from green (>60%) through yellow (30-60%) to red (<30%). |
| FR-WSM3D-UI-003 | Floating damage numbers pop up at the actor's head position on each hit, drift upward over 0.6 s, and fade out; at most `DamagePopPoolSize` (default 64) are live simultaneously. |
| FR-WSM3D-UI-004 | A faction badge (circular icon, 16×16 px sprite slice from the faction atlas) appears to the left of the nameplate; badge is omitted when the actor has no faction or when the camera is beyond `BadgeFadeDistance`. |
| FR-WSM3D-UI-005 | All worldspace UI elements respect the `LodSelector` culling tier: Voxel tier renders full UI; Procedural tier renders health bar only; Impostor tier renders nothing. |

---

## Architecture

### 1. WorldUIRenderer

New `MonoBehaviour` at `WorldSphereMod/Code/UI/WorldUIRenderer.cs`, added to
`Mod.Object` in `Mod.Init` (same pattern as `VoxelFrameDriver`).

Owns:
- One `NameplatePool` — a fixed-capacity pool of `WorldspaceNameplate` GameObjects.
- One `HealthBarBatcher` — submits quad instances via `MeshInstanceBatcher`.
- One `DamagePopPool` — a fixed-capacity pool of `DamagePop` structs.
- One `FactionBadgeBatcher` — submits quad instances via `MeshInstanceBatcher`.

Per-frame update order (called from `VoxelFrameDriver.LateUpdate` after
`VoxelRender.Flush` completes):

```
WorldUIRenderer.LateUpdate()
  → NameplatePool.Tick(visibleActors, cameraPos)
  → HealthBarBatcher.Flush(visibleActors)
  → DamagePopPool.Tick(deltaTime)
  → FactionBadgeBatcher.Flush(visibleActors)
```

### 2. Nameplate Prefab

`WorldSphereMod/Code/UI/WorldspaceNameplate.cs` — wraps a single
`TextMeshPro` component on a `Canvas (WorldSpace)`. Billboarding: each
`LateUpdate` sets `transform.rotation = CameraManager.MainCamera.transform.rotation`.

Distance fade: evaluates `dist = Vector3.Distance(transform.position, cameraPos)`,
then `alpha = 1 − saturate((dist − FadeStart) / (FadeEnd − FadeStart))` where
`FadeStart = NameplateFadeDistance * 0.7f` and `FadeEnd = NameplateFadeDistance`.
Written to `tmp.alpha`.

Height offset above actor head: `ActorHeadOffset + HealthBarHeight + NameplatePadding`
(all exposed in `SavedSettings`).

### 3. Health Bar Quad

No `GameObject` per actor. `HealthBarBatcher` builds a `Matrix4x4` per visible
actor each frame and calls `MeshInstanceBatcher.Submit(healthBarMesh, healthBarMat,
matrix, fillColor)`. The `HealthBarMat.shader` is a new unlit shader
(`WorldSphereMod/Resources/Shaders/HealthBar.shader`) with two properties:
`_FillFraction` (float) and `_FillColor` (Color). Both are written as per-instance
`MaterialPropertyBlock` values.

Billboarding: the matrix is constructed from `Tools.RotateToCamera` output
(same helper already used by voxel actor orientation), so it stays vertical on Y
and faces the camera horizontally.

### 4. Damage Number Pool

`DamagePop` is a struct: `{ TMP component ref, Vector3 origin, float age, float
duration }`. Pool is pre-warmed to `DamagePopPoolSize` instances at `Mod.Init`.

Triggered via a new `VoxelRender.cs` callback hook: `OnActorDamaged(actorIndex,
damageAmount)` — a `static event Action<int, int>` fired in the `EmitVoxels`
loop when `render_data.hp[i] < render_data.prevHp[i]`. `WorldUIRenderer`
subscribes to this event.

`DamagePopPool.Tick` advances each active pop's `age`, computes
`pos = origin + Vector3.up * (age / duration) * PopRiseHeight`, updates
`tmp.transform.position = pos` and `tmp.alpha = 1 − (age / duration)`. When
`age >= duration` the slot is returned to the free list.

### 5. Faction Badge Atlas

One `Texture2DArray` atlas (`WorldSphereMod/Resources/Textures/FactionBadgeAtlas.asset`)
packed from per-faction 16×16 icon sprites. Index into the array = `faction.id % atlasSlotCount`.
`FactionBadgeBatcher` passes the atlas index as a per-instance `_BadgeIndex` int in
the `MaterialPropertyBlock`; `WorldSphereMod/Resources/Shaders/FactionBadge.shader`
samples `UNITY_SAMPLE_TEX2DARRAY(_BadgeAtlas, float3(uv, _BadgeIndex))`.

Atlas packing happens once at `Mod.Init` via `FactionBadgeAtlasBuilder.Build()`
(new class, `WorldSphereMod/Code/UI/FactionBadgeAtlasBuilder.cs`), writes the
result into a cached static field. Rebuild is triggered when the faction count
changes (detected by comparing `Nations.instance.nations.Count` against a cached
value).

### 6. Distance Fade + LodSelector Integration

`LodSelector` already decides per-actor tier in `VoxelRender.EmitVoxels`. Phase 7
extends the per-actor `ActorRenderData` struct with a `UiTier` field (enum:
`Full | HealthOnly | None`) set by a new gate in `LodSelector.Classify`:

```csharp
uiTier = tier switch {
    LodTier.Voxel      => UiTier.Full,
    LodTier.Procedural => UiTier.HealthOnly,
    _                  => UiTier.None,
};
```

`WorldUIRenderer` reads `render_data.uiTier[i]` instead of computing its own
visibility, so UI elements are culled in exactly the same pass as the mesh.

`FrustumCuller` already lifts positions with `To3DTileHeight(false)` (Phase 1
fix). Nameplates and health bars use the same lifted position plus `ActorHeadOffset`,
so they are never culled when the actor mesh is visible.

---

## Sub-Tasks

### T-001 — Nameplate Prefab + Pool
- `WorldspaceNameplate.cs`: TMP component, billboard logic, distance fade.
- `NameplatePool.cs`: fixed-size pool, `Acquire()` / `Release()`, `Tick(actors, cam)`.
- Pool pre-warm: 128 slots (configurable via `SavedSettings.NameplatePoolSize`).
- Verification: 100 actors in-game, all nameplates visible close-up, faded at
  `NameplateFadeDistance`, no GC allocs in steady state (validate with Unity
  Profiler Memory tab).

### T-002 — Health Bar Batcher
- `HealthBarBatcher.cs`: per-frame matrix construction + `MeshInstanceBatcher` submission.
- `HealthBar.shader`: unlit, per-instance `_FillFraction` + `_FillColor`, clips quads at UVs > `_FillFraction`.
- Color transitions: `Color.Lerp` between `#4CAF50` (green), `#FFC107` (yellow),
  `#F44336` (red) based on fill fraction.
- Verification: spawn actor, reduce HP via console, bar visually shrinks and
  changes color correctly.

### T-003 — Damage Pop Pool
- `DamagePop` struct + `DamagePopPool.cs`.
- `VoxelRender.OnActorDamaged` event: fire when `hp[i] < prevHp[i]`; prevHp
  cached as a new `int[]` field on `VoxelRender`.
- Pop rise height: `1.5f` world units over `0.6f` s (both exposed in `SavedSettings`).
- Pool cap: `DamagePopPoolSize = 64` default; excess hits are silently dropped
  (no alloc, no throw).
- Verification: attack an actor repeatedly, numbers float up and fade, no stacking
  when pool is full.

### T-004 — Faction Badge Atlas
- `FactionBadgeAtlasBuilder.cs`: packs faction icon sprites into `Texture2DArray`.
- `FactionBadge.shader`: unlit + alpha-clip, samples `Texture2DArray` by index.
- `FactionBadgeBatcher.cs`: per-frame Submit loop, omits actors with `faction == null`.
- Atlas rebuild trigger: compare `Nations.instance.nations.Count` each frame in
  `WorldUIRenderer.LateUpdate`; rebuild only when count changes.
- Verification: spawn two kingdoms, badges show distinct icons; badge absent on
  neutral actors.

---

## Files to Touch

| File | Change |
|------|--------|
| `WorldSphereMod/Code/UI/WorldUIRenderer.cs` | New: main UI driver, owns pools and batchers, hooks into `VoxelFrameDriver.LateUpdate`. |
| `WorldSphereMod/Code/UI/WorldspaceNameplate.cs` | New: TMP billboard + distance fade per actor. |
| `WorldSphereMod/Code/UI/NameplatePool.cs` | New: fixed-capacity pool for nameplate GameObjects. |
| `WorldSphereMod/Code/UI/HealthBarBatcher.cs` | New: per-frame instanced health bar quad submission. |
| `WorldSphereMod/Code/UI/DamagePopPool.cs` | New: struct-based pool for floating damage numbers. |
| `WorldSphereMod/Code/UI/FactionBadgeAtlasBuilder.cs` | New: packs faction sprites into `Texture2DArray` at init. |
| `WorldSphereMod/Code/UI/FactionBadgeBatcher.cs` | New: per-frame instanced faction badge quad submission. |
| `WorldSphereMod/Code/Render/VoxelRender.cs` | Add `OnActorDamaged` event; add `prevHp` cache array; add `uiTier` write in `EmitVoxels`; call `WorldUIRenderer.LateUpdate` hook. |
| `WorldSphereMod/Code/LOD/LodSelector.cs` | Add `UiTier` field to classification output; set in `Classify` based on `LodTier`. |
| `WorldSphereMod/Code/SavedSettings.cs` | Add `WorldspaceUI` (default `false`), `NameplateFadeDistance` (`30f`), `BadgeFadeDistance` (`20f`), `DamagePopPoolSize` (`64`), `NameplatePoolSize` (`128`), `PopRiseHeight` (`1.5f`), `PopDuration` (`0.6f`), `ActorHeadOffset` (`1.8f`). |
| `WorldSphereMod/Resources/Shaders/HealthBar.shader` | New: unlit, per-instance `_FillFraction` + `_FillColor`. |
| `WorldSphereMod/Resources/Shaders/FactionBadge.shader` | New: unlit + alpha-clip, samples `Texture2DArray` by `_BadgeIndex`. |
| `WorldSphereMod/Resources/Textures/FactionBadgeAtlas.asset` | New: `Texture2DArray` stub (follow-on commit). |
| `WorldSphereMod/Code/Constants.cs` | Add `ActorHeadOffsetDefault`, `HealthBarHeight`, `NameplatePadding` constants. |
| `docs/smoke-test-phase7.md` | Add worldspace UI smoke tests for each FR. |

---

## Interaction with Phase 6 (Animation) and Phase 1 (Voxel)

The nameplate / health bar position must track the animated head, not the base
actor world position. Phase 7 reads `render_data.positions[i]` (already lifted
by `To3DTileHeight(false)` per the Phase 1 frustum-cull fix) and adds
`ActorHeadOffset * VoxelScaleMultiplier`. If `SkeletalAnimation` is active (Phase 6
`AnimationEnabled` flag), the head offset should additionally account for the
root Y-delta from the VAT idle-bob: add `savedSettings.AnimVATIdleBobAmplitude`
(the `A = 0.05 world units` constant from Phase 6 T-001) so nameplates don't
clip into the mesh during the bob cycle.

`MeshInstanceBatcher.Flush` must have already been called for voxel actors before
`WorldUIRenderer.LateUpdate` runs so the UI draws on top. Order is enforced by
the `VoxelFrameDriver.LateUpdate` sequence.

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| TextMeshPro not available in NML's Unity embed | Low | High | Check `Type.GetType("TMPro.TextMeshPro, Unity.TextMeshPro")` at init; fallback to `UnityEngine.UI.Text` with reduced quality |
| WorldSpace Canvas adds a draw call per actor (no batching) | Medium | Medium | Use a single shared Canvas parented at world origin; move `RectTransform` positions per actor instead of one Canvas per actor; TMP batches within one Canvas |
| `Nations.instance` null on first frame | Medium | Low | Null-check in `FactionBadgeAtlasBuilder.Build()`; defer to next frame |
| Per-instance `_BadgeIndex` on `MaterialPropertyBlock` doesn't batch across instances | Low | Medium | Use `Graphics.DrawMeshInstanced` with a `MaterialPropertyBlock` array (one entry per instance) — same pattern as `HealthBarBatcher`; verify with Frame Debugger |
| Nameplate position jitter when actor is moving | Low | Low | Consume `render_data.positions[i]` after parallel pass completes (Phase 3 concurrency note from `CLAUDE.md`); never read mid-pass |
| `VoxelScaleMultiplier` change (e.g., tuning from 8.0) misaligns head offset | Medium | Low | Multiply `ActorHeadOffset` by `savedSettings.VoxelScaleMultiplier` everywhere; add a settings-sanity log line `[WSM3D] UI head offset: {computed}` |

---

## Acceptance Criteria (Phase 7 Ship Gate)

- [ ] FR-WSM3D-UI-001 through FR-WSM3D-UI-005 pass in-game on a 256×256 map.
- [ ] `WorldspaceUI = true` default committed; fade distances tuned.
- [ ] No GC alloc per frame in steady state (Unity Profiler: 0 B/frame after warmup).
- [ ] `docs/smoke-test-phase7.md` updated with passing screenshots for all 5 FRs.
- [ ] `docs/HANDOFF.md` Phase 7 row updated from `scaffolding` to `landed`.
- [ ] No regression on Phase 1–6 smoke tests.
- [ ] `dotnet test` green (`tests/WorldSphereMod.Tests.Unit/ + Integration/`).

---

## Build Sequence (proposed commits)

1. `ui: add WorldspaceUI flag + fade/pool settings to SavedSettings; UiTier enum to LodSelector`
2. `ui: add WorldUIRenderer driver + NameplatePool + WorldspaceNameplate (TMP billboard)`
3. `ui: add HealthBar.shader + HealthBarBatcher instanced quad submission`
4. `ui: add OnActorDamaged event in VoxelRender + DamagePopPool`
5. `ui: add FactionBadge.shader + FactionBadgeAtlasBuilder + FactionBadgeBatcher`
6. `ui: wire LodSelector UiTier into WorldUIRenderer culling gate`
7. `ui: stub FactionBadgeAtlas Texture2DArray asset`
8. `ui: flip WorldspaceUI=true default; update phase table + HANDOFF + smoke tests`

---

## Key Cross-References

- `docs/phases/phase-6-animation.md` — VAT idle-bob amplitude (head offset adjustment)
- `WorldSphereMod/Code/Render/VoxelRender.cs` — `EmitVoxels` (damage event fire site + uiTier write)
- `WorldSphereMod/Code/LOD/LodSelector.cs` — `Classify` (UiTier output)
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs` — instanced submission API
- `WorldSphereMod/Code/Tools.cs` — `RotateToCamera`, `To3DTileHeight` (billboard + position)
- `WorldSphereMod/Code/3DCamera.cs` — `CameraManager.MainCamera` (billboard target)
- `CLAUDE.md` — `EmitVoxels` gate pattern, parallel render-pass concurrency caveat
