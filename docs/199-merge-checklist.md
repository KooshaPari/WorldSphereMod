# #199 GPU-Compute Go-Live — PR #37 Merge Checklist

> **Status**: CI-green, pending Unity-runtime visual gate (user-gated).
> **Branch**: `feat/gpu-compute-p4-consumer-migration`
> **Worktree**: `E:/wsm3d-wt/pr37`
> **Last verified**: 2026-06-01

---

## PR #37 Description

### Summary

This PR delivers the GPU-compute go-live for WorldSphereMod3D (task #199). It wires
`GpuSphereManager` alongside the existing CPU `SphereManager` in `Core.cs` using a
**hybrid port-boundary** architecture: the GPU path owns instanced actor/voxel
rendering; HeightField terrain stays CPU. This preserves full compatibility with the
existing terrain pipeline while enabling GPU-driven sphere positioning via the
`CompoundSphereCompute.compute` D3D11 compute shader.

### What landed (P0 – P5)

| Phase | Commit | What it does |
|---|---|---|
| P0 | `3564692d` | Revendor CompoundSpheres.dll with `IGridDimensions` port; decouple HeightFieldRenderer |
| P1 | `1f8ea647` | Revendor with `GpuSphereManager.Creator.CreateSphereManagerAsync` |
| P2 | `4c0fa34b` | Wire `GpuSphereManager` in parallel with CPU Manager in `Core.cs`; `SetActive(false)` until BindGpu |
| P3 | `2ac48ade` | Mirror `RefreshSphere` / `UpdateLayer` / `UpdateBaseLayer` to `GpuManager` |
| P4 | `64695b45` | `BindGpu`: push HeightField heights to GPU via `LegacyManagerShim`, re-activate layer |
| P5 | `5665df5f` | `CreateGpuSettings` passes `CompoundCompute` shader; null-guard skips GPU init when compute is absent |
| PR#37 fixes | `1ade9d3f` | Null-guard GPU callback, `ConfigureShape`, `UpdateScale`/`UpdateTexture` mirrors; remove stale layout file |

### DLL vendor mechanism

`WorldSphereMod/Assemblies/CompoundSpheres.dll` is a vendored prebuilt from the
`sota/gpu-compute-golive` submodule branch. The `.csproj` references it via
`<HintPath>WorldSphereMod\Assemblies\CompoundSpheres.dll</HintPath>`. This avoids
Unity import friction and means NML's Roslyn compile picks up the GPU surface without
needing the submodule checked out in a worktree.

**DLL integrity** (verified 2026-06-01):

```
Vendored:  62D4DEA1138AC2EF03DA08EFE5C28511164E40B212EF943EB1304443B9DD28E7
Built from sota/gpu-compute-golive @ 308f2bb: 62D4DEA1138AC2EF03DA08EFE5C28511164E40B212EF943EB1304443B9DD28E7
MATCH: YES
```

### Test counts

| Suite | Passed | Skipped | Notes |
|---|---|---|---|
| Unit | 158 | 3 | 3 skips = DelegateBindingTests needing Unity runtime (intentional) |
| Integration | 69 | 0 | |
| E2E | 310 | 0 | Includes 14 `GpuManagerBoundaryTests` covering Phases 2–5 |
| **Superproject total** | **537** | **3** | |
| Submodule parity (sota) | 19 | 0 | ShapeParityTests × 3 shapes + GridDimensionsPortTests |

### Z-fighting / BindGpu safety

`GpuSphereManager` is created with `SetActive(false)` (Phase 2) and only re-activated
in `BindGpu` (Phase 4) after `HeightField.InputHeights` has been pushed to the GPU
buffer. This prevents the GPU tile layer rendering at z=0 and z-fighting the
HeightField terrain during the init window.

---

## User Merge-Ready Checklist

### Headless CI gates (all green — no action needed)

- [x] `dotnet build WorldSphereMod.sln` — 0 errors
- [x] `dotnet test WorldSphereMod.sln` — 537 pass, 3 intentional skips
- [x] Submodule parity tests (`dotnet test E:/wsm3d-sota-worktree/Tests/CompoundSpheres.Tests`) — 19/19
- [x] DLL SHA256 matches sota build artifact
- [x] Collision-clean (render-mgr owns `E:/Dev/WorldSphereMod`; this PR lives in `E:/wsm3d-wt/pr37` only)

### Unity-runtime visual gate (user action required)

#### Install

```powershell
# Run from E:/wsm3d-wt/pr37
just install
# OR manually:
# Build: dotnet build WorldSphereMod.sln -c Release
# Copy bin/WorldSphereMod/Release/ → WorldBox Mods/<WSM3D-GUID>/
# Copy WorldSphereMod/Assemblies/CompoundSpheres.dll → Mods/<WSM3D-GUID>/Assemblies/
# Copy WorldSphereMod/AssetBundles/ → Mods/<WSM3D-GUID>/AssetBundles/
```

#### What to look for

1. **GPU-compute path active**: In the debug console (`PlayerPrefs debug_log = 0` to
   suppress per-frame noise), confirm `[WSM3D] GpuSphereManager created` log appears
   on world load. If `CompoundCompute` loaded from the shader bundle, `CreateGpuSettings`
   returns true and the GPU manager activates.

2. **Voxel actors render via GPU instancing**: Actors (creatures, buildings) should
   appear as 3D voxel meshes positioned by the compute shader. No magenta/missing
   materials. No all-impostor fallback (check `LodSelector.ImpostorOnlyMode = false`
   in logs).

3. **HeightField terrain stays CPU**: Terrain should render normally (hills, water,
   etc.). The GPU layer handles actors only — confirm no terrain geometry duplication
   or z-fighting on land tiles.

4. **No z-fighting on load**: During world generation, confirm no flickering between
   the GPU tile layer and the HeightField. The `SetActive(false)` → `BindGpu` →
   `SetActive(true)` sequence should make the GPU layer invisible until heights are
   synced.

5. **RefreshSphere / color updates work**: Change a tile's terrain type in-game;
   confirm the voxel actor color/texture updates (mirrors `GpuManager?.RefreshTextures()`
   and `GpuManager?.RefreshColors()`).

#### Pass criteria

- No Unity crash or NullReferenceException in the Player.log related to `GpuSphereManager`
- Voxel actors visible and correctly positioned on the sphere surface
- HeightField terrain unaffected
- No z-fighting during or after world load

#### If GPU path falls back silently

If `CompoundCompute` is null (shader bundle not loaded), `CreateGpuSettings` returns
false and the GPU manager is never created. The mod runs CPU-only — this is the
designed graceful fallback. Check that `worldspheremod_shaders` bundle loads in
`LoadAssets` and contains the `CompoundSphereCompute` compute shader key.

---

## Do NOT merge until

- [ ] Unity-runtime visual gate passes (screenshot confirming GPU instancing + no z-fighting)
- [ ] Team-lead gives merge signal (sequenced after render-mgr milestone)
- [ ] PR #37 on GitHub updated with this checklist summary

---

*Generated by gpu-mgr 2026-06-01. Trace: task #199, FR: GPU-compute go-live.*
