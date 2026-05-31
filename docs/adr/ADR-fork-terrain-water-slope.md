# ADR: Own terrain mesh, water surface, and slope smoothing in the Compound-Spheres fork

Status: Proposed (investigation + plan; no code changes yet)
Date: 2026-05-30
Branch: `fix/shader-standard-fallback`
Submodule: `External/Compound-Spheres` -> remote `KooshaPari/Compound-Spheres-3D`

---

## TL;DR — root cause of the slope regression

The known-good slope/terrain work was a **continuous corner-averaged height-field
mesh with analytic gradient normals**, implemented entirely *inside the fork* on the
`wsm3d/main` branch (file `CompoundSpheres/HeightFieldRenderer.cs`, 405 lines).

The submodule working tree is currently **detached at `73a7b77`** — a
`Merge branch 'main' of MelvinShwuaner/Compound-Spheres` (upstream). That merge commit
**does not contain any of the WSM3D height-field work**. The 4 WSM3D commits live only
on `wsm3d/main`, which is 4 commits *ahead* of the checked-out HEAD:

```
git -C External/Compound-Spheres log --oneline HEAD..wsm3d/main
7213176 feat: add Perlin micro-displacement to terrain corner heights
ebe12a8 perf: gate heightfield rebuild on actual tile dirty + skip camera-pan rebuilds
6e1cf94 feat(wsm3d): height-field renderer, chunked UpdateBuffer, null guards
abb54ff feat(frustum): add FrustumCuller + integrate into SphereManager DrawTiles
git -C External/Compound-Spheres log --oneline wsm3d/main..HEAD   # (empty)
```

Verification that HEAD source is stale:
- `grep -c HeightField External/Compound-Spheres/CompoundSpheres/SphereManager.cs` = **0**
- but `git diff --stat HEAD wsm3d/main` shows the WSM3D additions:
  ```
  CompoundSpheres/FrustumCuller.cs         | 117 +++++   (new)
  CompoundSpheres/HeightFieldRenderer.cs   | 405 +++++   (new)
  CompoundSpheres/SphereManager.cs         | 274 ++++    (HeightField/Culler wiring)
  CompoundSpheres/SphereManagerSettings.cs |  22 ++
  CompoundSpheres/SphereRow.cs             |  34 ++
  CompoundSpheres/BufferUtils.cs           | 124 ++
  ```

The **shipped DLL is fine** — `WorldSphereMod/Assemblies/CompoundSpheres.dll`
(built 2026-05-29) contains `HeightFieldRenderer`, `FrustumCuller`,
`RebuildAndDraw`, `get_/set_UseHeightFieldTerrain`, `PerlinNoise`
(confirmed via `strings`). It was built from `wsm3d/main`, **not** from the
detached HEAD source.

### What actually regressed
1. **Submodule pointer drift.** Whoever last touched the submodule checked out / merged
   Melvin's upstream `main` onto a detached HEAD, silently discarding the WSM3D
   `wsm3d/main` branch from the working tree. Any rebuild from the *current* checkout
   would compile a renderer with **no height-field at all** (back to flat instanced
   quads = "WorldBox native blocks"), and `WorldSphereMod/Code/Core.cs` would fail to
   compile because it calls `mgr.HeightField`, `mgr.UseHeightFieldTerrain`,
   `mgr.HeightField.MarkDirty()`, `hf.Configure(...)`, `hf.SetMaterial(...)` — members
   that only exist on `wsm3d/main`.
2. **Water + slope band-aids migrated into the main mod.** Because the fork's mesh path
   appeared "gone" from the checkout, water and extra smoothing were re-implemented as
   main-mod overlays (`Code/Water/WaterSurface.cs`, `Code/Terrain/TerrainSmoothing.cs`)
   instead of as part of the fork mesh. `WaterSurface.cs` even re-derives a
   corner-averaged depth/shore mesh — a duplicate of what `HeightFieldRenderer` already
   does — but as a *separate billboard GameObject* that floats above sand and clips at
   cube edges (the reported symptoms).

### Recovered known-good approach (what to restore + extend)
`wsm3d/main:CompoundSpheres/HeightFieldRenderer.cs` — for each tile **corner**, average
the height + biome color of the up-to-4 adjacent tiles, emit one unified `Mesh`
(`IndexFormat.UInt32`), compute **analytic normals from the height gradient**
(central differences: `tx=(2, dh/dx, 0)`, `tz=(0, dh/dz, 2)`, `n = cross(tx,tz)`),
plus a small Perlin micro-displacement (`freq 0.1`, `amp 0.3`) to break flat shading.
This *is* slope smoothing — done in mesh geometry, not as an overlay. It has a clean
callback boundary (`Configure(...)`) and a frame-coalesced dirty-rebuild throttle
(`QuietMs 0.2 / MaxStallMs 0.5 / MinIntervalMs 0.1`). It has **no water surface yet** —
that is the gap to fill in the fork.

