# ADR-0015: CompoundSpheres Performance and Async Initialization Fix

**Status:** Accepted

**Date:** 2026-05-25

**Author:** Claude / KooshaPari

**Stakeholders:** WorldSphereMod3D mod, CompoundSpheres library, all shape modes (flat + cylindrical)

---

## Context

CompoundSpheres is the GPU-instanced tile rendering engine that powers the
3D terrain in WorldSphereMod. `SphereManager` owns four
`GraphicsBuffer`s (Matrixes 64B, Colors 4B, Scales 12B, Textures 4B)
plus one indirect-args command buffer, all sized to `TotalTiles = Rows x Cols`.
On a standard WorldBox map (e.g. 461 x 720), that is **331,920 tiles**.

### Problem Statement

`SphereManager.Creator.CreateSphereManager` was fully synchronous: it
allocated all tile objects, built every `SphereRow`, then called
`Manager.Begin()` which performed four sequential `GraphicsBuffer.SetData`
uploads of the entire tile array in a single frame. This blocked the
Unity main thread for 25-45 seconds depending on map size and shape mode,
causing the game to freeze at the "Loading finished" transition with no
progress indication. On slower GPUs the OS marked the window as
"Not Responding."

### Forces

- Unity's `GraphicsBuffer.SetData` must run on the main thread (GL
  context affinity).
- WorldBox's `MapBox.finishMakingWorld` calls `Core.Become3D()` during
  the single post-generation frame. Any synchronous work there delays
  the first rendered frame.
- The mod already relies on Harmony postfixes that run every frame (e.g.
  `SphereControl.DrawTiles`). Rendering partially-initialized buffers
  would produce garbage geometry.
- Flat map mode is inherently cheaper (~25s) than cylindrical (~36-45s)
  because tile position/rotation delegates are simpler, but both exceed
  the 16ms frame budget by orders of magnitude.
- The `IsReady` gate must be checked at every draw call site; missing it
  causes a single frame of corrupted instanced draws.

### Alternatives Considered

| Alternative | Pros | Cons | Why not chosen |
|---|---|---|---|
| Background thread tile creation + main-thread buffer upload | Tile object alloc is ~30% of total time and is not GPU-bound | `SphereTile` constructor reads `SphereManager` delegates for position/rotation/scale which touch Unity APIs (Quaternion, Vector3); unsafe off main thread | Thread-safety risk too high for the gain |
| Single `ComputeBuffer.SetData` with native array | Fewer managed-to-native transitions | Still O(n) in a single frame; no chunking benefit | Does not solve the freeze |
| Loading screen overlay during sync init | Cheap to implement | User still cannot interact; OS still flags Not Responding after ~5s | Poor UX, does not reduce total time |
| **Coroutine-based chunked init (chosen)** | Spreads work across frames; game loop stays responsive; `IsReady` gate prevents partial draws | Total wall-clock time slightly increases due to yield overhead; adds complexity to the creation path | -- |

## Decision

Split `SphereManager` initialization into a coroutine pipeline that yields
control back to Unity every N tiles (default `chunkSize = 4096`). Gate all
`DrawTiles` calls on `SphereManager.IsReady` so rendering waits for buffer
uploads to complete.

### Implementation Notes

**CompoundSpheres library** (`External/Compound-Spheres/CompoundSpheres/`):

1. **`SphereManager.BeginCoroutine(int chunkSize = 4096)`**
   (`SphereManager.cs:304-313`) -- coroutine version of `Begin()`. Sets
   `IsReady = false`, then yields through four chunked buffer fills
   (Matrixes, Scales, Colors, Textures) via `GraphicsBuffer.SetBufferChunked`.
   Sets `IsReady = true` on completion.

2. **`SphereManager.IsReady`** (`SphereManager.cs:298`) -- public bool
   property. `false` while `BeginCoroutine` is running. The synchronous
   `CreateSphereManager` path sets it `true` immediately after `Begin()`.

3. **`Creator.CreateSphereManagerAsync`**
   (`SphereManager.cs:461-484`) -- coroutine factory. Sequence:
   - `Init()` (allocate buffers, bind material) -- single frame
   - `yield return BuildTilesAndRowsAsync(...)` -- chunked tile+row
     construction, yields every `chunkSize` tiles
   - `FinalizeRows(...)` -- per-row `InitRangeBuffer`, compute
     `_tileHalfSize` -- single frame
   - `onCreated?.Invoke(Manager)` -- caller stores the reference and
     runs post-init setup (camera, lighting) while buffers are still
     uploading
   - `yield return Manager.BeginCoroutine(chunkSize)` -- chunked buffer
     fills across frames
   - `sphereManagerSettings.Initiation(Manager)` -- shape-specific
     collider setup (cylinder or quad)

