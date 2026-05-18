# Phase 3 — Foliage, Clouds, Decorations as Crossed-Quads

Source: design pass by `feature-dev:code-architect` (agent run 2026-05-17).
Pre-implementation design doc; no code written yet.

> **⚠ SCOPE REVISION REQUIRED — see `docs/phase3-decompile-findings.md`.**
> The decompile-investigation pass revealed that trees / bushes / rocks
> in WorldBox are NOT `TopTileType` — they are `BuildingAsset` instances
> drawn through the already-patched `BuildingManager` path. The "patch
> on `QuantumSpriteLibrary.drawTopTiles`" plan in section 3 below is
> targeting a method that **does not exist**. Top tiles in WorldBox
> only cover surface overlays (grass, biomass, walls). Phase 3 should
> be re-scoped:
>
> - **Phase 3a:** route tree-tagged `BuildingAsset`s through a new
>   `CrossedQuadMesher` by extending the existing Phase 2 procgen
>   decision tree (add a `Shape` selector to `BuildingRules`).
> - **Phase 3b:** patch `WorldTilemap.renderTile` for surface overlay
>   3D (separate Unity Tilemap path).
>
> The `CrossedQuadMesher`, mesh cache, `WindSwayDriver`, and shader
> contract sections below are reusable. Sections 3 (TopTile
> Integration), 6 (Decision Tree), and 8 (Build Sequence commit 4)
> need rewrite before implementation begins.

---

## 1. Module Layout

Three files under `WorldSphereMod/Code/Foliage/` plus one shader. Three-layer stack matches Phases 1 and 2.

- **`CrossedQuadMesher.cs`** — computation layer. `Sprite` → `Mesh` of two perpendicular unit quads sharing a UV region. Vertex color RGB = tint, W = sway amplitude (1.0 for foliage, 0.0 for rock). Knows nothing about Harmony or batcher.
- **`CrossedQuadMeshCache.cs`** — caching layer. LRU dict keyed by `(sprite.GetInstanceID(), QuadShape)` (Crossed vs Single). Mirrors `VoxelMeshCache` shape exactly — same Entry struct, `_pendingDestroy` queue, 10% eviction, capacity 1024.
- **`FoliageRender.cs`** — integration shim. `[HarmonyPatch]` declarations + lazy `_material` resolution + routing into `MeshInstanceBatcher`.
- **`WindSwayDriver.cs`** — `MonoBehaviour` attached to `Mod.Object` in `Mod.Init`. Per-frame uploads `_WindTime`, `_WindDir`, `_WindSpeed` shader globals. Also flushes `FoliageRender` and calls `CrossedQuadMeshCache.Tick`/`DrainPendingDestroy`.
- Shader: `WorldSphereMod/Resources/Shaders/FoliageWind.shader`, loaded via `Resources.Load<Shader>` at material-resolve time. Falls back to `Sprites/Default`.

Namespace `WorldSphereMod.Foliage`. File names match type names 1:1. No static state except cache + material handle.

---

## 2. Public Type Signatures

### `FoliageRules.cs`

```csharp
namespace WorldSphereMod.Foliage;

enum QuadShape { Crossed, Single }   // Crossed=foliage, Single=rock/decoration

[Serializable]
struct FoliageRules
{
    string?   AssetId;
    QuadShape Shape;            // default Crossed
    float     SwayAmplitude;    // 0..1
    float     HeightScale;      // 0 = use sprite aspect
    bool      CastShadow;       // default false; Phase 5 revisits
    static FoliageRules Default;
}

static class FoliageRulesRegistry
{
    static void Register(string assetId, FoliageRules rules);
    static FoliageRules Resolve(string assetId);   // heuristic default if not registered
    static void Invalidate(string assetId);
}
```

### `CrossedQuadMesher.cs`

```csharp
static class CrossedQuadMesher
{
    static Mesh Build(Sprite sprite, QuadShape shape, float swayAmplitude, float heightScale);
}
```

8 verts, 12 tris. UVs from sprite atlas rect. Color.w = sway. `TEXCOORD1.x` = normalized height-along-quad (0 at base, 1 at top — pins base to ground in shader).

### `CrossedQuadMeshCache.cs`

```csharp
static class CrossedQuadMeshCache
{
    static int Capacity = 1024;
    static Mesh GetOrBuild(Sprite sprite, FoliageRules rules);
    static void Invalidate(int spriteInstanceId);
    static void Clear();
    static void Tick();
    static void DrainPendingDestroy();
    static int Count { get; }
}
```

### `WindSwayDriver.cs`

```csharp
sealed class WindSwayDriver : MonoBehaviour
{
    void LateUpdate();   // upload shader globals + FoliageRender.Flush() + cache Tick/Drain
}
```

---

## 3. TopTile Integration

Target method: `QuantumSpriteLibrary.drawTopTiles` (name inferred from sibling naming pattern `drawBuildings`/`drawFires`/`drawUnitsAvatars` enumerated in `Core.cs:186-192`; **must be confirmed via ILSpy** — see Risk 1).