---

## Current pipeline map (main mod -> fork)

### Fork (`External/Compound-Spheres/CompoundSpheres/`) — source of mesh
- `SphereManager.cs` (376 lines on HEAD; +274 on `wsm3d/main`) — `MonoBehaviour` owning
  the tile grid. Key members: `Mesh SphereTileMesh`, `Material Material`,
  GraphicsBuffers (`Matrixes/Colors/Scales/Textures`), `commandBuf` (indirect draw),
  `Vector3 SphereTilePosition(float X, float Y, float Height=0)` (delegates to a
  shape function), `void DrawTiles(int CameraX)`.
  - On `wsm3d/main` only: `bool UseHeightFieldTerrain`, `HeightFieldRenderer HeightField`
    (lazy), `FrustumCuller Culler`, `bool FrustumCullingEnabled`, `int LastCulledTiles`.
    `DrawTiles` branches: if `_useHeightField` -> `_heightField.RebuildAndDraw(CameraX, Min, Max, false)`,
    else frustum-culled per-row `RenderMeshIndirect`.
- `SphereRow.cs` — `DrawTiles()` => `Graphics.RenderMeshIndirect(_rp, mesh, commandBuf, 1)`
  (instanced quad path; bypassed when height-field is on).
- `SphereTile.cs`, `SphereManagerSettings.cs`, `BufferUtils.cs`, `DefaultSettings.cs`.
- `HeightFieldRenderer.cs` / `FrustumCuller.cs` — **`wsm3d/main` only.**
- `Default Assets/CompoundSphere.shader` — instanced shader (reads StructuredBuffers; not
  usable by a plain `DrawMesh`, hence the height-field needs its own vertex-color material).

### Main mod (`WorldSphereMod/`) — consumer
- `WorldSphereMod.csproj:129-130` references the **prebuilt** DLL:
  `<Reference Include="CompoundSpheres"><HintPath>WorldSphereMod\Assemblies\CompoundSpheres.dll</HintPath>`
  (NOT a project reference to fork source — so the DLL must be rebuilt + copied manually).
- `Code/Core.cs`:
  - `:630` calls `ConfigureHeightField(mgr, width, height)` during shape init.
  - `:904-906` on dirty tiles + `UseHeightFieldTerrain` => `Manager.HeightField.MarkDirty()`.
  - `:1055-1113` `ConfigureHeightField(...)` — the **port wiring**. Enables when
    `savedSettings.UseHeightFieldTerrain && CurrentShape==0`, then `hf.Configure(...)`
    with four adapters:
    - `sampleHeight(tx,ty)` -> `World.world.GetTileSimple(sx,sy).TileHeight()`
    - `sampleColor(tx,ty)`  -> `GetColor(tile.data.tile_id)`
    - `sampleTexture(tx,ty)`-> `WorldTileTexture(tile)`
    - `projectPosition(x,y,h)` -> `mgr.SphereTilePosition(x, y, h * HeightMult)`
    - then `hf.SetMaterial(new Material(Shader.Find("Sprites/Default")){color=white})`.
- `Code/CompoundSphereScripts.cs` — shape math + per-tile sampling:
  - `:33 SphereTileHeight(tile)` = `Tools.TrueHeight(tile.GetHeight(), render_z)` (+optional Perlin).
  - `:87 CartesianToFlat(mgr,X,Y,H)` = `new Vector3(X, H, Y + ZDisplacement)` (flat shape projection).
  - `:153 CartesianToCylindrical`, `:163` inverse — other shapes.
- `Code/TileMapToSphere.cs` — tile-grid -> sphere coordinate bridge.
- **Overlays to retire:**
  - `Code/Water/WaterSurface.cs` — billboard `MonoBehaviour`, builds its own
    corner-averaged depth/shore mesh + GerstnerWater material; floats above sand,
    clips at edges. Duplicates height-field logic.
  - `Code/Water/WaterRender.cs`, `Code/Water/WaterMaskBuffer.cs` — water lifecycle + mask.
  - `Code/Terrain/TerrainSmoothing.cs` — main-mod slope-quad overlay (gray clips at cube
    edges). Superseded by the fork height-field's intrinsic smoothing.

