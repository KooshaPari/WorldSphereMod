# Phase 10 — Proxy LOD tier status

**Last updated:** 2026-05-23  
**Audit source:** [`plan-vs-actual-gap-audit.md`](plan-vs-actual-gap-audit.md) gap #4 (Phase 10 Proxy LOD tier).

## Summary

| Layer | Status |
|---|---|
| `LodSelector` tier math | **Shipped** — `Voxel` / `Proxy` / `Impostor` with hysteresis |
| `FrustumCuller` + impostor + hardware fallback | **Shipped** |
| `SpriteVoxelizer.BuildProxy` + proxy mesh cache | **Deferred** — not in repo |
| Emit dispatch for `LodTier.Proxy` | **Routes to full voxel** — same path as `Voxel` |

README phase table: **code-complete (no proxy)** — selection exists; mid-tier mesh swap does not.

## Current `LodSelector` behavior

File: `WorldSphereMod/Code/LOD/LodSelector.cs`

- **Enum:** `LodTier { Voxel, Proxy, Impostor }`.
- **Thresholds (screen-projected size, not world meters):**
  - `VoxelThreshold = 0.08f` — nearest band.
  - `ProxyThreshold = 0.020f` — mid band (alpha.9 polish: 0.025 → 0.020 per ADR-0017).
- **Distance cutoffs:** recomputed when FOV, `Core.savedSettings.LODScale`, or either threshold changes:
  - `voxelMaxDist = entityHeight * lodScale / (VoxelThreshold * tanHalfFov)`
  - `proxyMaxDist = entityHeight * lodScale / (ProxyThreshold * tanHalfFov)`
  - Per-entity compare uses **squared** distance vs cached `_voxelMaxDistSqr` / `_proxyMaxDistSqr`.
- **`_entityHeight`:** `0.5f * 16.0f` — pre-multiplied for `VoxelScaleMultiplier=16` so LOD bands track rendered size without forcing `LODScale=16` manually.
- **Proposal order:** `distSqr < voxel` → `Voxel`; else `distSqr < proxy` → `Proxy`; else `Impostor`.
- **Hysteresis:** per `instanceId`, 3 consecutive frames of the same proposed tier before `current` changes (frame debounce, not distance deadband).
- **`ImpostorOnlyMode`:** when `Mod.OnLoad` detects missing compute or indirect-args support, every `Select` returns `Impostor` (compatibility fallback; instancing still required).

Deeper threshold/hysteresis notes: [`lod-system-audit.md`](lod-system-audit.md).

## Deferral — why Proxy still looks like Voxel

**Planned** (`docs/phase10-architecture.md` §3 dispatch):

- `Voxel` → `VoxelMeshCache.Get` (full greedy mesh).
- `Proxy` → `SpriteVoxelizer.BuildProxy(sprite)` (half-res downsample, depth=1, separate cache key).
- `Impostor` → `ImpostorBillboard.GetOrCreate`.

**Actual emit paths** (`VoxelRender.cs`, `BuildingProcRender.cs`):

1. `LodSelector.Select(...)` may return `LodTier.Proxy`.
2. Only **`tier == LodTier.Impostor`** gets a dedicated branch (impostor quad + atlas).
3. All other tiers — including **`Proxy`** — fall through to **`VoxelMeshCache.Get(sp, -1, true)`** (full voxel mesh).

There is **no** `BuildProxy` in `SpriteVoxelizer.cs` and **no** `LodTier.Proxy` branch in emit code. Mid-band entities pay full voxel cost; they are classified as Proxy for future wiring only.

**Skeletal path:** `tier != LodTier.Impostor` still allows `RigDriver.SubmitSkinnedActor` before the impostor/voxel split — Proxy-tier actors near camera can take the skinned path when rig type is known.

## What remains to close the gap

Per `phase10-architecture.md` build sequence step 3:

1. Add `SpriteVoxelizer.BuildProxy(Sprite)` (half-res → voxelize at depth=1).
2. Cache proxy meshes in `VoxelMeshCache` under a distinct depth/key.
3. Branch emit loops: `if (tier == LodTier.Proxy) { ... BuildProxy / cache ... }` else existing voxel path.
4. Playtest LOD pop at voxel↔proxy boundary (3-frame hysteresis may need dither later).

Until then, tuning `ProxyThreshold` changes **which entities are labeled Proxy** but not **which mesh they draw**.

## E2E invariants (source tests)

`tests/WorldSphereMod.Tests.E2E/LodPhase10InvariantsTests.cs`:

- `LodSelector` exposes three tiers and proposes `Proxy` in `Select`.
- `Proxy_tier_emit_uses_full_voxel_path_until_BuildProxy_ships` — emit code never branches on `LodTier.Proxy`; no `BuildProxy` symbol.

## Related docs

- Gap audit: [`plan-vs-actual-gap-audit.md`](plan-vs-actual-gap-audit.md)
- Architecture target: [`../../phase10-architecture.md`](../../phase10-architecture.md)
- E2E gap note (mid LOD): [`e2e-coverage-gaps.md`](e2e-coverage-gaps.md) § ranked gaps #4
