# Phase 3 — Foliage, Clouds, Decorations (revised scope)

Source: rewrite after the decompile pass at `docs/phase3-decompile-findings.md` invalidated the original `QuantumSpriteLibrary.drawTopTiles` premise. Historical design-state snapshot; use `docs/HANDOFF.md` for current defaults.

---

## 1. Scope summary

Two prongs, two PRs.

**Phase 3a — foliage as a `BuildingRules` shape selector.** Trees, bushes, rocks in WorldBox are `BuildingAsset` instances (`tree_green_1`, `palm_tree`, `corrupted_tree`, …). They flow through the already-patched `BuildingManager.precalculateRenderDataParallel` and `BuildingProcRender.EmitMeshes` Postfix. Phase 3a adds a `BuildingShape` enum and routes tree-tagged assets to `CrossedQuadMesher.Build` instead of `BuildingMeshGen.Generate`. Reuses everything from Phase 2.

**Phase 3b — surface-overlay 3D.** `TopTileType` in WorldBox covers only surface overlays: grass, savanna, biomass, snow, road, walls. The render path is Unity's `Tilemap` system via `WorldTilemap.renderTile → TilemapExtended.addToQueueToRedraw → TilemapExtended.redraw`. Patches `renderTile` to skip the 2D Tilemap pipe when `IsWorld3D && CrossedQuadFoliage` and emit a crossed-quad mesh per overlay tile. Walls are the exception — they flow through `QuantumSpriteLibrary.drawWallType` and get a separate transpile.

**Cloud refactor.** Unchanged from the original architecture: `EffectData.EmitCrossedQuad` field, `fx_cloud` updated, sprite-renderer creation guarded in `EffectPatches.SeperateSprite`. Lands inside the Phase 3a PR (small enough to bundle, distinct mechanism).

Out of scope: lit foliage (Phase 5 lighting), shadows (Phase 5), real wind animation on the GPU (deferred until the lit shader stack is up; Phase 3 ships the wind globals + vertex displacement in the placeholder unlit shader).

---

## 2. Module Layout

Five new files under `WorldSphereMod/Code/Foliage/`. Modifications to two existing files.

- **`Foliage/CrossedQuadMesher.cs`** — pure computation. `Sprite → Mesh` of two perpendicular quads + (for `Single` shape) a single ground-aligned quad. Vertex color.w = sway amplitude (0 for rocks, 1 for foliage). `TEXCOORD1.x` = normalized height-along-quad. No Unity-runtime dependencies beyond `UnityEngine.Mesh`.
- **`Foliage/CrossedQuadMeshCache.cs`** — LRU cache. Key = `assetId + ":" + shape` (the same asset can render differently under different `BuildingRules`, so shape is part of the key). Mirrors `VoxelMeshCache` shape: `_pendingDestroy` queue, `DrainPendingDestroy`, capacity 1024.
- **`Foliage/FoliageMaterial.cs`** — static helper owning the foliage `Material` handle. `EnsureMaterial()` loads `Resources/Shaders/FoliageWind` (when shipped) with a fallback chain to `Sprites/Default`. Mirrors `VoxelRender.EnsureMaterial`. Foliage uses a different material from voxel because the wind shader is distinct.
- **`Foliage/WindSwayDriver.cs`** — `MonoBehaviour` attached to `Mod.Object` in `Mod.Init`. `LateUpdate` uploads `_WindTime`, `_WindDir`, `_WindSpeed` as `Shader.SetGlobalFloat`/`SetGlobalVector` so all foliage meshes share them. Also calls `CrossedQuadMeshCache.Tick()` + `DrainPendingDestroy()`.
- **`Foliage/FoliageTileRender.cs`** — Phase 3b only. Harmony Prefix on `WorldTilemap.renderTile` and a transpile/Prefix on `QuantumSpriteLibrary.drawWallType`. Routes overlay tiles to crossed-quad mesh emit via the shared `MeshInstanceBatcher`.

Shader: `WorldSphereMod/Resources/Shaders/FoliageWind.shader` — authored in Phase 3a, baked into the AssetBundle alongside the Phase 5 lit shader work (Phase 3 ships the placeholder fallback until then).