### Main-repo history of the band-aids (for reference)
`git log --oneline --all -- WorldSphereMod/Code/Terrain/TerrainSmoothing.cs` and
`.../Water/WaterSurface.cs` show the overlay era: `274955dc feat: height-field terrain
fork + ...` introduced the fork path; later commits (`b8d2c653`, `fcda3b05`,
`65928857 full-terrain smooth surface ... drop gray wash`, `c2d60975 slope mesh builds`)
fought the overlay symptoms. Water lineage runs `90d1b170 WaterMaskBuffer+WaterSurface`
-> `b3333fff 3-wave gerstner` -> `e0db1e9b Standard-fallback translucent` — all overlay,
none in the fork mesh.

---

## Decision — own terrain + water + slope in the fork via ports/adapters

Keep the fork as the **single source of surface geometry**. The main mod provides
*data* (height, color, texture, water-level/depth per tile) and a *projection*; the fork
emits *all* surface meshes (land + water) with smoothing baked into geometry. No
billboards in the main mod.

### Hexagonal boundary (already half-built — extend it)
The fork's domain port is the `Configure(...)` callback set. It is the seam: fork knows
nothing of WorldBox types; main mod adapts WorldBox -> primitives.

Proposed extended port on `HeightFieldRenderer` (additive, backward compatible):

```csharp
// New water adapters (nullable; when null -> no water surface emitted)
public void ConfigureWater(
    Func<int,int,bool>  sampleIsWater,    // (tx,ty) => tile is water
    Func<int,int,float> sampleWaterLevel, // (tx,ty) => absolute water surface height (world units)
    Func<int,int,float> sampleSeabed);    // (tx,ty) => terrain floor height under the water column
// Depth (for color/foam) is derived in-fork: depth = waterLevel - seabed, per corner.
```

Water surface is then a **second sub-mesh** in the same `RebuildAndDraw` pass:
corners averaged exactly like land, positioned at `projectPosition(x, y, waterLevel)`,
emitted only where any adjacent tile `IsWater`. Per-corner `color.g` carries shore flag
(corner touches a non-water tile) and `color.b` carries normalized depth — reusing the
scheme `WaterSurface.cs` already proved, but as fork geometry that sits *below sand*
because its height comes from `sampleWaterLevel`/`seabed`, not a fixed billboard plane.
Slope smoothing for both land and water is the existing corner-average + analytic-normal
math — nothing new.

### What changes
Fork (`External/Compound-Spheres/CompoundSpheres/`):
- `HeightFieldRenderer.cs` — add `ConfigureWater(...)`, a water sub-mesh build inside
  `Rebuild(...)`, and either a second submesh (`mesh.subMeshCount=2`) drawn with a water
  material, or a sibling `Mesh _waterMesh` drawn via a second `Graphics.DrawMesh`. Reuse
  the corner-average loop; gate water corners on `sampleIsWater`.
- `SphereManager.cs` — expose the water material slot (mirror `HeightField.SetMaterial`)
  e.g. `HeightField.SetWaterMaterial(mat)`.
- Optionally fold the Gerstner wave displacement into the **water material/shader** in
  `Default Assets/` (vertex animation in-shader, not CPU bob) so motion stays GPU-side.

Main mod (`WorldSphereMod/`):
- `Code/Core.cs ConfigureHeightField(...)` — after the existing `hf.Configure(...)`, add
  `hf.ConfigureWater(isWater, waterLevel, seabed)` reading WorldBox water data
  (`tile.Type.liquid` / sea-level constant), and `hf.SetWaterMaterial(...)` with the
  translucent water shader currently resolved in `WaterSurface.cs`.
- **Delete / disable** `Code/Water/WaterSurface.cs`, `WaterRender.cs`, `WaterMaskBuffer.cs`,
  `Code/Terrain/TerrainSmoothing.cs` and any Harmony patches that spawn/maintain them.
  Move the water shader-resolve helper into the Core wiring (or a small adapter) so the
  fork material gets a valid translucent shader.

### What the main mod stops doing
No `WaterSurface` GameObject, no `TerrainSmoothing` slope quads, no per-frame billboard
mesh rebuilds. The main mod becomes a pure data/adapter provider. This removes two
parallel corner-averaged-mesh implementations (DRY) and the band-aid layer entirely.

---

## Rebuild + ship workflow (the part that caused the drift)