4. **`BuildTilesAndRowsAsync`** (`SphereManager.cs:496-511`) -- yields
   every `chunkSize` tile constructions. Each tile is a `new SphereTile`
   plus a `new SphereRow` at row boundaries.

**WorldSphereMod** (`WorldSphereMod/Code/Core.cs`):

5. **`Sphere.Begin()`** (`Core.cs:559-596`) -- calls
   `CreateSphereManagerAsync` via `StartCoroutine` on the mod's
   MonoBehaviour. The `onCreated` callback assigns `Manager` and calls
   `FinishBecome3D()` (camera + lighting setup). This fires after tiles
   are built but before buffer uploads finish.

6. **`Sphere.DrawTiles(int CameraX)`** (`Core.cs:852-855`) -- the
   `IsReady` gate:
   ```csharp
   if (Manager == null || !Manager.IsReady) return;
   Manager.DrawTiles(CameraX);
   ```
   This is called every frame from the `SphereControl` Harmony postfix.
   Before `IsReady` becomes true, the frame renders normally (2D fallback
   or blank terrain) instead of corrupted instanced geometry.

**Frustum culling** (`External/Compound-Spheres/CompoundSpheres/FrustumCuller.cs`):

7. **Chunk-level AABB culling** -- `FrustumCuller.GetVisibleColumnRange`
   groups columns into chunks of 16 (`ChunkSize = 16`). For each chunk,
   it computes a world-space AABB from the first and last tile positions
   plus `_tileHalfSize`, then tests against the camera frustum planes.
   Visible chunks are merged into a contiguous `[colStart, colStart + colCount)`
   range. `SphereRow.DrawTiles(colStart, colCount)` adjusts the
   `MaterialPropertyBlock`'s `Row` offset and creates a per-range
   indirect-args buffer to draw only the visible columns.

8. **Per-row draw granularity** (`SphereRow.cs:66-80`) -- when the
   visible range is a strict subset, `DrawTiles(colStart, colCount)`
   shifts the shader's `Row` uniform by `colStart`, sets
   `instanceCount = colCount` in the range command buffer, and issues
   `Graphics.RenderMeshIndirect`. When the full row is visible, it falls
   through to the original single-call path.

### Data flow summary

```
Sphere.Begin()
  |
  +-- StartCoroutine(CreateSphereManagerAsync(...))
        |
        +-- Init(): alloc 4 GraphicsBuffers (331K entries each)
        +-- yield BuildTilesAndRowsAsync: ~81 yields (331K / 4096)
        +-- FinalizeRows: per-row range buffers + _tileHalfSize
        +-- onCreated(Manager): FinishBecome3D() runs camera/lighting
        +-- yield BeginCoroutine:
        |     +-- yield Matrixes.SetBufferChunked  (~81 yields)
        |     +-- yield Scales.SetBufferChunked    (~81 yields)
        |     +-- yield Colors.SetBufferChunked    (~81 yields)
        |     +-- yield Textures.SetBufferChunked  (~81 yields)
        |     +-- IsReady = true
        +-- Initiation(): shape collider creation

Each frame (SphereControl postfix):
  Sphere.DrawTiles(CameraX)
    |-- if !Manager.IsReady -> return (no draw)
    |-- FrustumCuller.UpdatePlanes(camera)
    |-- for each visible row:
    |     +-- GetVisibleColumnRange (16-col chunk AABB test)
    |     +-- SphereRow.DrawTiles(colStart, colCount)
    |           +-- Graphics.RenderMeshIndirect(rangeCommandBuf)
```

## Consequences

### Positive

- Game no longer freezes at "Loading finished." The main thread yields
  every 4096 tiles, keeping frame time under ~50ms during init (measured
  on a mid-range GPU).
- `FinishBecome3D` (camera pivot, cubemap lighting, procedural sky) runs
  as soon as the `Manager` reference exists, so the player sees a 3D
  camera and skybox while terrain buffers are still uploading.
