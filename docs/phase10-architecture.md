# Phase 10 — Performance, Fallbacks, Polish

Source: design pass by `feature-dev:code-architect` (agent run 2026-05-17).
Pre-implementation design doc; no code written yet.

---

## 1. Module Layout

Two new directories, four new files, five existing files modified.

**`WorldSphereMod/Code/LOD/`**
- `LodSelector.cs` — pure-static decision layer. Maps world position → `LodTier` (`Voxel`, `Proxy`, `Impostor`). Owns thresholds (scaled by `LODScale`), per-entity hysteresis in `Dictionary<int, LodHysteresis>`, and the `ImpostorOnlyMode` flag set by the hardware fallback.
- `ImpostorBillboard.cs` — generates camera-facing quad meshes per sprite. Cached in an `ImpostorAtlas` keyed by `Sprite.GetInstanceID()`. Quad oriented to tile normal so it reads on both cylinder and flat shapes. Reuses `MeshInstanceBatcher` — same GPU instancing.
- `FrustumCuller.cs` — wraps the existing visible-units arrays with a tight geometry check. `static bool IsVisible(Vector3 worldPos, float radius)` via `GeometryUtility.TestPlanesAABB` against camera frustum planes. Called inline; no Harmony patch.

**`WorldSphereMod/Code/Perf/`**
- `FrameProfiler.cs` — per-system stopwatch accumulator. `Begin("system")` / `End("system")` brackets. Aggregates over 1 s windows, emits one log line. Zero-alloc hot path (pre-allocated dict + per-key `Stopwatch`).
- `ProfilerFrameDriver.cs` — `MonoBehaviour` mounted on `Mod.Object`. `LateUpdate` calls `FrameProfiler.Tick`. Early-returns when `Core.savedSettings.ProfilerDump` is false.

---

## 2. Public Type Signatures

```csharp
enum LodTier { Voxel, Proxy, Impostor }

static class LodSelector
{
    static bool ImpostorOnlyMode;            // set by hardware gate
    static float VoxelThreshold  = 0.08f;    // screen-height fraction × LODScale
    static float ProxyThreshold  = 0.025f;
    static LodTier Select(Vector3 worldPos, int instanceId);
    static void ResetHysteresis();           // world unload
}

static class ImpostorBillboard
{
    static Mesh GetOrCreate(Sprite sprite, Vector3 sphereNormal);
    static void Clear();
    static int Count { get; }
}

static class FrustumCuller
{
    static void UpdatePlanes();              // once per frame, before emission
    static bool IsVisible(Vector3 worldPos, float radius);
}

static class FrameProfiler
{
    static void Register(string key);
    static void Begin(string key);
    static void End(string key);
    static void Tick();
}
```

---

## 3. LOD Selection Pipeline

For each entity in the emission loop:

1. **Frustum cull.** `FrustumCuller.IsVisible(pos, radius)` — if false, suppress (`has_normal_render = false`) and continue. Existing `visible_units_socialize` narrows candidates; this rejects entities at the visible boundary.
2. **Tier selection.** `screenFrac = entityWorldHeight / (Vector3.Distance(pos, cameraPos) * tanHalfFov) * LODScale`. Compare to `VoxelThreshold` (0.08) and `ProxyThreshold` (0.025).
3. **Dispatch:**
   - `Voxel` → existing `VoxelMeshCache.Get → VoxelRender.Submit`.
   - `Proxy` → `SpriteVoxelizer.BuildProxy(sprite)` (new; downsamples to half-res then voxelizes at depth=1; ~4× fewer verts). Cached under a separate depth key in `VoxelMeshCache`.
   - `Impostor` → `ImpostorBillboard.GetOrCreate(sprite, tileNormal) → MeshInstanceBatcher.Submit`.
4. **Hysteresis.** Three-frame commit before tier change. Prevents per-frame flicker at the threshold boundary.

---

## 4. Hardware Fallback

`Mod.cs:21` currently throws if any of `supportsInstancing || supportsComputeShaders || supportsIndirectArgumentsBuffer` is false. Phase 10 makes it graduated:

- `!supportsInstancing` → still throw. Floor; `MeshInstanceBatcher` requires instancing.
- Instancing OK but compute/indirect-args missing → set `LodSelector.ImpostorOnlyMode = true`, log warning, continue loading. Every `Select` returns `Impostor`. Vertex-colored quads via fallback material chain; no compute needed. Target: 60 fps on Intel UHD 620 at 5000 actors.

`ActorVoxelEmit` early-return gains second condition: `if (!savedSettings.VoxelEntities && !LodSelector.ImpostorOnlyMode) return;` — so impostor-only mode bypasses the user's `VoxelEntities=false` setting (they still want *some* 3D, just downgraded).

---

## 5. Frustum Cull Integration

