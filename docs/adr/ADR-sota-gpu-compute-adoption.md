# ADR-sota-gpu-compute-adoption: Adopting Compound-Spheres GPU-Compute Rewrite as SOTA Renderer Base

**Status:** Proposed (P1 scaffolding accepted)

**Date:** 2026-05-30

**Author:** Claude / KooshaPari

**Stakeholders:** WorldSphereMod3D mod, CompoundSpheres submodule (fork
`KooshaPari/Compound-Spheres-3D`, branch `wsm3d/main`), upstream
`MelvinShwuaner/Compound-Spheres`.

---

## Context

Upstream `MelvinShwuaner/Compound-Spheres` is **3 commits ahead** of our
`wsm3d/main` (`b1b7d0a` "new shit" → `bbf302c` "dynamic management" →
`5b87277` "the compute shader update"). Those commits are a **ground-up
GPU-driven rewrite** that moves per-tile **model-matrix and color computation
off the CPU and onto the GPU via compute kernels**. This is strictly superior
to our current CPU `UpdateBuffer`/`SetBufferChunked` path (see ADR-0015,
which fought a 25–45 s synchronous upload by chunking on the CPU — the GPU
path removes that work entirely).

We want this as our SOTA renderer base. But a blind merge **breaks the main
mod build**: the upstream API is incompatible with our consumers, and our
fork-only additions (HeightFieldRenderer, FrustumCuller, water sub-mesh,
Perlin displacement, dirty-gate) do not exist upstream.

### The compute-kernel contract (reverse-engineered from upstream/main)