1. **Re-attach the submodule to the WSM3D line first:**
   ```
   git -C External/Compound-Spheres checkout wsm3d/main
   # (current detached 73a7b77 is the stale Melvin merge — do NOT build from it)
   ```
   Then commit the submodule pointer bump in the main repo so it never silently reverts.
2. If Melvin's upstream `main` truly needs to be integrated, **merge upstream INTO
   `wsm3d/main`** (preserving the height-field), never the reverse, and never leave the
   submodule on a detached upstream HEAD.
3. Implement the water sub-mesh changes on `wsm3d/main`.
4. Build the fork: `dotnet build External/Compound-Spheres/CompoundSpheres/CompoundSpheres.csproj -c Release`.
5. Copy the output `CompoundSpheres.dll` -> `WorldSphereMod/Assemblies/CompoundSpheres.dll`
   (the main `WorldSphereMod.csproj:129-130` references this path by `HintPath`, so the
   copy is mandatory — there is no project reference). Mind the known stale-DLL trap:
   confirm the new DLL is actually loaded (`strings ... | grep ConfigureWater`).
6. Build the main mod; verify `Core.cs` compiles against the new DLL API.
7. **Vision-verify** (telemetry PASS != correct): screenshot in-game, read the PNG —
   water must sit at/below sand level as a depth-shaded translucent surface, slopes must
   be smooth with no gray edge clips and no billboard floating.

---

## Phased steps

- **P0 — Stabilize the submodule (no behavior change).**
  Checkout `wsm3d/main`, bump the main-repo submodule pointer, rebuild DLL from
  `wsm3d/main`, confirm parity with the shipped DLL (`strings` diff: both have
  `HeightFieldRenderer`/`PerlinNoise`). Establishes the known-good baseline = current
  shipped behavior. Commit so drift can't recur.

- **P1 — Water surface into the fork.**
  Add `ConfigureWater` + water sub-mesh + water material slot in `HeightFieldRenderer.cs`.
  Wire `ConfigureWater` in `Core.cs`. Keep `WaterSurface.cs` *disabled* behind a flag
  for A/B. Rebuild DLL, ship, vision-verify water height/depth.

- **P2 — Retire water overlays.**
  Delete `WaterSurface.cs` / `WaterRender.cs` / `WaterMaskBuffer.cs` and their Harmony
  patches. Move the translucent water shader resolution into the Core water wiring.

- **P3 — Retire terrain-smoothing overlay.**
  Confirm fork height-field smoothing covers all cases `TerrainSmoothing.cs` handled;
  delete `TerrainSmoothing.cs` + its patches. Vision-verify slopes.

- **P4 — Water shader polish (optional).**
  Move Gerstner wave displacement + shore foam fully into the water material/shader in
  `Default Assets/`; remove any residual CPU wave/bob code.

- **P5 — Traceability + guardrails.**
  Add a CI/check that fails if the submodule is on a detached non-`wsm3d/main` commit, or
  if the shipped DLL lacks expected exports. Trace P1-P4 to FR/NFR (Tracera) + an
  AgilePlus story.

---

## Concrete references
- Fork stale HEAD: `73a7b77` (`Merge branch 'main' of MelvinShwuaner/Compound-Spheres`).
- WSM3D terrain branch: `wsm3d/main` — commits `abb54ff`, `6e1cf94`, `ebe12a8`, `7213176`.
- Recovered renderer: `wsm3d/main:CompoundSpheres/HeightFieldRenderer.cs`
  (`Configure` L60-96, `RebuildAndDraw` L106-170, corner-average L188-300,
  analytic normals L312-340).
- Port wiring: `WorldSphereMod/Code/Core.cs:1055-1113` (`ConfigureHeightField`),
  `:630` (call), `:904-906` (`MarkDirty`).
- Per-tile sampling/shape: `WorldSphereMod/Code/CompoundSphereScripts.cs:33`, `:87`, `:153`.
- DLL reference: `WorldSphereMod.csproj:129-130` (HintPath to `Assemblies/CompoundSpheres.dll`).
- Overlays to retire: `WorldSphereMod/Code/Water/{WaterSurface,WaterRender,WaterMaskBuffer}.cs`,
  `WorldSphereMod/Code/Terrain/TerrainSmoothing.cs`.
- Shipped DLL (2026-05-29) confirmed to contain `HeightFieldRenderer`/`FrustumCuller`/
  `RebuildAndDraw`/`PerlinNoise`, and NO water symbols.