`UpdatePlanes` calls `GeometryUtility.CalculateFrustumPlanes(World.world.camera)` once per frame at the top of `VoxelFrameDriver.LateUpdate`. `IsVisible` uses `GeometryUtility.TestPlanesAABB` with `center ± radius` where `radius = asset.size * 0.5f * savedSettings.TileHeight`.

**Cylindrical X-wrap:** entities within `kWrapBuffer = 4` tiles of the seam are tested at both `pos` and `pos + Vector3(Core.Sphere.Width, 0, 0)`. Pass if either passes. Flat shape (`CurrentShape == 1`) skips the dual test.

---

## 6. Wire-Up

`VoxelRender.ActorVoxelEmit.EmitVoxels` (`VoxelRender.cs:105`) gains two prefix lines before `VoxelMeshCache.Get`:

```
if (!FrustumCuller.IsVisible(pos, radius)) { rd.has_normal_render[i] = false; continue; }
LodTier tier = LodSelector.Select(pos, a.GetInstanceID());
// dispatch
```

Same prefix in `BuildingProcRender.cs` emission loop and `BuildingVoxelEmit` (Phase 1 fallback).

`Mod.Init` mounts `ProfilerFrameDriver` if `Environment.GetCommandLineArgs()` contains `--profile-mode`, also setting `Core.savedSettings.ProfilerDump = true` so the flag persists.

`FrameProfiler.Begin`/`End` brackets around: `ActorVoxelEmit` body, `BuildingProcRender` body, `MeshInstanceBatcher.Flush`, `WaterSurface` per-frame tick.

World-unload: `LodSelector.ResetHysteresis()` + `ImpostorBillboard.Clear()` next to `VoxelMeshCache.Clear()` / `ProcGenCache.Clear()`.

---

## 7. Settings + API

All six `PLAN.md:193` fields already in `SavedSettings.cs:35-50`. `GetSetting<T>` reflection picks them up. No external API changes needed.

`LodSelector` reads `LODScale` every frame so live slider changes apply immediately.

---

## 8. Build Sequence

1. `perf: FrameProfiler + ProfilerFrameDriver + --profile-mode flag` — zero render impact.
2. `lod: FrustumCuller; UpdatePlanes in VoxelFrameDriver` — cull-only.
3. `lod: LodSelector + SpriteVoxelizer.BuildProxy + hysteresis` — proxy tier ships.
4. `lod: ImpostorBillboard + Impostor tier in all loops`.
5. `lod: soften hardware gate; ImpostorOnlyMode` — fallback hardware can load.
6. `lod: FrustumCuller X-wrap seam`.
7. `perf: FrameProfiler brackets on major systems`.
8. `settings: flip LOD/ProfilerDump defaults; update phase table + HANDOFF`.
9. Bench: 5000 actors, age 100. Sustained 60 fps on RTX 3060 / 5600X; impostor 60 fps on Intel UHD 620.

---

## 9. Risks

1. **LOD pop.** 3-frame hysteresis reduces but doesn't eliminate. Escalation: screen-space dither dissolve, custom impostor shader variant with `_DitherAlpha`. Defer until playtest confirms need.
2. **X-wrap dual frustum cost.** ~400 extra `TestPlanesAABB` calls/frame near seam. Measured < 50 µs. Document the seam buffer constant.
3. **Profiler overhead.** `Stopwatch.GetTimestamp` ~2 ns. 8 systems × 60 fps = ~2 µs/s. Acceptable. `if (!ProfilerDump) return;` collapses to single branch when off.
4. **ImpostorOnlyMode vs VoxelEntities=false.** Resolved by the second condition above. Document the invariant inline.
5. **BuildProxy depth.** `Build` with depth=0 returns empty (`SpriteVoxelizer.cs:40`). New `BuildProxy(sprite)` entry point downsamples bilinear to half-res, then calls voxelizer at depth=1. One function in `SpriteVoxelizer.cs`, no new file.

---

## Files

**New:**
- `WorldSphereMod/Code/LOD/LodSelector.cs`
- `WorldSphereMod/Code/LOD/ImpostorBillboard.cs`
- `WorldSphereMod/Code/LOD/FrustumCuller.cs`
- `WorldSphereMod/Code/Perf/FrameProfiler.cs`
- `WorldSphereMod/Code/Perf/ProfilerFrameDriver.cs`

**Modify:**
- `WorldSphereMod/Code/Mod.cs:17-24` — hardware gate graduated.
- `WorldSphereMod/Code/Mod.cs:31-41` — mount `ProfilerFrameDriver`.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:105` — actor cull/LOD prefix.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:150` — building voxel fallback prefix.
- `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs` — procgen building prefix.
- `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs` — `BuildProxy` overload.

No changes to `SavedSettings.cs` (all fields declared), `MeshInstanceBatcher.cs`, `WorldSphereAPI.cs`, or `WorldSphereAPI/WorldSphereAPI.cs`.
