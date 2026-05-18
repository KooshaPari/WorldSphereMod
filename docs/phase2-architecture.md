# Phase 2 — Procedural Building Meshes: Architecture Blueprint

Source: design pass by `feature-dev:code-architect` (agent run 2026-05-17).
This is a pre-implementation design doc. No code is written yet.

---

## 1. Module Layout

The four new files under `WorldSphereMod/Code/ProcGen/` form a clean three-layer stack with one integration shim.

- **`BuildingRules.cs`** — data layer. Owns the `BuildingRules` record and the JSON loader that merges file-based overrides (from a `Locales/`-style folder) with the in-memory registry populated via the public API.
- **`BuildingMeshGen.cs`** — computation layer. Owns all heuristic logic. Takes a `BuildingAsset` plus the resolved `BuildingRules` and emits a `Mesh`. Calls `Tools.PixelsFromSpriteAtlas` for raw pixel data; writes nothing to disk.
- **`ProcGenCache.cs`** — caching layer. `Dictionary<string, Mesh>` keyed by `BuildingAsset.id + ":" + spriteVersion`, with an LRU eviction policy mirroring `VoxelMeshCache`.
- **`BuildingProcRender.cs`** (under `ProcGen/`, not `Voxel/`) — integration shim. Contains the Harmony Postfix on `BuildingManager.precalculateRenderDataParallel` and routes through `MeshInstanceBatcher` (the Phase 1 batcher, shared).

`BuildingMeshGen`, `BuildingRules`, and `ProcGenCache` know nothing about Harmony or Unity's render pipeline. The wiring class is the only thing that touches `MeshInstanceBatcher` and `Core.savedSettings`.

---

## 2. Public Type Signatures

### `BuildingRules.cs`

```csharp
namespace WorldSphereMod.ProcGen;

enum RoofStyle { Inferred, Flat, Gable, Hipped }

[Serializable]
struct DoorSpec { int x, y, w, h; }   // pixel coords in sprite space

class BuildingRules
{
    string AssetId;                   // null = unset, use heuristics
    RoofStyle Roof;                   // Inferred = heuristic picks; others authoritative
    int Stories;                      // 0 = heuristic; >=1 = override
    float FootprintDepth;             // 0 = heuristic; >0 = Z extrusion in world units
    DoorSpec[] Doors;                 // empty = heuristic
    DoorSpec[] Windows;               // empty = heuristic
    bool PerpendicularRoof;           // swap gable ridge axis
    static BuildingRules Default;     // all-zero / Inferred sentinel
}

static class BuildingRulesLoader
{
    static Dictionary<string, BuildingRules> LoadFromDirectory(string path);
    static void MergeInto(Dictionary<string, BuildingRules> registry, BuildingRules r);
}
```

### `BuildingMeshGen.cs`

```csharp
namespace WorldSphereMod.ProcGen;

static class BuildingMeshGen
{
    // Primary entry. Returns null only on irrecoverable pixel-read failure.
    static Mesh Generate(BuildingAsset asset, BuildingRules rules);

    // Sub-steps (internal, InternalsVisibleTo for tests):
    static Color32[] SampleSprite(BuildingAsset asset);
    static Rect DetectFootprint(Color32[] pixels, int w, int h);
    static RoofStyle InferRoofStyle(Color32[] pixels, int w, int h, BuildingRules rules);
    static int InferStories(Color32[] pixels, int h, BuildingRules rules);
    static List<DoorSpec> InferOpenings(Color32[] pixels, int w, int h, BuildingRules rules);
    static Mesh BuildWallMesh(Rect footprint, int stories, List<DoorSpec> openings, float storyHeight);
    static Mesh BuildRoofMesh(Rect footprint, RoofStyle style, Color32 roofColor);
    static Mesh Combine(Mesh walls, Mesh roof);
}
```

### `ProcGenCache.cs`

```csharp
namespace WorldSphereMod.ProcGen;

static class ProcGenCache
{
    static int Capacity = 512;
    static Mesh GetOrGenerate(BuildingAsset asset, BuildingRules rules);
    static void Invalidate(string assetId);   // called by RegisterBuildingRules
    static void Clear();                       // world unload
    static int Count { get; }

    // Internal key: asset.id + ":" + asset.main_sprite?.GetInstanceID()
}
```