The keystone asset — the `.compute` shader — **does not exist in either
repo**. Upstream `ManagerBase.Init()` calls
`ComputeShader.FindKernel(MatrixKernel)` / `FindKernel(ColorKernel)` and
`ComputeShader.Dispatch(...)` against a shader asset that was never committed
(it lived only in Melvin's local Unity project). The contract, derived from
`ManagerBase.cs`, `BufferUtils.cs` (`ComputeGraphicsBuffer<T>`), and
`SphereManager.cs`:

| Item | Type / wiring | Source |
|------|---------------|--------|
| `InputPositions` | `ComputeBuffer<Vector2>` bound to MatrixKernel; set in `Begin()` to `Tiles[i].Position` (grid X=row, Y=col) | `ManagerBase.Init` |
| `OutputMatrices` | `ComputeGraphicsBuffer<Matrix4x4>` (GraphicsBuffer + `Dirty` ComputeBuffer), bound to MatrixKernel `"OutputMatrices"` AND material `"Matrixes"` | `ManagerBase.Init` |
| `OutputColors` | `ComputeGraphicsBuffer<Color32>` bound to ColorKernel `"OutputColors"` AND material `"Colors"` | `ManagerBase.Init` |
| `Dirty` | per-buffer `ComputeBuffer<int>` (1 = recompute), `Refresh()` uploads dirty flags then `Dispatch` | `ComputeGraphicsBuffer.Refresh` |
| `Radius` | `ComputeShader.SetFloat("Radius", Rows/(2*PI))` | `SphereManager.Init` |
| Thread group | `numthreads(64,1,1)`; dispatch `ceil(TotalTiles/64)` groups | `Begin()`, `ComputeGraphicsBuffer` ThreadCount |
| Scales | NOT computed in compute; `Buffer<Vector3>` bound to material `"Scales"`, applied in the **vertex** shader (`v.vertex.xyz * Scales[ID]`) | `CompoundSphere.shader` |

The matrix kernel must reproduce, on the GPU, the exact CPU geometry the old
fork used per tile:
- position = `DefaultSettings.CartesianToCylindrical(Radius, X, Y, height)`
  (`phi = X/Rows*2PI`, `r = Radius+height`, `xy = r*(cos,sin)phi`, `z = Y`);
- rotation = `DefaultSettings.CylindricalRotation` =
  `LookRotation((Vector2)Position) * Euler(0,-180,0)` (faces radially outward);
- output is a column-major TRS `float4x4` (scale deferred to vertex shader).

The color kernel is a pass-through pack today (copy `InputColors` → `OutputColors`,
both packed RGBA32) — the seam where GPU tinting/day-night/biome blends move
later.

### What breaks (must be reconciled, NOT merged blindly)

- New hierarchy `SphereManager : ManagerBase<SphereTile> : ManagerRoot:MonoBehaviour`;
  adds `TileBase`, `DynamicManager/Row/Tile`, `BufferBase`/`ComputeGraphicsBuffer`.
- `SphereTile` struct→class; `Position` Vector3→**Vector2**; **no** `Rotation`/
  `Matrix`/`UpdateColor` (now GPU). Private field `SphereTiles`→`Tiles`.
- `SphereManagerSettings` ctor now **requires** `(ComputeShader, string matrixKernel,
  string colorKernel)` and **drops** the get-position / get-rotation / get-color
  delegates.
- `RefreshScales/Colors/Textures` return **void** (were `bool`).

Main-mod consumers of the OLD API that will not compile against upstream as-is:
- `Core.cs` — `SphereManagerSettings` ctor (~1525), `RefreshScales` bool checks
  (~889/893/897/932), `To3D`/Shape/tile-color/rotation delegates, reflected
  `"SphereTiles"` field (~1009);
- `CompoundSphereScripts.cs`;
- `Tools.cs` — `SphereTile.Rotation` (~515);
- `Mod.cs`.

Fork-only additions that **must survive**: `HeightFieldRenderer.cs`
(corner-averaged height-field mesh; only touches `_manager.Rows/Cols/Material/
SphereTileMesh` — ports cleanly), `FrustumCuller.cs` (CPU per-tile Vector3 cull —
**needs rework**, positions moved to GPU and to Vector2), water sub-mesh, Perlin
micro-displacement, dirty-gate.

---

## Decision

Adopt the GPU-compute rewrite **incrementally on `wsm3d/main`**, behind an
**adapter shim** that preserves the old consumer API until the main mod is
migrated. Each phase is independently buildable and assignable to a separate
worktree agent. Do **not** force the breaking merge.

### P1 — Keystone + additive substrate (this ADR's scaffolding)
1. **Author the missing `.compute`** (`Default Assets/CompoundSphereCompute.compute`)
   implementing the contract above. *(DONE this phase.)*
2. Bring in upstream `ManagerBase`/`TileBase`/`DynamicManager`/`DynamicRow`/
   `DynamicTile`/`BufferBase`+`ComputeGraphicsBuffer` **additively**, namespaced
   so they coexist with the current classes (e.g. under
   `CompoundSpheres.Gpu` or as `*Gpu` types) — do **not** replace the existing
   `SphereManager`/`SphereManagerSettings`/`SphereTile` yet.
3. **Adapter shim** (`CompoundSpheres/Compat/LegacyApiShim` — signatures
   scaffolded this phase, see below) exposing the OLD surface on top of the new
   model: old `SphereManagerSettings` ctor (with position/rotation/color
   delegates), `bool RefreshScales/Colors/Textures`, `SphereTiles` field,
   `SphereTile.Rotation`/`.Matrix`. The shim translates delegate-driven CPU
   values into the new GPU `InputPositions`/`InputColors`/`InputHeights`/`Scales`
   buffers. **Main mod keeps compiling unchanged.**
4. Bake-project wiring: teach `Tools/Unity-Bake-Project/Assets/Editor/BakeShaders.cs`
   to copy + tag `*.compute` into the `wsm3d-shaders` bundle (it currently only
   handles `*.shader`). Runtime loads the kernel from that bundle (mirrors
   `Core.LoadAssets`).

### P2 — Port HeightFieldRenderer onto the new manager
Retarget `HeightFieldRenderer` to `ManagerBase.Rows/Cols/Material/SphereTileMesh`
and feed corner heights into the new `InputHeights` compute buffer (the
`.compute` already reads it, gated by `HasHeights`). Keep water sub-mesh + Perlin.

### P3 — Rework FrustumCuller for GPU/Vector2 positions
`FrustumCuller` currently culls on CPU `SphereTile` Vector3 positions, which no
longer exist. Two options (decide in P3): (a) cull on `(X,Y)` grid + recompute
the cylindrical world pos CPU-side from `Radius` (cheap, no readback), or
(b) move culling into a GPU cull kernel feeding the indirect-args
`commandBuf` (matches `DynamicManager`'s `Culler` ComputeShader pattern).
Prefer (a) for P3 (no architecture change), (b) is a later perf ADR.

### P4 — Migrate main-mod consumers + remove the shim
Migrate `Core.cs` / `CompoundSphereScripts.cs` / `Tools.cs` / `Mod.cs` to the
new API (Vector2 positions, GPU matrices/colors, void Refresh*, `Tiles` field),
delete the legacy `SphereManager`/`SphereTile`/`SphereManagerSettings` and the
shim. Update the reflected `"SphereTiles"` lookup to `"Tiles"`.

### P5 — Rebuild DLL + 60f1 bundle
`dotnet build WorldSphereMod.csproj -c Release`; run `Tools/bake-shaders.ps1`
to bake the compute shader into the `wsm3d-shaders` bundle; `install.ps1`
(stale-DLL fix). Vision-verify per the project charter.

---

## Consequences

- **Positive:** removes the entire CPU matrix/color upload (ADR-0015's
  chunked-upload workaround becomes unnecessary), shifts work to GPU, scales to
  large maps; SphereTile becomes a lightweight class; keeps our fork features.
- **Negative / risk:** two parallel manager implementations during P1–P3
  (shim cost); FrustumCuller rework is the riskiest piece; compute path requires
  `SystemInfo.supportsComputeShaders` (already gated in `Mod.cs:29`).
- **Reversible:** the `.compute` + additive types do not touch the old build;
  if P4 stalls we still ship the old CPU path.

## Status of this phase (P1 scaffolding)

- **DONE:** authored `Default Assets/CompoundSphereCompute.compute` (kernels
  `OutputMatrices` + `OutputColors`, contract above); this ADR; adapter-shim
  signature stubs (if committed — see report).
- **DEFERRED to P2+:** importing upstream `ManagerBase`/`Dynamic*`/`BufferBase`
  types, full shim body, HeightFieldRenderer port, FrustumCuller rework,
  consumer migration, bake-script `*.compute` support, DLL+bundle rebuild.