- Frustum culling reduces per-frame draw calls from `visibleRows x 1` to
  `visibleRows x visibleChunks`, culling 30-70% of columns at typical
  zoom levels. `Manager.LastCulledTiles` exposes the cull count for
  profiling.
- The synchronous `CreateSphereManager` path remains available and sets
  `IsReady = true` immediately, preserving backward compatibility for
  callers that do not need async.

### Negative

- Total wall-clock time from `Become3D` to fully-rendered terrain is
  **36-45 seconds** for cylindrical, **~25 seconds** for flat. This is
  longer than the synchronous path's raw elapsed time because of per-yield
  overhead and frame scheduling. The user sees an empty or partially-lit
  terrain during this window.
- `onCreated` fires before `IsReady` is true. Any code that stores the
  `Manager` reference and immediately calls `DrawTiles` or buffer refresh
  methods will silently no-op until `IsReady` flips. This is correct
  behavior but non-obvious to future contributors.
- Each `SphereRow` now allocates a dedicated `_rangeCommandBuf`
  (`GraphicsBuffer.IndirectArguments`) for frustum-culled partial draws,
  doubling the per-row GPU buffer count. On a 461-row map that is 461
  additional small buffers (~4KB total).

### Neutral

- Chunk size of 16 columns in `FrustumCuller` is a compile-time constant.
  It could be made configurable but the current value balances AABB
  accuracy against per-chunk test overhead for typical map sizes.
- The `chunkSize = 4096` default for tile construction and buffer upload
  is passed through the entire call chain but is not exposed in
  `SavedSettings`. Tuning it requires a code change.

## Remaining Performance Issues and Future Work

1. **36-45s init is still too long.** The chunked coroutine prevents
   freezing but does not reduce total work. Future approaches:
   - **Streaming tile load:** only build tiles within the camera's initial
     viewport, then expand outward across frames. Requires `SphereRow`
     to support lazy initialization.
   - **Native buffer fill:** use `NativeArray<T>` + `SetData(NativeArray)`
     to avoid managed-to-native marshaling overhead per chunk.
   - **Parallel tile construction:** `SphereTile` position/rotation
     delegates currently touch Unity math types. If those are replaced
     with `System.Numerics` or pure math, tile objects can be built on
     worker threads with only the final `SetData` on main thread.

2. **No LOD for sphere tiles.** Every tile draws at full mesh resolution
   regardless of distance. A future LOD system should:
   - Merge distant tiles into coarser chunks (e.g. 4x4 or 8x8 super-tiles).
   - Use `SphereRow.DrawTiles(colStart, colCount)` to draw merged ranges
     with a simplified mesh.
   - Transition between LOD levels based on camera-to-surface distance
     (already computed in `Sphere.LogDiagnostics`).

3. **CPU-side frustum culling.** The current chunk-level AABB test runs
   on the CPU (one `GeometryUtility.TestPlanesAABB` per 16-column chunk
   per visible row). For a 461x720 map with 45 chunks/row and ~200
   visible rows, that is ~9000 AABB tests per frame. A GPU-driven
   culling compute shader could eliminate this CPU cost entirely by
   writing visible instance IDs into an append buffer.

4. **Flat map is 10s faster than cylindrical.** The cylindrical
   position delegate (`CartesianToCylindrical`) performs trig (sin/cos
   via `PointOnCircle`) per tile; the flat delegate is a trivial
   coordinate swap. Precomputing cylindrical positions into a lookup
   table during init would eliminate per-tile trig during buffer fill.

## References

- `External/Compound-Spheres/CompoundSpheres/SphereManager.cs` -- async
  factory, `BeginCoroutine`, `IsReady` gate, buffer management
- `External/Compound-Spheres/CompoundSpheres/SphereRow.cs` -- per-row
  indirect draw, range buffer for partial column draws
- `External/Compound-Spheres/CompoundSpheres/FrustumCuller.cs` --
  chunk-level AABB frustum culling (16-column chunks)
- `WorldSphereMod/Code/Core.cs:559-596` -- `Sphere.Begin()` coroutine
  launch and `onCreated` callback
- `WorldSphereMod/Code/Core.cs:852-855` -- `Sphere.DrawTiles` IsReady gate
- `WorldSphereMod/Code/CompoundSphereScripts.cs` -- tile position, rotation,
  scale, and color delegates passed to `SphereManagerSettings`
- Related: ADR-0011 (Phase 1 visibility postmortem), ADR-0016 (Phase 1
  victory chain)