Modifications:
- `WorldSphereMod/Code/ProcGen/BuildingRules.cs` — add `BuildingShape` enum + `Shape` field on `BuildingRules`. `BuildingRulesRegistry.Register` calls `ProcGenCache.Invalidate` AND `CrossedQuadMeshCache.Invalidate` (both caches need it; same asset can flip between shape paths).
- `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs` — emission loop branches on resolved shape.

Namespace `WorldSphereMod.Foliage` for new files; existing namespaces preserved.

---

## 3. Public Type Signatures

```csharp
namespace WorldSphereMod.ProcGen;

enum BuildingShape { Procgen, CrossedQuad, Single }

class BuildingRules
{
    string? AssetId;
    RoofStyle Roof;
    int Stories;
    float FootprintDepth;
    DoorSpec[] Doors;
    DoorSpec[] Windows;
    bool PerpendicularRoof;
    BuildingShape Shape;           // NEW; default Procgen
    float SwayAmplitude;           // NEW; foliage only, default 1.0
}
```

```csharp
namespace WorldSphereMod.Foliage;

static class CrossedQuadMesher
{
    static Mesh Build(Sprite sprite, BuildingShape shape, float swayAmplitude);
}

static class CrossedQuadMeshCache
{
    static int Capacity = 1024;
    static Mesh GetOrBuild(Sprite sprite, BuildingShape shape, BuildingRules rules);
    static void Invalidate(string assetId);
    static void Clear();
    static void Tick();
    static void DrainPendingDestroy();
    static int Count { get; }
}

static class FoliageMaterial
{
    static bool EnsureMaterial();
    static Material? Get();        // null if EnsureMaterial returned false
    static void Reset();           // world reload
}

sealed class WindSwayDriver : MonoBehaviour
{
    void LateUpdate();             // globals + cache tick + drain
}
```

---

## 4. Decision Tree (per visible building/tile)

### Phase 3a (in `BuildingProcRender.EmitMeshes`)

```
ProceduralBuildings == false && VoxelEntities == false
    → vanilla 2D billboard (existing fallback)

ProceduralBuildings == false && VoxelEntities == true
    → BuildingVoxelEmit Phase 1 fallback (existing)

ProceduralBuildings == true:
    rules = BuildingRulesRegistry.Resolve(asset.id)
    rules.Shape == Procgen
        → BuildingMeshGen.Generate → ProcGenCache → VoxelRender.Submit
    rules.Shape == CrossedQuad   (auto-detected for tree_*, bush_*, palm_*, etc)
        → CrossedQuadMesher.Build → CrossedQuadMeshCache → MeshInstanceBatcher.Submit(mesh, FoliageMaterial.Get(), trs, tint)
    rules.Shape == Single        (auto-detected for rock_*, stone_*, boulder_*)
        → CrossedQuadMesher.Build(Single) — emits one ground-aligned quad with sway=0
        → MeshInstanceBatcher.Submit(mesh, FoliageMaterial.Get(), trs, tint)
```

### Phase 3b (in `FoliageTileRender`)

```
CrossedQuadFoliage == false || !IsWorld3D
    → WorldTilemap.renderTile Prefix returns true; vanilla Tilemap pipe runs

CrossedQuadFoliage == true && IsWorld3D:
    Prefix on WorldTilemap.renderTile inspects tile.top_tile_type:
        - "wall_*" → defer to existing path; drawWallType transpile handles it separately
        - tile.top_tile_type.tag_foliage true (or asset id in a foliage allow-list)
            → emit crossed-quad mesh via MeshInstanceBatcher.Submit
            → return false to skip Tilemap.SetTiles
        - all other overlays (grass, biomass, road, fuse, …)
            → return true; let the vanilla Tilemap pipe handle it
            (grass surface overlays stay 2D until a later phase)
```

Walls separately: transpile/Prefix on `QuantumSpriteLibrary.drawWallType` substitutes the per-wall-tile `QuantumSprite` emit with a wall-mesh `MeshInstanceBatcher.Submit`. Wall mesh = extruded prism along the wall direction, height from `wallAsset.height_id` or a fixed default.

---

## 5. Wire-Up Sketches

### Phase 3a building Postfix extension

`ProcGen/BuildingProcRender.cs`, inside `EmitMeshes` loop after `BuildingRulesRegistry.Resolve(b.asset.id)`:

```csharp
BuildingRules rules = BuildingRulesRegistry.Resolve(b.asset.id);
Mesh m;
Material mat;
switch (rules.Shape)
{
    case BuildingShape.CrossedQuad:
    case BuildingShape.Single:
        if (!FoliageMaterial.EnsureMaterial()) continue;
        m = CrossedQuadMeshCache.GetOrBuild(rd.main_sprites[i], rules.Shape, rules);
        mat = FoliageMaterial.Get()!;
        break;
    default:
        m = ProcGenCache.GetOrGenerate(b.asset, rules);
        mat = VoxelRender.GetMaterial();  // new accessor returning _material
        break;
}
if (m == null) continue;
// ... TRS build, MeshInstanceBatcher.Submit(m, mat, trs, rd.colors[i])
rd.scales[i] = Vector3.zero;  // suppress sprite (BuildingRenderData has no has_normal_render)
```

`VoxelRender.GetMaterial()` is a new public accessor returning the existing `_material` so the procgen branch reuses the voxel material (lit pipeline shares between phases when Phase 5 ships).

### `BuildingRulesRegistry` auto-detection

`Resolve(string assetId)` checks the registry first; if missing, applies the heuristic:
- starts with `tree_`, `palm_`, or `bush_` → `Shape = CrossedQuad`, `SwayAmplitude = 1.0`
- starts with `rock_`, `stone_`, or `boulder_` → `Shape = Single`, `SwayAmplitude = 0`
- otherwise → `Shape = Procgen` (default)

Returns a synthesized `BuildingRules` with the heuristic-set fields; registered overrides take precedence.

### Phase 3b `WorldTilemap.renderTile` Prefix

```csharp
[HarmonyPatch(typeof(WorldTilemap), nameof(WorldTilemap.renderTile))]
public static class RenderTilePrefix
{
    public static bool Prefix(WorldTile pTile)
    {
        if (!Core.savedSettings.CrossedQuadFoliage || !Core.IsWorld3D) return true;
        var tt = pTile.top_tile_type;
        if (tt == null) return true;
        if (!IsFoliageOverlay(tt)) return true;       // grass/road/etc fall through
        EmitFoliageMesh(pTile);
        return false;  // skip vanilla Tilemap.SetTiles
    }
}
```

`IsFoliageOverlay` checks `tt.id` against a small allow-list (extensible by mods via a future `RegisterFoliageOverlay` API; not Phase 3a scope).

### Cloud refactor

In `Effects.cs`, `EffectData` gains an optional `bool EmitCrossedQuad = false` parameter. `Constants.cs` `fx_cloud` entry updated. `EffectPatches.SeperateSprite` (`Effects.cs:184`) gains a guard `if (Data.EmitCrossedQuad) return;`. `EffectManager.SetEffect3D` gains a branch: when true, submit a crossed-quad mesh via `MeshInstanceBatcher` with `ShadowCastingMode.Off` and the foliage material.

### Per-frame driver

`Mod.Init` after `AddComponent<VoxelFrameDriver>` adds `AddComponent<WindSwayDriver>`. `WindSwayDriver.LateUpdate` sets `Shader.SetGlobalFloat("_WindTime", Time.time)`, `Shader.SetGlobalVector("_WindDir", _windDir)`, `Shader.SetGlobalFloat("_WindSpeed", 1.5f)`. Then `CrossedQuadMeshCache.Tick()` and `DrainPendingDestroy()`.

`VoxelFrameDriver` already drains `VoxelMeshCache` + `ProcGenCache`; adding the foliage cache requires one more line OR splitting the drain into `WindSwayDriver`. Pick the latter — keeps the foliage subsystem self-contained.

---

## 6. Wind Shader Contract (unchanged from original architecture)

C# supplies per vertex: `POSITION`, `TEXCOORD0` (UV from sprite atlas rect), `COLOR` (RGB tint + W sway amplitude), `TEXCOORD1.x` (height-along-quad 0=base/1=top).

Per-frame globals from `WindSwayDriver`: `_WindTime` (`Time.time`), `_WindDir` (Vector2 XZ), `_WindSpeed` (float).

Shader displaces vertex XZ by `swayAmplitude * heightAlongQuad * sin(phase)` where `phase` is derived from world-space XZ (or cylindrical theta on the curved shape) plus `_WindTime`. No per-instance CPU upload beyond what `MeshInstanceBatcher` already sends.

---