---

## 3. Sprite-to-Mesh Heuristic Pipeline

H = heuristic-with-knobs (defaults run, `BuildingRules` fields override).
D = data-driven (authored value used verbatim).

1. **Pixel extraction (D).** `Tools.PixelsFromSpriteAtlas(asset.main_sprite)` → `Color32[]` + width/height. On null return: emit a unit cube fallback and cache it.
2. **Footprint detection (H, knob: `FootprintDepth`).** Alpha-mask bounding box. Trim fully-transparent edge columns. If `rules.FootprintDepth > 0`, override Z; else Z = X (square assumption).
3. **Story count inference (H, knob: `Stories`).** If `rules.Stories >= 1`, accept verbatim. Else: scan rows bottom-to-top for horizontal bands of near-uniform hue (ΔH < 15 HSV) separated by darker mortar lines (luminance dip > 0.1). Clamp [1, 4]. Wall height = `TileHeight * stories * BuildingSize`.
4. **Roof palette clustering (H, knob: `Roof`).** If overridden, accept. Else: top 20% rows → hue histogram. Dominant warm cluster (0-40° or 340-360° HSV, sat > 0.3) labels "roof". ≥60% column coverage with continuous top segment → `Gable`. Center-tapered → `Hipped`. Else `Flat`. Dominant roof color becomes roof vertex color.
5. **Opening detection (H, knobs: `Doors`/`Windows`).** If overridden, use verbatim. Else: scan lower 40% for dark rectangles (luminance < 0.25 vs surrounding). Tall-narrow = door. Wide-short = window. Store as `DoorSpec`.
6. **Wall mesh.** Extrude footprint to closed prism. 4 side quads. For each opening, subdivide its face and inset the opening at 5% depth. Vertex colors from mid-band wall color. No UVs (matches `VoxelLit.shader` contract).
7. **Roof mesh.** Flat: cap quad. Gable: two angled quads at ridge along long axis (or short if `PerpendicularRoof`); ridge height = short-axis / 2. Hipped: four converging quads; ridge length = long-axis × 0.4.
8. **Combine + cache.** `Mesh.CombineMeshes(walls, roof)`. Single draw call per instance. Destroy prior mesh on key collision (GPU memory hygiene).

---

## 4. Wire-Up

The existing `calculatebuildindata3D` is a **Prefix** returning `false` (fully replaces vanilla when `IsWorld3D`). Phase 2 adds a **Postfix** on the same method.

Decision tree per instance:

```
ProceduralBuildings == true  AND  asset.id not in PerpBuildings
    → ProcGenCache.GetOrGenerate → MeshInstanceBatcher.Submit
      → suppress sprite render (mechanism TBD — see Risk 2)

ProceduralBuildings == false  AND  VoxelEntities == true  AND  not PerpBuildings
    → Phase 1 fallback: VoxelMeshCache.Get(main_sprite) → MeshInstanceBatcher.Submit

ProceduralBuildings == false  AND  VoxelEntities == false
    → vanilla 2D billboard path (render_data unchanged)

asset.id in PerpBuildings  (any setting)
    → always billboard (top-down floor-plan orientation)
```

Postfix lives in `ProcGen/BuildingProcRender.cs` (independently revertable from Phase 1). Flush piggybacks on `VoxelFrameDriver.LateUpdate` — no second driver.

Thread-safety: Postfix runs after `Parallel.For` exits → single-threaded access to `ProcGenCache` is safe → no lock needed.

---

## 5. API Additions

### Internal (`WorldSphereMod/Code/WorldSphereAPI.cs`, class `WorldSphereModAPI`)

```csharp
internal static readonly ConcurrentDictionary<string, BuildingRules> BuildingRulesRegistry
    = new ConcurrentDictionary<string, BuildingRules>();

public static void RegisterBuildingRules(string assetId, object rulesObj)
```

Contract: cast `rulesObj` to `BuildingRules`, write to registry, call `ProcGenCache.Invalidate(assetId)`.

### External (`WorldSphereAPI/WorldSphereAPI.cs`)

