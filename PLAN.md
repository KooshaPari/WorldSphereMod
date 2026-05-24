# WorldSphereMod → WorldSphereMod3D — Hard Fork Plan

## Context

**Why this exists.** `MelvinShwuaner/WorldSphereMod` is a NeoModLoader/Harmony-based mod for WorldBox marketed as "the 3D Worldbox mod." Investigation of the codebase + repos + Steam/GameBanana pages reveals the truth: **the terrain is the only thing that is truly 3D**. Everything else — actors, buildings, drops, items, projectiles, effects, talk bubbles, shadows, even Crabzilla and dragons — is still a 2D `SpriteRenderer`/`QuantumSprite` quad that is *positioned* in 3D space and rotated to face the camera. UI is flat Canvas overlay. Water is tile-color, clouds are sprite cards, foliage is sprite cards. Lighting is a skybox texture and per-tile color baking (no directional sun, no real-time shadows). Animation is frame-swap sprite arrays. The terrain backend itself is shipped as a **vendored, closed-built `CompoundSpheres.dll`** (sources exist at `MelvinShwuaner/Compound-Spheres`, Unity 2022.3, render-only, no per-vertex normals/lighting).

**What this plan delivers.** A hard fork (`WorldSphereMod3D`) that finishes the 3D conversion across every dimension the user approved: hybrid sprite→3D (voxelized actors, procedural buildings, crossed-quad foliage), open-source rebuilt terrain backend with per-vertex lighting, directional sun + cascaded shadow maps, animated mesh water, worldspace UI, bone-driven actor animation, mesh clouds/particles, plus polish (post-processing, day/night, decals, LOD/impostor fallback for compute-shader-less hardware).

**Why hybrid sprite→3D (user-chosen).** Voxelization gives free 3D for any sprite while preserving the pixel-art identity; procedural building meshes look better at scale than voxelized buildings; crossed-quads handle dense foliage cheaply. All three are mesh-based, so they share the lighting/shadow stack.

---

## Reference points already in the codebase (reuse, don't re-create)

| Need | Existing function | Location |
|---|---|---|
| 2D→3D world position | `Tools.To3D`, `Tools.To3DTileHeight` | `WorldSphereMod/Code/Tools.cs` |
| 3D→2D world position | `Tools.To2D` | `WorldSphereMod/Code/Tools.cs:199` |
| Tile height lookup | `Tools.TrueHeight`, `Tools.GetTileHeightSmooth` | `WorldSphereMod/Code/Tools.cs:311` |
| Camera-facing rotation | `Tools.RotateToCamera`, `Tools.RotateToCameraAtTile`, `Tools.GetCameraAngle` | `WorldSphereMod/Code/Tools.cs:460` |
| Upright/ground rotation | `Tools.GetUprightRotation`, `Tools.GetRotation` | `WorldSphereMod/Code/Tools.cs` |
| Mesh raycast pick | `Tools.IntersectMesh` | `WorldSphereMod/Code/Tools.cs:199` |
| Actor render pass | `SourcePatches.calculateactordata3D` | `WorldSphereMod/Code/QuantumSprites.cs:448` |
| Building render pass | `SourcePatches.calculatebuildindata3D` | `WorldSphereMod/Code/QuantumSprites.cs:609` |
| Effect data table | `Constants.EffectDatas` | `WorldSphereMod/Code/Constants.cs:20` |
| Per-asset orientation flags | `Constants.PerpActors/PerpBuildings/PerpProjectiles` | `WorldSphereMod/Code/Constants.cs:31` |
| Sprite atlas → pixel buffer | `Tools.PixelsFromSpriteAtlas` | `WorldSphereMod/Code/Tools.cs:119` |
| AssetBundle loader | `AssetBundleUtils.GetAssetBundle("worldsphere")` | `WorldSphereMod/Code/Core.cs:407` |
| Camera | `CameraManager.MainCamera`, `RotateCamera.Rotation` | `WorldSphereMod/Code/3DCamera.cs` |
| Sphere/flat shape config | `Core.Shapes`, `Core.CurrentShape` | `WorldSphereMod/Code/Core.cs` |
| Coordinate-conversion transpilers | `DimensionConverter.ConvertPositions/ConvertQuantum` | `WorldSphereMod/Code/DimensionConverter.cs` |
| Public API hooks | `WorldSphereMod.API.WorldSphereModAPI` | `WorldSphereMod/Code/WorldSphereAPI.cs` |
| External API shape | `WorldSphereAPI` (delegate-based, reflection-loaded) | `WorldSphereAPI/WorldSphereAPI.cs` |
| Texture array packing | `Core.CreateTextures` | `WorldSphereMod/Code/Core.cs:436` |
| Settings model | `SavedSettings` | `WorldSphereMod/Code/SavedSettings.cs` |
| Backend rendering tool | `SphereManager.Creator.CreateSphereManager` | `Compound-Spheres` (external repo) |