Patch lives in `FoliageRender.cs` as a `[HarmonyPrefix]`. When `CrossedQuadFoliage == true && IsWorld3D`, Prefix returns `false` (full replacement, same pattern as `SourcePatches.calculatebuildindata3D`).

Inside the replacement:
1. Iterate visible top-tile list.
2. Resolve `FoliageRules` per asset id.
3. Cache lookup via `CrossedQuadMeshCache.GetOrBuild`.
4. Submit `(mesh, _material, trs, tileColor)` to `MeshInstanceBatcher`.
5. Matrix from `Tools.To3DTileHeight(tilePos)` + identity rotation (foliage stands upright, no camera-facing).
6. Density cull: skip tile when `(tile.x * 2654435761u ^ tile.y * 2246822519u) % 1000 >= FoliageDensity * 1000` — deterministic hash, same tiles always drawn (no flicker).

Flush piggybacks on `WindSwayDriver.LateUpdate`. No separate driver.

Sprite suppression: not needed. Prefix returning false replaces the entire vanilla draw call. Top tiles aren't actor `render_data`, so no `has_normal_render`-style flag to clear.

---

## 4. Cloud Refactor

`Constants.cs:29` `fx_cloud` currently: `new EffectData(false, true, 21, false)` — `SeperateSprite=true`, `ExtraHeight=21`, `OnGround=false`.

Phase 3 adds one optional field to `EffectData`: `bool EmitCrossedQuad`. `fx_cloud` becomes `new EffectData(false, true, 21, false, emitCrossedQuad: true)`. All other callers unaffected — backward compatible.

In `Effects.cs`:
- `EffectManager.SetEffect3D` branches on `Data.EmitCrossedQuad`. When true: submit a crossed-quad mesh via `FoliageRender.SubmitCloud(effect, cloudHeight)`, deactivate the `SpriteRenderer`.
- `EffectPatches.SeperateSprite` (Effects.cs:184) Postfix gains a guard: `if (Data.EmitCrossedQuad) return;` — skip the redundant sprite object creation.
- `UpdateEffect` / `UpdateSeperatedSprite` gain same guards; cloud per-frame update calls `FoliageRender.UpdateCloud` (drifts world position by a slow velocity seeded from effect instance id).

Shadow casting: clouds use a separate `Material` instance with `ShadowCastingMode.Off` passed to `MeshInstanceBatcher.Flush` (already supports per-flush shadow mode at line 66).

---

## 5. Wind Shader Contract

C# supplies per vertex:

| Attribute | Meaning |
|---|---|
| `POSITION` | Object-space, from `CrossedQuadMesher`. |
| `TEXCOORD0` (UV) | Atlas sub-rect mapped by mesher. |
| `COLOR` (RGBA) | RGB = tint (from `MeshInstanceBatcher` `_InstanceColor`); W = sway amplitude (baked per asset). |
| `TEXCOORD1.x` | Normalized height-along-quad (0 base, 1 top). |

Per-frame globals (uploaded by `WindSwayDriver`):
- `_WindTime` (float): `Time.time * _WindSpeed`.
- `_WindDir` (Vector2): world-space XZ unit vector, slowly rotating.
- `_WindSpeed` (float): TBD from `SavedSettings.WindSpeed` (future) or constant.

**No per-instance CPU upload.** Shader derives spatial phase from `unity_ObjectToWorld._m03/_m23` (world XZ) combined with `_WindTime`. Eliminates per-instance upload beyond what `MeshInstanceBatcher` already sends.

---

## 6. Decision Tree Per Top Tile

```
CrossedQuadFoliage == false
    → vanilla drawTopTiles (Prefix returns true)

CrossedQuadFoliage == true AND IsWorld3D:

    rules = FoliageRulesRegistry.Resolve(tile.top_tile_type.id)

    rules.Shape == Crossed (default)
        → mesh = CrossedQuadMeshCache.GetOrBuild(sprite, rules)
        → MeshInstanceBatcher.Submit; sway = rules.SwayAmplitude (default 1.0)

    rules.Shape == Single (rocks/boulders/decorations)
        → same path, sway = 0 (shader displacement = 0; no CPU branch)

    Density cull (hash >= FoliageDensity)
        → skip entirely; vanilla sprite also suppressed
```

Heuristic: asset id contains "rock"/"boulder"/"stone" → `Single` + sway 0. Else `Crossed` + sway 1.0. Overridable via `RegisterFoliageRules`.

---

## 7. API Additions

### Internal (`WorldSphereMod/Code/WorldSphereAPI.cs`)

```csharp
public static void RegisterFoliageRules(string assetId, object rulesObj);
public static event Action<Camera>? OnFoliageCameraUpdate;
```

`OnFoliageCameraUpdate` fires before `WindSwayDriver` uploads globals. Lets external mods inject custom shader globals. Only fire if subscribers exist.