```csharp
delegate void RegisterBuildingRules(string assetId, object rules);

class WorldSphereAPI
{
    RegisterBuildingRules? registerBuildingRules;

    public void RegisterBuildingRules(string assetId, object rules)
        => registerBuildingRules?.Invoke(assetId, rules);
}

// In WorldSphereAPI(Type) ctor:
MethodInfo? regBuilding = WorldSpherePort.GetMethod(
    "RegisterBuildingRules", BindingFlags.Static | BindingFlags.Public);
if (regBuilding != null)
    registerBuildingRules = (RegisterBuildingRules)Delegate.CreateDelegate(
        typeof(RegisterBuildingRules), regBuilding);
```

`BuildingRules` lives in `WorldSphereMod3D.dll` (has Unity dependencies for `Color32` etc — actually it doesn't, but the assembly does); external callers receive it as `object` and must either copy the struct locally or use JSON. Consider Risk 4 (JSON overload).

---

## 6. Build Sequence (one PR, atomic commits)

1. `procgen: add BuildingRules data types and JSON loader` — types only.
2. `procgen: add ProcGenCache with stub generator` — stub returns 1×1 quad.
3. `procgen: implement footprint + wall mesh generation` — pipeline steps 1-3, 5-6. Flat cap for now.
4. `procgen: add roof inference (gable/hipped/flat)` — pipeline steps 4 + 7.
5. `procgen: wire BuildingProcRender Postfix into render pipeline` — decision tree + `ProcGenCache.Clear()` on unload.
6. `procgen: expose RegisterBuildingRules on internal + external API` — both files; `BuildingRulesLoader` scan in `Core.Init`.
7. `procgen: enable ProceduralBuildings by default, update docs` — flip default; README phase table; HANDOFF update.
8. Open PR → before/after screenshots × all vanilla building assets, bench at 1k buildings (≤ 5 ms target), `ProcGenCache` hit-rate via `ProfilerDump`.

---

## 7. Risks Requiring Decision

1. **File placement for the Postfix.** Architect picked `ProcGen/BuildingProcRender.cs` (cleaner separation; ProcGen depends on Voxel's batcher). Alternative: keep it in `Voxel/` like Phase 1. **Recommendation:** ProcGen/ as designed.

2. **`render_data.has_normal_render` on BuildingManager.** Phase 1's actor Postfix reads `rd.has_normal_render[i]` to suppress the sprite quad. Unknown whether `BuildingManager.render_data` has the same flag. **Resolution:** in-flight via agent #2 decompile.

3. **`ProceduralBuildings` already defaults to `true` in `SavedSettings.cs:29`.** Same issue for `CrossedQuadFoliage`, `MeshWater`, `HighShadows`, `SkeletalAnimation`, `WorldspaceUI`, `DayNightCycle` — all currently `true` despite none being implemented. Violates the CLAUDE.md "default OFF until validated" rule. **Action:** small Phase 0 nit-fix commit flipping all unimplemented flags to `false`; each phase flips its own when shipping.

4. **External `BuildingRules` ergonomics.** Boxed `object` is awkward. Consider also exposing `RegisterBuildingRules(string assetId, string rulesJson)` so external mod authors don't need to copy the struct or use reflection. **Recommendation:** ship both signatures.

5. **`Tools.PixelsFromSpriteAtlas` thread-safety.** Not documented as thread-safe. Phase 2 runs on main thread → safe today. Note the constraint inline if a future parallel pre-gen pass is added.

6. **Sprite version cache key.** `asset.main_sprite?.GetInstanceID()` assumes WorldBox doesn't hot-reload sprites mid-session. Likely true; confirm before relying on it.

---

## Files referenced

- `docs/PLAN.md:78-92` (Phase 2 scope), `docs/PLAN.md:200-211` (API surface)
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs` — `Submit`/`Flush` API (reused)
- `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs` — LRU pattern (mirrored)
- `WorldSphereMod/Code/Voxel/VoxelRender.cs` — Postfix + flush pattern
- `WorldSphereMod/Code/QuantumSprites.cs:607-673` — existing building Prefix
- `WorldSphereMod/Code/SavedSettings.cs:29` — `ProceduralBuildings` flag (currently default-true; see Risk 3)
- `WorldSphereMod/Code/Constants.cs:32` — `PerpBuildings` dictionary
- `WorldSphereMod/Code/WorldSphereAPI.cs` — internal API extension point
- `WorldSphereAPI/WorldSphereAPI.cs` — external delegate + binding pattern