Every new system below **must hook through these** rather than duplicating math/coords/atlas/coordinate code.

---

## Phased Plan

### Phase 0 — Fork plumbing (1–2 days)

- Rename repo to `WorldSphereMod3D`. Keep the existing dev branch (`claude/research-ultraplan-fork-DdgI5`).
- `mod.json`: change `name` → `WorldSphereMod3D`, `GUID` → `worldsphere3d.melvinshwuaner.fork` (so it can be installed alongside upstream).
- Add `MelvinShwuaner/Compound-Spheres` as a fork (`Compound-Spheres-3D`) + git submodule under `External/Compound-Spheres-3D/`. Delete vendored `WorldSphereMod/Assemblies/CompoundSpheres.dll` and replace the `<Reference Include="CompoundSpheres">` in `WorldSphereMod.csproj` with a `<ProjectReference>` to the submodule's C# project. Confirm rebuild produces an equivalent DLL by binary-diffing the output against the vendored one on a known asset bundle.
- Rebuild AssetBundles (`WorldSphereMod/AssetBundles/{win,linux,osx}/worldsphere`) via a small Unity 2022.3 builder project under `External/AssetBundleBuilder/`. Add a `Tools/build-bundles.{sh,ps1}` shim and a GitHub Action that runs it headless.
- Add a `dotnet build` GitHub Action that targets the WorldBox install paths via a `Directory.Build.props` so contributors can point `WORLDBOX_PATH` at their own copy.

**Verify:** rebuild loads in WorldBox, terrain renders identically to upstream, settings tab opens.

---

### Phase 1 — Voxel sprite pipeline (1–2 weeks)

Goal: replace `QuantumSprite` billboards for **actors, items, drops, projectiles, talk bubbles, status icons** with voxelized meshes.

New module: `WorldSphereMod/Code/Voxel/`
- `SpriteVoxelizer.cs` — takes `Sprite` → `Mesh`. Each opaque texel becomes a unit cube; per-cube color baked into a vertex-color attribute. Greedy mesh on the X/Y plane (Z-thickness=1). For thicker silhouettes (large boss frames) allow a per-asset depth override.
- `VoxelMeshCache.cs` — `Dictionary<Sprite, Mesh>` with LRU eviction; keyed by `sprite.GetInstanceID()`. Survives world rebuilds.
- `VoxelInstanceBatcher.cs` — wraps `Graphics.DrawMeshInstanced` (or `DrawMeshInstancedIndirect` since the mod already gates on `SystemInfo.supportsIndirectArgumentsBuffer` in `Mod.cs:21`). One batch per (mesh, material) pair, up to 1023 instances per call.
- `VoxelMaterial.mat` + `VoxelLit.shader` — URP-compatible lit shader with vertex colors, normal generation from face direction, supports the directional shadow cascade from Phase 6.

Wire-up:
- Patch `QuantumSprites.cs:448` `calculateactordata3D`: when `Core.savedSettings.Is3D && !Constants.PerpActors.ContainsKey(asset.id)`, **skip the sprite path** and append to a new `VoxelRenderQueue` with `{mesh, matrix, color}`. Keep the existing path as fallback (asset opted out, or hardware-gate fail).
- New `LateUpdate` driver in `Core.cs` flushes the queue via `VoxelInstanceBatcher` after `MapBox.renderStuff`.
- Talk bubbles (`QuantumSprites.cs:61` `drawSocialize3D`) and arrows (`drawArrowQuantumSprite`) get voxelized variants too, but those keep the camera-billboard rotation because they're UI-adjacent.
- Items held by actors (the per-actor `mainSprite`/`secondarySprite` from `AnimationFrameData`) get voxelized lazily on first use; per-actor we instance one body mesh + up to two item meshes with the bone-driven matrices from Phase 7.
- Drops: patch `Drop.updatePosition` (already touched at `General.cs:224`) to route to the voxel renderer.
- Projectiles: extend `QuantumSpriteLibrary.drawProjectiles` transpiler to consult `Constants.PerpProjectiles` — voxel mesh aligned to velocity for arrows/bolts; voxel mesh aligned to gravity for boulders.