### External (`WorldSphereAPI/WorldSphereAPI.cs`)

```csharp
delegate void RegisterFoliageRules(string assetId, object rules);
// Binding via MethodInfo + Delegate.CreateDelegate — same pattern as RegisterBuildingRules.
```

No `EditFoliage` hook. `FoliageRules.SwayAmplitude` / `Shape` / `HeightScale` cover per-asset behavior.

---

## 8. Build Sequence (one PR, atomic commits)

1. `foliage: add FoliageRules + FoliageRulesRegistry` — types + heuristic defaults.
2. `foliage: add CrossedQuadMesher + CrossedQuadMeshCache` — mesh build + LRU cache; placeholder shader.
3. `foliage: add WindSwayDriver MonoBehaviour` — globals upload + flush + cache tick.
4. `foliage: Postfix on QuantumSpriteLibrary.drawTopTiles` — full replacement, density cull, batcher submit.
5. `foliage: refactor fx_cloud to EmitCrossedQuad path` — `EffectData` field + `Constants.cs` entry + guards in `EffectPatches`.
6. `foliage: expose RegisterFoliageRules; flip CrossedQuadFoliage=true; docs` — internal + external API; default flip; phase table.

---

## 9. Risks Requiring Decision

1. **Top-tile method name.** Decompile WorldBox DLL with ILSpy before commit 4 to confirm `QuantumSpriteLibrary.drawTopTiles` exists. If not, find the actual draw entry point.
2. **Per-tile vs batched.** If WorldBox draws top tiles per-tile in a tight loop (rather than one batch method), Prefix-replacement strategy fails. Alternative: per-call Postfix on `drawQuantumSprite` with caller asset-id detection + per-frame submission set to dedupe.
3. **Sway phase: shader-derived vs baked.** Blueprint picks shader-derived (world XZ hash). Alternative: bake random `float` per quad into `TEXCOORD1.y` at mesh build for better visual variety. Recommend bake; confirm before commit 2.
4. **Perf at 5k trees (≤ 3 ms target).** GPU side is fine (<100 draw calls). CPU side = 5k matrix builds + cache lookups per frame. Profile in commit 4. If CPU > 1 ms, cache matrices per unchanged tile via dirty tracking.
5. **`EffectData` struct field add.** Field is optional; existing 4-arg ctor unchanged; `ConcurrentDictionary` entries carry `EmitCrossedQuad = false` by default. No migration.

---

## Files to Create or Modify

**New:**
- `WorldSphereMod/Code/Foliage/FoliageRules.cs`
- `WorldSphereMod/Code/Foliage/CrossedQuadMesher.cs`
- `WorldSphereMod/Code/Foliage/CrossedQuadMeshCache.cs`
- `WorldSphereMod/Code/Foliage/WindSwayDriver.cs`
- `WorldSphereMod/Code/Foliage/FoliageRender.cs`
- `WorldSphereMod/Resources/Shaders/FoliageWind.shader` (placeholder; real vertex-displacement lands here)

**Modify:**
- `WorldSphereMod/Code/Effects.cs` — `EmitCrossedQuad` field; cloud branch; guard in `EffectPatches`.
- `WorldSphereMod/Code/Constants.cs` — `fx_cloud` entry.
- `WorldSphereMod/Code/Mod.cs` — `AddComponent<WindSwayDriver>` in `Init`.
- `WorldSphereMod/Code/WorldSphereAPI.cs` — `RegisterFoliageRules` + `OnFoliageCameraUpdate`.
- `WorldSphereAPI/WorldSphereAPI.cs` — `RegisterFoliageRules` delegate + binding.

No changes to `SavedSettings.cs` (flags already present), `MeshInstanceBatcher.cs`, `VoxelMeshCache.cs`, or `ProcGenCache.cs`.

---

## Key file references

- `WorldSphereMod/Code/Effects.cs:11-23` — `EffectData` struct (add `EmitCrossedQuad` here).
- `WorldSphereMod/Code/Constants.cs:29` — `fx_cloud` entry to update.
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:53` — `Submit` signature reused.
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:66` — `Flush` accepts per-call `ShadowCastingMode`.
- `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs:96-107` — `Evict` pattern mirrored.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:93-131` — Phase 1 Postfix pattern (Phase 3 uses Prefix-replace instead).
- `WorldSphereMod/Code/QuantumSprites.cs:406-424` — `MainQuantumSpritePatch` structural model.
- `WorldSphereMod/Code/Core.cs:186-192` — list of patched `QuantumSpriteLibrary` methods (confirms `drawTopTiles` not yet touched).
- `WorldSphereMod/Code/WorldSphereAPI.cs:70-79` — `RegisterCustomMesh` pattern.
- `WorldSphereMod/Code/SavedSettings.cs:31,48` — `CrossedQuadFoliage` (default false) + `FoliageDensity`.
