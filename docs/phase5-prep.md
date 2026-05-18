# Phase 5 prep — Compound-Spheres-3D submodule + Unity version

Research notes for the Phase 5 backend rebuild (per-vertex normals, water mask).
Not implementation work yet — context for whoever picks up Phase 5.

## Upstream backend: `MelvinShwuaner/Compound-Spheres`

- URL: https://github.com/MelvinShwuaner/Compound-Spheres
- Last push: 2026-03-29
- Stars: 0, no license set (treat as proprietary until Melvin clarifies)
- Built for **Unity 2022.3** per the README ("other versions might be incompatible")
- 7 C# files, single .csproj, no Unity Asset folder — meant to be imported into a Unity project

Layout:
```
CompoundSpheres.sln
CompoundSpheres/
  BufferUtils.cs
  CompoundSpheres.csproj
  DefaultSettings.cs
  SphereManager.cs
  SphereManagerSettings.cs
  SphereRow.cs
  SphereTile.cs
Default Assets/
README.md
```

## What the API looks like

`SphereManager.Creator.CreateSphereManager(rows, cols, SphereManagerSettings, name)` returns a manager. `SphereManagerSettings` carries delegates for per-tile position/rotation/scale/color/texture-index plus a list of `IBufferData` for custom per-tile buffers (e.g. `CustomBufferData<Vector3>("AddedColors", 12, callback)`).

The mod already wires this up in `Core.cs:294-346` (`Begin()`) and `Core.cs:407-413` (`LoadAssets()`).

## Phase 5 deliverables that touch this backend

Per `docs/PLAN.md` lines 122-132:

1. **Per-vertex normals on terrain mesh.** The vendored `CompoundSpheres.dll` currently produces an unlit-shaded mesh (no per-vertex normals). Need to either:
   - Add a custom buffer for vertex normals (computed once per tile from the height field).
   - Modify the shader to derive normals via screen-space derivatives (cheap but lower quality).
   - The cleanest path is the custom buffer route — fits the existing `IBufferData` extensibility hook.

2. **Water-mask SSBO** (Phase 4 prereq). A `CustomBufferData<float>` for `sea_level - tile_height` per tile, consumed by a separate water mesh layer that overlays the terrain.

3. **Material slider `_NormalStrength`** — material-level concern, lands with the rebuilt shader.

## Submodule plan

Per `PLAN.md:48-49`:
- Hard-fork the repo to `KooshaPari/Compound-Spheres-3D`.
- Add it as a git submodule at `External/Compound-Spheres-3D/`.
- Replace the vendored DLL reference in `WorldSphereMod.csproj` with a `<ProjectReference>` to the submodule's `.csproj`.
- Binary-diff first build output against vendored DLL on a known asset bundle to confirm parity.

## Unity version risk

The README explicitly says Unity 2022.3. This machine has Unity Hub plus:
- `2021.3.45f1`
- `6000.3.11f1` (Unity 6)

**Neither is 2022.3.** AssetBundle compatibility across Unity major versions is fragile (different serialization formats). Risks:
- **Building** the modified Compound-Spheres in 2021 or Unity 6 may or may not produce a DLL the game's Unity 2022 runtime can load.
- **Rebuilding the `worldsphere` AssetBundle** in 2021 produces bundles incompatible with the game's 2022 runtime. Confirmed risk if Phase 5's shader work requires bundle rebuild.

**Action items before Phase 5 starts:**
1. Install Unity 2022.3 LTS via Unity Hub (multi-GB download, user choice).
2. Cut the `External/AssetBundleBuilder/` Unity project as a 2022.3 project.
3. Verify a no-op rebuild of the existing `worldsphere` bundle in 2022.3 produces a binary-identical (or game-loadable) output. Use that as the parity test before any modifications.

## Order of operations for Phase 5

Recommended (each is its own commit; whole phase = one PR):

1. Add `External/Compound-Spheres-3D/` as a submodule pointing at our fork (initially identical to upstream).
2. Build the submodule in Unity 2022.3, verify it produces the same DLL the vendored one ships.
3. Add per-vertex normal buffer + companion lit shader to the submodule.
4. Add water-mask buffer to the submodule (used by Phase 4).
5. Swap the .csproj reference from vendored to submodule.
6. Replace `_material` in `VoxelRender.cs` with a real `VoxelLit.shader` material loaded from the rebuilt bundle.
7. Add `Sun` directional light + 4-cascade shadow config to `CameraManager.Begin()`.
8. Default `SavedSettings.HighShadows = true`. Update phase table.

## Open questions for whoever owns Phase 5

- Should the fork be kept in sync with upstream (cherry-pick Melvin's changes) or diverge fully? Affects how rebase-friendly we keep the patch set.
- Should we contribute the per-vertex normals + water mask back upstream as a PR? Lower-friction for the user; higher-friction for control.
- If Unity 2022.3 install is a hard no, fallback is to keep the vendored DLL and patch shaders only via `MaterialPropertyBlock` overrides — works for some Phase 5 features (basic lighting from screen-space derivatives) but not all (e.g., the water mask needs backend cooperation).