**Verify:** spawn a kingdom, sweep camera, confirm units cast voxel silhouettes from any angle, no z-fighting against terrain, 60 fps with 500 units on mid-range hardware. Profile: `Graphics.DrawMeshInstanced` count and cache hit-rate logged behind a `--voxel-stats` setting.

---

### Phase 2 — Procedural building meshes (2–3 weeks)

Goal: replace `BuildingManager.precalculateRenderDataParallel` sprite output with procgen meshes per asset.

New module: `WorldSphereMod/Code/ProcGen/`
- `BuildingMeshGen.cs` — Inspect each `BuildingAsset`'s top-down footprint + main sprite. Heuristic:
  1. **Footprint extrusion**: build a base prism from the sprite's silhouette (treat opaque alpha as floor plan after a top-down projection of the sprite).
  2. **Roof inference**: detect roof palette colors (warm reds/browns/wood/stone) clustered in upper rows; emit a hipped or gable roof above the prism using the dominant roof color.
  3. **Door/window detection**: find dark vertical rectangles in the lower half → emit door/window cutouts as a separate dark inset quad.
  4. Bake per-asset, cache in `ProcGenCache`, keyed by `BuildingAsset.id` + sprite version.
- `BuildingRules.cs` — public override file (JSON) so users / mod authors can ship `{ "house_human": { "roof": "gable", "stories": 2, "doors": [...]} }` overrides via `Locales`-style folder.

Wire-up: patch `BuildingManager.precalculateRenderDataParallel` to emit `{mesh, matrix, tintColor}` into `BuildingRenderQueue`; reuse `VoxelInstanceBatcher`'s API (rename → `MeshInstanceBatcher`).

**Verify:** every vanilla building asset has a generated mesh. Visual diff: place each asset once at the equator and once at a 70° latitude; confirm orientation/scale look right against `Constants.PerpBuildings`. Bench: 1000 buildings instanced, ≤ 5 ms per frame.

---

### Phase 3 — Foliage, clouds, decorations as crossed-quads (1–2 weeks)

Goal: every "top tile" decoration (trees, bushes, rocks, cacti) and every billboard cloud becomes two perpendicular quads — looks 3D from any angle, ~free perf.

- `CrossedQuadMesher.cs` builds a `Mesh` with two quads sharing the sprite texture at 90°.
- Patch the `TopTile` rendering path (currently feeds `QuantumSpriteLibrary.drawQuantumSprite` via library helpers; not yet patched in the mod) so each top tile pulls a crossed-quad mesh from a per-sprite cache and is drawn via `MeshInstanceBatcher`.
- Wind shader: vertex displacement based on world position + time + height-along-quad, parameterized by an asset tag (`tag_foliage` vs `tag_rock` — rocks get 0 displacement).
- Clouds: refactor `EffectData` `"fx_cloud"` (`Constants.cs:29`) — instead of a separated sprite renderer, emit a slow-drifting crossed-quad mesh with soft-particle blending. Cast no shadow (or a stylized fake shadow ground decal — see Phase 9).

**Verify:** generate a forest biome, fly the camera around it from 360°, leaves never disappear (no billboard pop), wind looks plausible. Profile: 5k trees ≤ 3 ms.

---

### Phase 4 — Mesh water (1–2 weeks)

Goal: water becomes a proper mesh layer in front of the terrain, not just a tile color.

- In the rebuilt `Compound-Spheres-3D`, extract the existing height/biome buffer; expose a **water mask** SSBO indicating which tiles are water and their depth (`sea_level - tile_height`).
- New mesh layer `WaterSurface` overlaid on the SphereManager: a copy of the terrain mesh, clipped to the water mask, displaced upward to sea level.
- `WaterShader.shader`: Gerstner waves (3 wave directions), depth-tint blend (shallow=teal, deep=navy), Fresnel cubemap reflection from the skybox material, shoreline foam from depth gradient.
- Patch `MapLayer` water tile color so the terrain stops painting blue under the water mesh (avoid double-blue).

**Verify:** spawn a small island, confirm waves animate, shoreline foam present, lakes look correct, no shimmer where rivers meet ocean.

---

### Phase 5 — Lighting + cascaded shadows (1–2 weeks)

Goal: a real sun, real shadow maps, normals on every mesh.