## 7. API Additions

### Internal (`WorldSphereMod/Code/WorldSphereAPI.cs`)

No new method — the existing `RegisterBuildingRules(string assetId, object rulesObj)` carries the new `Shape` and `SwayAmplitude` fields automatically since they're on `BuildingRules`.

### External (`WorldSphereAPI/WorldSphereAPI.cs`)

No new delegate.

For Phase 3b's surface-overlay allow-list, defer the `RegisterFoliageOverlay` API to a separate small follow-up — keeps Phase 3 PR scope contained.

---

## 8. Build Sequence

**Phase 3a PR** (5 atomic commits):

1. `procgen: add BuildingShape enum + Shape/SwayAmplitude fields on BuildingRules`
2. `foliage: add FoliageMaterial + CrossedQuadMesher` — types only.
3. `foliage: add CrossedQuadMeshCache + WindSwayDriver MonoBehaviour` — driver attached but not yet consulted by any emit path.
4. `procgen: branch BuildingProcRender on rules.Shape; CrossedQuad path live` — heuristic auto-detection of tree/rock IDs; `BuildingRulesRegistry.Resolve` synthesizes the shape.
5. `foliage: refactor fx_cloud to EmitCrossedQuad path; flip CrossedQuadFoliage=true; update phase table + HANDOFF`.

**Phase 3b PR** (4 atomic commits, ships separately):

1. `foliage: add FoliageTileRender + RenderTilePrefix; foliage-overlay allow-list`
2. `foliage: transpile QuantumSpriteLibrary.drawWallType for wall mesh emit`
3. `foliage: tile-change invalidation via WorldTile.current_rendered_tile_graphics`
4. `foliage: ship Phase 3b — update phase table + HANDOFF`

---

## 9. Risks

1. **Auto-detect heuristic for tree/rock asset IDs.** `tree_*`/`rock_*`/`bush_*` covers vanilla WorldBox but won't catch modded foliage. Mitigation: ship the heuristic + the `RegisterBuildingRules` override path; modded foliage assets opt in via `api.RegisterBuildingRules(assetId, new BuildingRules { Shape = CrossedQuad })`. Acceptable.

2. **Tree sprite atlas readability.** Per `docs/phase1-review.md` issue #2 and the Phase 2 hardening commit, atlas textures without Read/Write enabled crash `GetPixels32`. `CrossedQuadMesher.Build` follows `SpriteVoxelizer.Build` — guard with `if (!sprite.texture.isReadable) return EmptyMesh();` matching the existing pattern.

3. **`WorldTilemap.renderTile` signature.** Decompile (`docs/phase3-decompile-findings.md`) confirms the method exists and is per-tile. Need to verify it's actually called per-frame (might be once-per-tile-change). If once-per-tile-change, the Phase 3b mesh emit needs its own per-frame redraw trigger — likely `WorldTile.current_rendered_tile_graphics` dirty-tracking is the right hook. Confirm during implementation.

4. **`drawWallType` runs per-frame.** Confirm via Unity Profiler; if it's already once-per-change like top tiles, the wall mesh emit follows the same dirty-tracking. Otherwise the transpile is straightforward.

5. **Material sharing with Phase 5 lit shader.** Foliage material is currently distinct from voxel material because the wind shader is different. When Phase 5 lit shader lands, the foliage shader gets a lit variant too. Both materials stay separate; the lit upgrade is per-shader, not unified.

6. **`Effects.cs` `EffectData` field add.** Existing constructor signature is positional; new field added as optional last parameter. Existing call sites unchanged.

---

## Files referenced

- `docs/phase3-decompile-findings.md` — the scope-correcting investigation.
- `WorldSphereMod/Code/ProcGen/BuildingRules.cs` — extended with `Shape` + `SwayAmplitude`.
- `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs` — extended with shape branch.
- `WorldSphereMod/Code/ProcGen/ProcGenCache.cs` — unchanged.
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs` — `Submit(mesh, material, trs, color)` reused.
- `WorldSphereMod/Code/Effects.cs:11-23` — `EffectData` struct extension.
- `WorldSphereMod/Code/Constants.cs:29` — `fx_cloud` entry.
- `WorldSphereMod/Code/SavedSettings.cs:31,48` — `CrossedQuadFoliage` (default false), `FoliageDensity` (1.0).
