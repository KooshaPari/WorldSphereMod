# GPU Terrain Port Boundary Blueprint — Issue #199

> Status: design, 2026-05-31. Robust long-term path for the GPU-compute go-live:
> clean subsystem separation behind explicit ports (hexagonal) — `GpuSphereManager`
> owns instanced actor/voxel rendering; the CPU `HeightFieldRenderer` stays the
> standalone terrain surface. NOT a unified-manager swap (that won't compile).

## Patterns & Conventions Found

**Architecture**
- `CompoundSpheres` (fork/submodule, `E:/wsm3d-sota-worktree/CompoundSpheres/`) is the rendering library. The main mod at `E:/Dev/WorldSphereMod/WorldSphereMod/Code/` calls into it via `using CompoundSpheres`.
- Hexagonal port pattern is already partially present: `ILegacyManagerApi` (`Compat/ILegacyManagerApi.cs:29`) defines a seam, `LegacyManagerShim` (`Compat/LegacyManagerShim.cs:34`) implements the old surface over `GpuSphereManager`.
- Settings live in `SavedSettings` / JSON; phase gates live on `[Phase(nameof(SavedSettings.*))]`.
- All rendering is DX11 via `Graphics.DrawMesh` / `Graphics.RenderMeshIndirect` / `DrawMeshInstancedIndirect`.

---

## Question 1 — Every `SphereManager` Usage in Core.cs, Classified

| Line | Code | Renders | Category |
|------|------|---------|----------|
| 581 | `static SphereManager Manager;` | Declaration of the single CPU instance | — |
| 590 | `static SphereManagerSettings SphereManagerConfig;` | Settings struct | — |
| 596–597 | `delegate Vector3 To2D(SphereManager …)` / `delegate Vector2 To2DFast(…)` | Shape geometry helpers (shape-projection math: cylindrical/flat/cube) — INPUT to the SphereManager, not a rendering category | (e) shape math |
| 635 | `SphereManager.Creator.CreateSphereManagerAsync(width, height, SphereManagerConfig, onCreated: mgr => { Manager = mgr; ConfigureHeightField(mgr, …); … })` | Creates the single manager and in the callback: (1) stores the CPU manager, (2) calls `ConfigureHeightField` which wires the `HeightFieldRenderer` for terrain, (3) triggers `FinishBecome3D` which activates all other subsystems | (a) terrain surface via HeightField; (b/c/d) actors/buildings/foliage/liquids all use the same manager's tile grid as a coordinate reference |
| 907 | `Manager.HasDirtyHeights` | Reads dirty-scale queue to gate HeightField rebuild — strictly terrain path | (a) terrain |
| 910 | `Manager.RefreshScales()` | Flushes scale (height) dirty queue to GPU buffer — tile elevation, terrain | (a) terrain |
| 914 | `Manager.RefreshTextures()` | Flushes texture index buffer — used by the instanced tile draw only (bypassed when HeightField is active) | (b/c) actors/terrain textures |
| 918 | `Manager.RefreshCustom("AddedColors")` | Flushes the "AddedColors" custom buffer (flash/overlay tints applied to instanced tiles) — used by the instanced sprite path, NOT HeightField | (b) actor overlay colors |
| 925 | `Manager.UseHeightFieldTerrain` | Gate: when true HeightField path is live | (a) terrain |
| 932 | `Manager.HeightField.MarkDirty()` | Triggers terrain mesh rebuild | (a) terrain |
| 958 | `Manager.RefreshColors()` | Flushes base color buffer — instanced tile draw | (a/b) terrain tiles in instanced mode, also actors |
| 962 | `Manager.UpdateCustom("AddedColors", …)` | Per-tile flash update | (b) overlay |
| 966 | `Manager.UpdateColor(…)` | Per-tile base color dirty mark | (a/b) |
| 975–979 | `Manager.Destroy()` | Teardown | — |
| 1035 | `typeof(SphereManager).GetField("SphereTiles", …)` | Diagnostic reflection to count textured tiles | (e) diagnostics only |
| 1079 | `Manager.DrawTiles(CameraX)` | THE draw call. When `UseHeightFieldTerrain=true`, `SphereManager.DrawTiles` (line 247–255 in `SphereManager.cs`) branches entirely into `HeightFieldRenderer.RebuildAndDraw` and returns without touching instanced tiles. When false, it runs the per-row instanced `DrawMeshIndirect` path | (a) terrain when HF=true; (a/b/c/d) in legacy instanced mode |
| 1090 | `mgr.UseHeightFieldTerrain = enabled` | Sets terrain mode | (a) terrain |
| 1093 | `mgr.HeightField` (property, lazy-created) | Terrain surface renderer | (a) terrain |
| 1848 | `SphereManagerConfig = new SphereManagerSettings(…)` | Wires shape delegates into the manager constructor | (e) config |

**Summary:** With `UseHeightFieldTerrain=true` (default ON, `SavedSettings.cs:120`), `Manager.DrawTiles` renders ONLY the HeightField terrain surface and returns early. VoxelRender (actors/bodies/voxels), BuildingProcRender (buildings), FoliageTileRender (foliage), and the liquid sub-meshes inside HeightFieldRenderer all run INDEPENDENTLY — they do not call `Manager.DrawTiles` and do not depend on the SphereManager draw path.

---

## Question 2 — Relationship Map

**HeightFieldRenderer** (`CompoundSpheres/HeightFieldRenderer.cs`)
- Owned by `SphereManager` via `SphereManager.HeightField` property (line 104, `SphereManager.cs`). Constructor takes a `SphereManager` (`HeightFieldRenderer(SphereManager manager)`, line 201). Uses `_manager.Rows`, `_manager.Cols`, `_manager.Material` — a property dependency, not a rendering dependency.
- Draws via `Graphics.DrawMesh(_mesh, …)` directly. Does NOT use `DrawMeshIndirect`/instancing.
- Contains its own water and liquid sub-mesh draw calls (lava/swamp/acid), all via `Graphics.DrawMesh`.
- Conclusion: HeightFieldRenderer is the terrain path. It calls nothing on VoxelRender, BuildingProcRender, or FoliageTileRender.

**VoxelRender** (`Code/Voxel/VoxelRender.cs`) — completely independent of `SphereManager`. Uses `MeshInstanceBatcher` + `VoxelMeshCache`. Renders actor bodies (b), building voxels (c), projectile voxels (e).

**BuildingProcRender** (`Code/ProcGen/BuildingProcRender.cs`) — routes entirely through `VoxelRender.Submit(...)`. No SphereManager dependency. Renders buildings (c).

**FoliageTileRender** (`Code/Foliage/FoliageTileRender.cs`) — routes through `MeshInstanceBatcher`. No SphereManager dependency. Renders foliage/grass/road overlays (c).

**Liquids/overlays** — liquids live inside `HeightFieldRenderer` as sub-meshes (independent `Graphics.DrawMesh`). Old 2D billboard water/lava retired (`Core.cs:1205`, `ProceduralSky.cs:423`). Flash overlay ("AddedColors") is a structured buffer on `SphereManager` used by the instanced tile draw; with HeightField enabled the tile quads are not drawn, so AddedColors are NOT rendered when HeightField is live — a latent bug worth noting.

**LOD selector** (`Code/LOD/FrustumCuller.cs`) — `FrustumCuller` exists in both the submodule (`CompoundSpheres/FrustumCuller.cs`) and in `GpuSphereManager`. Used by `SphereManager.DrawTiles` (instanced path) and `GpuSphereManager.DrawTiles`.

---

## Question 3 — GPU Manager Public Surface

**`GpuSphereManager`** (`CompoundSpheres/Gpu/GpuSphereManager.cs`)
- Extends `ManagerBase<GpuSphereTile>` → `ManagerRoot : MonoBehaviour`.
- Constructor: **synchronous only** — `Creator.CreateSphereManager(rows, cols, GpuSphereManagerSettings, Name)` (line 285). No async equivalent yet (gap vs CPU manager).
- Draw: `DrawTiles(int cameraX)` with per-row frustum culling + column-range clipping; `DrawAllTiles()` without cull.
- Shapes: cylindrical (default), flat, cube via `ConfigureShape(TileShape)` / `ConfigureCube(GpuCubeRegion[], float)`. All three in the compute kernel via `GpuShapeMath`.
- Color: `SetInputColor(int I, Color32)` → `InputColors` buffer → `OutputColors` kernel.
- Heights: `SetHeight(int I, float)` / `SetHeights(Func<int,float>)` → `InputHeights` → `OutputMatrices` kernel. `HasHeights` gates elevation.
- Custom buffers: inherited from `ManagerBase` via `AddCustomBuffer(IBufferData)` / `UpdateCustom(string, int)` / `RefreshCustom(string)`.
- Texture: `UpdateTexture(int X, int Y)` / `RefreshTextures()` → `Textures` buffer.
- Refresh: `RefreshScales()`, `RefreshColors()`, `RefreshMatrixes()`, `RefreshAll()`.
- Missing vs CPU: no `HasDirtyHeights`/`HasDirtyTiles`, no incremental bool `Refresh*(maxPerFrame)` (LegacyManagerShim wraps + always returns `true`), no `UseHeightFieldTerrain`, no `HeightField`.
- `GpuSphereManagerSettings` requires: `GetSphereTileTexture`, `Texture2DArray`, `GetGpuCameraRange`, `GpuInitiation`; plus inherited `SphereTileMesh`, `SphereTileMaterial`, `ComputeShader` (`GpuKernels.Matrix/Color`), `GetSphereTileScale`, `GetDisplayMode`.
- `GpuKernels.Matrix = "CSMatrices"`, `GpuKernels.Color = "CSColors"`.
- `GpuCubeRegion` — per-face geometry struct, uploaded via `ComputeBuffer` to the cube kernel.

**`LegacyManagerShim`** (`CompoundSpheres/Compat/LegacyManagerShim.cs`)
- Wraps a `GpuSphereManager`, exposes `ILegacyManagerApi` (old delegate-style surface). Bridges old color delegate → `InputColors`; old height sampler → `InputHeights`; old Refresh* bool → GPU dispatch + always returns true.
- Does NOT expose `HasDirtyHeights`, `HasDirtyTiles`, `UseHeightFieldTerrain`, `HeightField`.
- `public GpuSphereManager Gpu => _gpu` — lets a consumer drop to the raw GPU manager.

---

## Question 4 — Is HeightField Terrain Already Standalone?

**Best case confirmed — with a shallow structural coupling.**

`HeightFieldRenderer` is functionally standalone for rendering (direct `Graphics.DrawMesh`, own material, no dependence on the instanced tile draw). But it is structurally coupled to `SphereManager` two ways:

1. **Constructor dependency** (`HeightFieldRenderer.cs:201`): `new HeightFieldRenderer(SphereManager manager)` stores `_manager` for `_manager.Rows`, `_manager.Cols`, `_manager.Material` — all readable from the GPU manager too (identical properties on `ManagerBase`/`GpuSphereManager`).
2. **Ownership**: `SphereManager.HeightField` (line 104) is the lazy factory. Moving it out requires a new constructor or a factory accepting an abstract interface.

`HeightFieldRenderer.BindGpu(LegacyManagerShim shim)` (line 176) already anticipates migration: accepts a shim and calls `shim.SetHeights(…)` to push terrain heights into the GPU. This is the designed integration point.

**Conclusion:** Terrain is NEARLY separated. Coupling is shallow — 3 property reads (`Rows`, `Cols`, `Material`) + ownership. Ideal cut point.

---

## Architecture Decision

**Port Interface (`IGridDimensions`) + HeightFieldRenderer factory injection. Do NOT replace the CPU `SphereManager` or refactor `LegacyManagerShim`.**

1. Extract minimal `IGridDimensions` in the fork that both `SphereManager` and `GpuSphereManager` implement (they already have identical `Rows`/`Cols`/`Material`). Change `HeightFieldRenderer(SphereManager)` → `HeightFieldRenderer(IGridDimensions)`.
2. `Core.Sphere` keeps existing `SphereManager Manager` as coordinate/color/texture authority (proven). Its `DrawTiles` already branches into `HeightField.RebuildAndDraw` and returns — instanced tile draw is ALREADY dead in prod.
3. Wire a `GpuSphereManager` in PARALLEL for the instanced actor/voxel render path. Core.cs gets a second manager field, initialized in the `CreateSphereManagerAsync` callback alongside the CPU manager.
4. `VoxelRender`, `BuildingProcRender`, `FoliageTileRender` do NOT change — they already bypass both managers.

**Trade-offs:** two managers in memory one frame (~10 MB tile buffer overhead at 316², acceptable). Core.Sphere.DrawTiles still calls `Manager.DrawTiles` (→ HeightField); the GPU manager draw is added SEPARATELY in the camera frame driver.

---

## Port Interfaces (Minimal, Matching Existing Idioms)

### Fork-side (CompoundSpheres submodule)

**`CompoundSpheres/IGridDimensions.cs`** (new file)
```csharp
namespace CompoundSpheres
{
    public interface IGridDimensions
    {
        int Rows { get; }
        int Cols { get; }
        UnityEngine.Material Material { get; }
        UnityEngine.Vector3 SphereTilePosition(float X, float Y, float Height);
    }
}
```
Both `SphereManager` and `GpuSphereManager` already satisfy these members. Add `: IGridDimensions` to each class declaration.

**`HeightFieldRenderer` ctor change** (`HeightFieldRenderer.cs:201`): `SphereManager manager` → `IGridDimensions manager`; field `readonly SphereManager _manager` → `readonly IGridDimensions _manager`. `SphereManager.HeightField` still does `new HeightFieldRenderer(this)` (SphereManager now implements the interface).

### Superproject-side (WorldSphereMod)
- **`IActorVoxelPort`** — no new interface needed: `VoxelRender.Submit(Mesh, Matrix4x4, Color)` IS the port; GPU manager's `DrawTiles` is the output port. Already decoupled.
- **`ITerrainSurfacePort`** — already exists as `HeightFieldRenderer` + its `Configure(...)` callback. No new interface.
- Only interface gap = `IGridDimensions`.

---

## Exact Migration Map: Which Core.cs Usages Move Where

| Core.cs Usage | Stays on CPU SphereManager | Moves to / adds GpuSphereManager |
|---|---|---|
| `CreateSphereManagerAsync` (635) | YES — HeightField coordinate reference | Add parallel `GpuSphereManager.Creator.CreateSphereManager` in same callback |
| `Manager.HasDirtyHeights` (907) | YES — gates HeightField rebuild | No |
| `Manager.RefreshScales()` (910) | YES — feeds HeightField height data | Also call on GPU manager to push tile scale updates |
| `Manager.RefreshTextures()` (914) | Keep (legacy compat) | Add GPU `RefreshTextures()` in same block |
| `Manager.RefreshCustom("AddedColors")` (918) | Keep | Wire GPU AddedColors buffer in `CreateGpuSettings`; add GPU call |
| `Manager.RefreshColors()` (958) | Keep | Add GPU `RefreshColors()` |
| `Manager.DrawTiles(CameraX)` (1079) | YES — routes into HeightField.RebuildAndDraw | Add SEPARATE `GpuManager?.DrawTiles(CameraX)` after |
| `Manager.Destroy()` (975) | YES | Also destroy GPU manager |
| `ConfigureHeightField` (1081) | YES — terrain only | No change |
| `SphereManagerConfig = new SphereManagerSettings(…)` (1848) | YES | Add parallel `GpuSphereManagerSettings` creation |

**NOT ported to GPU (terrain-only, stay CPU):** `UseHeightFieldTerrain`, `HeightField`, `MarkDirty()`, `HasDirtyHeights`, `ConfigureHeightField`, `SampleTileOverlay`, `ConfigureLiquidSurface`, `GetCamerRange`.

**ARE ported to GPU (actor/voxel instanced):** `RefreshColors`/`UpdateColor`/`UpdateBaseLayer`, `RefreshTextures`/`UpdateTexture`, `RefreshScales`/`UpdateScale`, `RefreshCustom("AddedColors")`, and the `DrawTiles` call in the camera frame driver.

### The 15 "missing members" — resolution
Only **5** are genuinely needed on the GPU manager and all 5 already exist on `ManagerBase<T>` or via `LegacyManagerShim`:
- `RefreshScales()` bool → `LegacyManagerShim.RefreshScales()` adapts.
- `RefreshCustom("AddedColors")` bool → `ManagerBase.RefreshCustom(string)` exists; wire AddedColors buffer.
- `RefreshColors()` bool → `LegacyManagerShim.RefreshColors()` adapts.
- `UpdateCustom("AddedColors", x, y)` → `ManagerBase.UpdateCustom(string, int)` exists.
- `UpdateColor(x, y)` → `LegacyManagerShim.UpdateColor(int)` adapts.

The other 10 (`HasDirtyHeights`, `HeightField`, `UseHeightFieldTerrain`, `CreateSphereManagerAsync` [GPU gets a sync→coroutine wrapper], `GetSphereTilePosition`/`GetSphereTileRotation`/`To2D`/`To2DFast`/`tileRotation`/Shape delegates) are terrain/shape-math only — stay CPU.

---

## Data Flow (After Migration)

```
Frame Tick (3DCamera.Update → Core.Sphere.DrawTiles(cameraX))
  |
  +-- CPU SphereManager.DrawTiles(cameraX)
  |       └── if UseHeightFieldTerrain → HeightFieldRenderer.RebuildAndDraw(...)
  |               ├── Graphics.DrawMesh(_mesh, ...)         [TERRAIN SURFACE]
  |               ├── Graphics.DrawMesh(_waterMesh, ...)    [WATER]
  |               └── Graphics.DrawMesh(liquidMesh, ...)    [LAVA/SWAMP/ACID]
  |
  +-- [NEW] GpuSphereManager.DrawTiles(cameraX)
          └── per-row GpuSphereRow.DrawTiles(colStart, colCount)
                  └── Graphics.RenderMeshIndirect(...)       [ACTOR TILE INSTANCES]

Separate VoxelRender.Flush() (VoxelFrameDriver.Update)
  └── MeshInstanceBatcher.Flush()
          └── Graphics.DrawMeshInstanced(...)                [ACTOR BODIES/BUILDINGS/FOLIAGE]
```

---

## Implementation — xDD-First Build Sequence

### Phase 0 — Fork: Interface Extraction (submodule)
- [ ] **0.1 TEST** `CompoundSpheres.Tests`: assert `SphereManager` and `GpuSphereManager` both satisfy `IGridDimensions` (rows/cols/material/position).
- [ ] **0.2 IMPL** `CompoundSpheres/IGridDimensions.cs`.
- [ ] **0.3 IMPL** `SphereManager.cs`: add `: IGridDimensions`.
- [ ] **0.4 IMPL** `GpuSphereManager.cs`: add `: IGridDimensions`.
- [ ] **0.5 IMPL** `HeightFieldRenderer.cs:201`: ctor param `SphereManager`→`IGridDimensions`; field type change.
- [ ] **0.6 BUILD** submodule, 14 shape parity tests green.
- [ ] **0.7 DLL REVENDOR** rebuild `CompoundSpheres.dll` → `Assemblies/CompoundSpheres.dll` on `feat/gpu-compute-p4-consumer-migration`.

### Phase 1 — Fork: GpuSphereManager Async Creator (submodule)
- [ ] **1.1 TEST**: `GpuSphereManager.Creator.CreateSphereManager` returns a fully-initialized manager (non-null tiles, correct row/col count).
- [ ] **1.2 IMPL** add `CreateSphereManagerAsync(rows, cols, GpuSphereManagerSettings, Action<GpuSphereManager> onCreated)` — wraps the synchronous create in an `IEnumerator` yielding across frames for the heavy tile loop, matching the CPU coroutine pattern.
- [ ] **1.3 BUILD** submodule, revendor DLL.

### Phase 2 — Superproject: Wire GpuSphereManager Alongside CPU Manager
- [ ] **2.1 TEST** new `GpuManagerInitTests`/`CoreInvariantsTests`: after `Sphere.Begin`, both `Sphere.Manager` and `Sphere.GpuManager` non-null with matching Rows/Cols.
- [ ] **2.2 IMPL** `Core.cs` `Sphere`: add `static GpuSphereManager GpuManager;` by line 581.
- [ ] **2.3 IMPL** in the `CreateSphereManagerAsync` `onCreated` callback (after 639): start `GpuSphereManager.Creator.CreateSphereManagerAsync(width, height, BuildGpuSettings(), gpuMgr => { GpuManager = gpuMgr; })`.
- [ ] **2.4 IMPL** add `static void CreateGpuSettings()` (mirror `CreateSettings()` at 1846) building `GpuSphereManagerSettings` from the same delegates targeting GPU types; `GetGpuCameraRange` adapts `CurrentShape.GetCameraRange`.
- [ ] **2.5 IMPL** `DrawTiles` (1076): after `Manager.DrawTiles(CameraX)`, add `GpuManager?.DrawTiles(CameraX)` (null-guard — not ready early frames). **Risk #2 mitigation (a): keep GPU terrain layer inactive (`GpuManager.SetActive(false)`) until heights synced in Phase 4** to avoid z-fighting with HeightField on flat worlds.
- [ ] **2.6 IMPL** `Finish` (973): `GpuManager?.Destroy(); GpuManager = null;`.
- [ ] **2.7 BUILD** superproject, CI green.

### Phase 3 — Superproject: Wire RefreshSphere to Both Managers
- [ ] **3.1 TEST** `RefreshSphere()` calls both `Manager.RefreshScales()` and `GpuManager.RefreshScales()` when both non-null.
- [ ] **3.2 IMPL** `RefreshSphere` (890): after each `Manager.Refresh*()`, add mirroring `GpuManager?.Refresh*()`. For `RefreshCustom("AddedColors")` **wire the GPU AddedColors custom buffer in `CreateGpuSettings` FIRST (Risk #3 — else KeyNotFound)**. **Risk #5/#6: keep GPU `RefreshScales` INSIDE the existing `hadDirtyHeights` gate; do NOT use the shim's O(N) full-scan color path — use the GPU dirty-queue (`SetColorDirty(int I)` + `RefreshColors()`).**
- [ ] **3.3 IMPL** `UpdateLayer` (960): after `Manager.UpdateCustom`, add `GpuManager?.UpdateCustom("AddedColors", Tile.X*Sphere.Height+Tile.Y)`.
- [ ] **3.4 IMPL** `UpdateBaseLayer` (964): after `Manager.UpdateColor`, add `GpuManager?.UpdateColor(…)`.
- [ ] **3.5 BUILD** + E2E invariants.

### Phase 4 — Superproject: HeightField Heights to GPU (BindGpu)
- [ ] **4.1 TEST** after `ConfigureHeightField`, `GpuManager?.HasHeights == true` if `CompoundCompute != null`.
- [ ] **4.2 IMPL** `ConfigureHeightField` (after 1093): if `CompoundCompute != null && GpuManager != null`, build `LegacyManagerShim(GpuManager, …)` and call `hf.BindGpu(shim)` (push terrain heights into the GPU matrix kernel). Then re-activate the GPU terrain layer (undo the Phase 2.5 `SetActive(false)`).
- [ ] **4.3 IMPL** `RefreshSphere` `hadDirtyHeights` gate (925): after `Manager.HeightField.MarkDirty()`, re-sync GPU heights (verify `HeightFieldRenderer.RebuildAndDraw` already calls `PushHeightsToGpu()` when a shim is bound — `HeightFieldRenderer.cs:542`).
- [ ] **4.4 BUILD** + revendor DLL if fork changed.

### Phase 5 — Superproject: Shader Bundle + Compute Integration
- [ ] **5.1 TEST** `Core.Sphere.CompoundCompute != null` when the wsm3d-shaders bundle ships `CompoundSphereCompute.compute`.
- [ ] **5.2 IMPL** verify `LoadAssets` `CompoundCompute` load (1649); `CreateGpuSettings()` passes `CompoundCompute` as `GpuSphereManagerSettings.ComputeShader`. Guard: if `CompoundCompute == null`, SKIP GpuManager creation + log the existing warning.
- [ ] **5.3 VISUAL VERIFY** (REQUIRES USER UNITY GUI — batchmode IPC dead): capture `?mode=camera` screenshot, set day-time, inspect pixels. Confirm actor instances render via GPU path, terrain still smooth via HeightField, no z-fighting, water intact.

### Phase 6 — Cleanup (AFTER visual sign-off only)
- [ ] **6.1** Gate/remove the dead instanced draw in `SphereManager.DrawTiles` (258–296) behind `!UseHeightFieldTerrain` (already there). Leave HeightField branch untouched.
- [ ] **6.2** Delete `ILegacyManagerApi`/`LegacyManagerShim` once no Core.cs uses old `bool RefreshScales(maxPerFrame)`.
- [ ] **6.3** `[Obsolete]` on `SphereManager.DrawTiles` instanced path → point to `GpuSphereManager`.

---

## Risk Callouts

1. **Smooth-slope terrain regression (HIGH)** — terrain is HeightField-driven via `SphereManager.DrawTiles`→`RebuildAndDraw`. Phase 2 only ADDS a second draw call; does not modify the HF branch. Confirm pixels after Phase 2 before Phase 4.
2. **GPU manager draw over HeightField (MEDIUM)** — both managers draw tiles; GPU tiles sit at Height=0 until Phase 4. On flat worlds they conflict with the HF mesh. Mitigation (a): keep GPU terrain layer inactive (`SetActive(false)`) until heights synced in Phase 4. (b) accept dev-build noise. Use (a).
3. **AddedColors buffer mis-wiring (MEDIUM)** — `GpuManager?.RefreshCustom("AddedColors")` throws `KeyNotFoundException` if the buffer isn't registered. Wire AddedColors in `CreateGpuSettings` (Phase 2.4) with `CustomBufferData<Vector3>` before Phase 3.
4. **Async creation race (MEDIUM)** — `GpuManager` may be null on early frames; null-guard in 2.5 handles it. `CreateSphereManager` is synchronous (line 285); the async wrapper yields only for the tile loop.
5. **3.2s/frame CPU terrain rebuild storm (ALREADY FIXED — do not re-break)** — fixed by `SteadyStateIntervalMs`/`SettleRebuildBudget` debounce (`HeightFieldRenderer.cs:525`). Wire GPU `RefreshScales()` ONLY inside the existing `hadDirtyHeights` gate.
6. **LegacyManagerShim color-refresh O(N)/frame (LOW)** — `RefreshColors()` scans all `TotalTiles` (line 78). Use the GPU dirty-queue path, not the shim full-scan; use the shim only for height sync (Phase 4).
7. **`SphereManager.Material` vs GPU material (LOW)** — after 0.5, `HeightFieldRenderer` resolves `IGridDimensions.Material`. Confirm both managers receive the same `CompoundSphereMaterial`.

---

## Files to Create or Modify

**Submodule (`E:/wsm3d-sota-worktree/CompoundSpheres/`)**
- CREATE `CompoundSpheres/IGridDimensions.cs`
- MODIFY `CompoundSpheres/SphereManager.cs` (+`: IGridDimensions`)
- MODIFY `CompoundSpheres/Gpu/GpuSphereManager.cs` (+`: IGridDimensions`, +`CreateSphereManagerAsync`)
- MODIFY `CompoundSpheres/HeightFieldRenderer.cs:201` (ctor param type)

**Superproject (`E:/Dev/WorldSphereMod/WorldSphereMod/Code/`)**
- MODIFY `Code/Core.cs` — `GpuManager` field, `CreateGpuSettings()`, dual `DrawTiles`, `Finish` cleanup, `RefreshSphere` mirrors, `ConfigureHeightField` BindGpu
- `Code/3DCamera.cs` — no change (Core.Sphere.DrawTiles at line 245 routes to both after 2.5)

**Tests**
- MODIFY `tests/WorldSphereMod.Tests.E2E/CoreInvariantsTests.cs` (GPU init spec)
- CREATE `tests/WorldSphereMod.Tests.E2E/GpuManagerBoundaryTests.cs` (per-phase contract tests)

**DLL revendor points:** after Phase 0.7 and Phase 1.3 → `Assemblies/CompoundSpheres.dll` on `feat/gpu-compute-p4-consumer-migration` (PR #37).

### Key file paths
- `Code/Core.cs` (consumer, 581–1912)
- `CompoundSpheres/HeightFieldRenderer.cs` (terrain; ctor seam line 201; PushHeightsToGpu ~542; debounce ~525; BindGpu 176)
- `CompoundSpheres/SphereManager.cs` (CPU manager; DrawTiles branch line 247; HeightField factory 104)
- `CompoundSpheres/Gpu/GpuSphereManager.cs` (GPU manager; Creator.CreateSphereManager 285)
- `CompoundSpheres/Gpu/GpuManagerBase.cs` (ManagerBase; SetHeights 221; SetInputColor 201)
- `CompoundSpheres/Compat/LegacyManagerShim.cs` (shim; RefreshColors loop 78)
- `CompoundSpheres/Compat/ILegacyManagerApi.cs` (port contract)
- `Code/Voxel/VoxelRender.cs`, `Code/Foliage/FoliageTileRender.cs`, `Code/ProcGen/BuildingProcRender.cs` (all independent)