- Add a `DirectionalLight` `Sun` GameObject parented to `CameraManager.MainCamera`'s rig (not to the camera itself — independent yaw/pitch). Drive its rotation from a `TimeOfDay` float (Phase 8 hooks; default fixed at 11am for first ship).
- Configure URP shadow cascades (4 cascades, 50 m total range) — gated behind `SavedSettings.HighShadows`.
- Rebuild `Compound-Spheres-3D` to emit **per-vertex normals** on the terrain mesh (currently the vendored backend's terrain is unlit-shaded). Add a `_NormalStrength` material slider.
- Bake **screen-space ambient occlusion** via URP's SSAO renderer feature (cheap, no per-vertex AO bake needed).
- Replace the per-actor `SpriteShadow` flat quad pattern (`Effects.cs` `UpdateShadow`) with: voxel/mesh actors already cast real shadows from the directional light, so delete the shadow patch path entirely when `IsWorld3D && Sun.shadows != None`. Keep the flat-quad path only as a fallback for the crossed-quad foliage's contact shadow.

**Verify:** day-lit screenshot vs vanilla; visible shadow lengthening with time-of-day slider; no shadow acne, no peter-panning on actors at all latitudes.

---

### Phase 6 — Skeletal animation (2–3 weeks)

Goal: animate voxel/mesh actors via bones, not frame-swap sprite arrays.

WorldBox already supplies per-actor `AnimationFrameData` containing pose offsets, arm-swing, head/body/leg positions (`QuantumSprites.cs:498`-ish reads `tFrameData.size_unit` etc.). We map this to a minimal skeleton:

- `Rig/HumanoidRig.cs` — 12-bone skeleton (root, hips, spine, head, L/R upper-arm, L/R forearm, L/R upper-leg, L/R lower-leg). At voxelization time we **auto-segment** the sprite into regions (head row, torso rows, arms columns, legs rows) using palette + position heuristics and assign each voxel to a bone.
- `Rig/QuadrupedRig.cs` — 9 bones for wolves/horses/etc.
- `Rig/RigDriver.cs` — every frame, read `AnimationFrameData` from the existing actor pipeline, compute bone transforms, push as a `Matrix4x4[]` to a compute shader that skins the voxel mesh.
- Custom assets without a known rig fall back to **no skeleton** (use the static voxel mesh) and still render fine.
- Special bosses get hand-rigged once:
  - **Crabzilla**: `General.cs:313` already has a multi-sprite copy rig — we replace each copied SpriteRenderer with a voxel mesh child, keeping the existing `Manager` transform parent.
  - **Dragon**: similar treatment. Removes the `Dragon.create` Postfix that just disables the sprite renderer.

**Verify:** record vanilla 2D animation reference of a swordsman, a wolf, Crabzilla. Side-by-side at matched frames — silhouettes should track within ~1 voxel. Bench: 1000 skinned actors ≤ 4 ms.

---

### Phase 7 — Worldspace UI (1 week)

Goal: nameplates, health bars, selection rings, damage numbers exist in 3D space.

- New `Worldspace/WorldUIRenderer.cs` — one `Canvas` per UI element type, set to `RenderMode.WorldSpace`, parented to a follow-rig that mirrors actor positions.
- Port `NameplateText` (already 3D-position-patched at the screen-projection step) to TextMeshPro world canvas; add distance-based fade and a depth-soft fade where text would intersect terrain.
- Selection ring: thin torus mesh on the ground under the selected unit, scrolls with a dotted shader.
- Health bars: a small mesh quad with a 2-color shader, billboards to camera horizontally but stays vertical on Y.
- Damage numbers: 3D TMP popups using `MeshInstanceBatcher` for batching.

**Verify:** select 100 units, all rings visible at all zooms; nameplates legible up close and fade gracefully at distance.

---

### Phase 8 — Sky + atmosphere + day/night (1 week)

- Replace the `Skybox` material with a procedural sky (Hosek-Wilkie or simpler 3-color gradient with sun disc).
- Drive `TimeOfDay` from WorldBox in-game time (the game already simulates day/night for some plants — reuse `MapBox.world_time` if exposed; otherwise expose a slider in `WorldSphereTab.cs`).
- Linked to Phase 5's sun rotation. Color-temperature shift sun/ambient from dawn → noon → dusk → night.
- Fog: exponential height fog parameterized by `SavedSettings.FogDensity` (new field).

**Verify:** day/night cycle visible in screenshots, performance unchanged, fog blends with the skybox seam.

---

### Phase 9 — Particles, decals, post-processing (1 week)

- Convert critical sprite-billboard effects (`fx_meteorite`, `fx_explosion_wave`, `fx_fire_smoke`, `fx_antimatter_effect`, `fx_napalm_flash`) to small voxel meshes / particle bursts using URP VFX Graph if available, else legacy `ParticleSystem` with mesh-render mode.
- **Footprint decals** under units, **scorch decals** under explosions, **blood decals** in combat — pooled `DecalProjector`s.
- URP post-processing volume: bloom, color grading (subtle), vignette. Gated by `SavedSettings.PostFX`.

**Verify:** explosion looks volumetric, scorch decals persist, no FPS regression > 5%.

---

### Phase 10 — Performance, fallbacks, polish (3–5 days)

- LOD ladder per voxel/procgen mesh: voxel-mesh (near) → low-poly proxy (mid) → impostor billboard (far). The impostor path is also the **compatibility fallback** for hardware that fails the existing compute-shader gate in `Mod.cs:21`, so users without modern GPUs still get a 3D-positioned billboard mod equivalent to upstream.
- Frustum cull with the existing zone system; tighten the visible-units arrays already in use (`World.world.units.visible_units_socialize` etc.).
- Settings: extend `SavedSettings.cs` with `HighShadows`, `PostFX`, `FogDensity`, `WaterDetail`, `FoliageDensity`, `LODScale`. Extend `WorldSphereAPI.GetSetting<T>` (already reflection-based, picks these up automatically).
- Add `--profile-mode` console flag dumping `Time.deltaTime` breakdown per system once per second.

**Verify:** vanilla map, age 100, 5000 actors — sustained 60 fps on mid-range hardware (RTX 3060 / 5600X reference). Fallback path runs at 60 fps on Intel UHD 620 (sprite-billboard mode).

---

## Public API changes (`WorldSphereAPI` v2)

Backwards-compatible additions in `WorldSphereAPI/WorldSphereAPI.cs`:

- `RegisterCustomMesh(string assetId, Mesh mesh, Texture albedo)` — bypasses voxelization for a given asset.
- `RegisterBuildingRules(string assetId, BuildingRules rules)` — override procgen heuristics.
- `RegisterRig(string assetId, RigData rig)` — assign a custom skeleton/bone weights.
- `RegisterEffectMesh(string effectId, Mesh mesh)` — replace voxelized effect with author-supplied mesh.
- `event Action<float> OnTimeOfDayChanged` — for mods reacting to day/night.
- `bool IsModel3D { get; }` — true when meshes (Phase 1+) are active, distinct from `IsWorld3D` (terrain only).

Existing `IsWorld3D`, `MakeActorNonUpright`, `MakeBuildingNonUpright`, `MakeProjectileNonUpright`, `EditEffect`, `GetSetting<T>` are preserved with identical signatures.

---

## Critical files to modify

**Modify in-place:**
- `WorldSphereMod/Code/Core.cs` — register new batchers in `Init`/`PostInit`; load new bundles; light/sun setup.
- `WorldSphereMod/Code/QuantumSprites.cs` — route actor/building/projectile/talk-bubble draws into the new queues (Phase 1–3).
- `WorldSphereMod/Code/Effects.cs` — replace `SpriteShadow` path, route effects to mesh batchers (Phase 5, 9).
- `WorldSphereMod/Code/General.cs` — drop Crabzilla/Dragon hacks, replace with proper rigs (Phase 6); update `Drop.updatePosition` to route through mesh batcher.
- `WorldSphereMod/Code/3DCamera.cs` — parent the new `Sun` rig; expose `TimeOfDay`.
- `WorldSphereMod/Code/Constants.cs` — extend `EffectDatas` with new fields (e.g., `MeshOverride`); keep keys as-is for compat.
- `WorldSphereMod/Code/SavedSettings.cs` — new fields listed in Phase 10.
- `WorldSphereMod/Code/WorldSphereAPI.cs` — implement new API surface.
- `WorldSphereMod/Code/WorldSphereTab.cs` — UI toggles for new settings.
- `WorldSphereAPI/WorldSphereAPI.cs` — public delegate additions.
- `WorldSphereMod/mod.json` — new name/GUID/version.
- `WorldSphereMod.csproj` — swap vendored `CompoundSpheres` reference for the submodule `ProjectReference`.

**New files:**
- `WorldSphereMod/Code/Voxel/{SpriteVoxelizer,VoxelMeshCache,MeshInstanceBatcher,GreedyMesher}.cs`
- `WorldSphereMod/Code/ProcGen/{BuildingMeshGen,BuildingRules,ProcGenCache}.cs`
- `WorldSphereMod/Code/Foliage/{CrossedQuadMesher,WindSwayDriver}.cs`
- `WorldSphereMod/Code/Water/{WaterSurface,WaterMaskBuffer}.cs`
- `WorldSphereMod/Code/Rig/{HumanoidRig,QuadrupedRig,RigDriver,VoxelBoneAssigner,CrabzillaRig,DragonRig}.cs`
- `WorldSphereMod/Code/Worldspace/{WorldUIRenderer,NameplateWorld,SelectionRing,DamagePopup}.cs`
- `WorldSphereMod/Code/Lighting/{SunRig,TimeOfDay,ProceduralSky,SsaoConfig}.cs`
- `WorldSphereMod/Code/Effects/{DecalPool,ParticleMeshAdapter}.cs`
- `WorldSphereMod/Code/Perf/{LodSelector,ImpostorFallback,ProfilerDump}.cs`
- `WorldSphereMod/Resources/Shaders/{VoxelLit,WaterGerstner,FoliageWind,ProceduralSky,DecalDeferred}.shader`
- `External/Compound-Spheres-3D/` — submodule, hard fork of `MelvinShwuaner/Compound-Spheres`, with per-vertex normals patch and exposed water mask buffer.
- `External/AssetBundleBuilder/` — Unity 2022.3 project that builds the three platform bundles.
- `.github/workflows/{build.yml,bundles.yml}` — CI.

---

## Verification plan (end-to-end)

1. **Build**: `dotnet build WorldSphereMod.csproj -c Release` produces `WorldSphereMod3D.dll`; AssetBundle workflow produces `worldsphere` bundles for the 3 platforms.
2. **Install**: drop folder into `%APPDATA%/NeoModLoader/Mods/WorldSphereMod3D/` alongside upstream; confirm both can coexist (different GUIDs).
3. **Smoke test per phase**: each phase ships behind a `SavedSettings` flag (default on once the phase is verified). Generate a small map, screenshot from 6 camera angles, diff against the previous phase.
4. **Reference scenes**: include 4 deterministic-seed worlds under `Testing/Scenes/`:
   - `small_kingdom_500` (perf baseline for actors)
   - `forest_5k` (foliage perf)
   - `coastal_water` (water/shore)
   - `crabzilla_boss` (skeletal special-case)
   Document expected FPS for each on a reference rig in `Testing/PERFORMANCE.md`.
5. **API regression**: external test mod under `WorldSphereTester/` that uses every public API call; ensure all upstream calls still work and new calls work as documented.
6. **Hardware fallback**: simulate the compute-shader gate failure (force `Mod.cs:21` to throw) and confirm the impostor-billboard LOD path still renders correctly.
7. **Visual side-by-side**: PR checklist requires before/after screenshots for any rendering-affecting change.

---

## Risks & open items

- **WorldBox updates** can break Harmony patches at any time; the existing patch surface is already wide (~80+ patches). Mitigation: lock dev to a specific WorldBox build, document it in `README`, and add a startup check that warns if signatures don't match.
- **Voxel memory cost**: 1000 unique sprites × ~30 KB mesh each = ~30 MB GPU. Mitigated by mesh cache LRU + dedup on identical sprite hashes.
- **Skeleton coverage**: actor assets without a matching rig fall back to static voxel mesh — acceptable for first ship; rigs can be added incrementally.
- **Steam Workshop removal**: upstream's workshop page is flagged as removed for guideline violation (per Steam page check). Plan to ship via GameBanana + GitHub releases only; do not re-list on Workshop until cause is known.
- **Backend submodule**: `MelvinShwuaner/Compound-Spheres` has 0 stars / 0 forks, may be incomplete. Fallback: keep the original vendored DLL working as a build-time option if our submodule build fails to match feature parity.

---

## Deliverable

A self-contained hard fork on branch `claude/research-ultraplan-fork-DdgI5` (already created) that, when merged phase by phase, produces a true-3D WorldBox experience: real meshes for every entity, real lighting and shadows, real water, real animation, real worldspace UI — installable side-by-side with the original mod and gracefully degrading to billboards on incompatible hardware.
