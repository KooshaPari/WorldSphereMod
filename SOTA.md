# State of the Art — WorldBox 3D modding

A scan of what already exists in the WorldBox-3D-rendering space, and what
this fork uniquely brings.

## Upstream — `MelvinShwuaner/WorldSphereMod`

The benchmark and the parent of this fork. Marketed as "the 3D Worldbox
mod". Genuinely innovative: ships a vendored `CompoundSpheres.dll`
(source at `MelvinShwuaner/Compound-Spheres`, Unity 2022.3) that turns the
flat WorldBox tilemap into a sphere/cylinder/torus mesh with per-tile
elevation, and adds a fly-camera + cylindrical X-wrap math layer
(`Tools.To3D`, `Tools.WrappedDist`).

**Limitations:** terrain is the only thing that is actually 3D. Every
entity is a `SpriteRenderer` rotated to face the camera (`QuantumSprites`,
`QuantumSpriteLibrary`). UI is flat Canvas. Water is per-tile colour.
Lighting is skybox + baked colour. Animation is frame-swap. No real shadows
beyond a flat-quad blob. No LOD path — hardware that fails the
compute-shader gate gets a red icon and nothing else.

## Other forks / alternatives

- **HaxeBox-style total ports.** Reimplementations of WorldBox-like games
  in different engines (Haxe, Godot) target different goals — usually
  simulation flexibility, not visual fidelity over the original sprite art.
  Out of scope: this fork stays *inside* WorldBox.
- **Other WorldBox visual mods.** Most ship reshade preset packs (post-FX
  only) or terrain texture replacements. None we've found rebuild the
  entity-rendering pipeline.
- **Vanilla WorldBox 2D.** The baseline — flat tilemap, no 3D at all.
  Reference point for the upstream-WorldSphereMod comparison only.

## What `WorldSphereMod3D` brings that nothing else does

1. **Voxelized sprite entities.** Sprites become cube meshes with greedy
   meshing. Pixel-art identity preserved; silhouette is correct from any
   camera angle. (Phase 1)
2. **Procedural building geometry.** Heuristic mesh generation from each
   `BuildingAsset`'s sprite — footprint extrusion, story inference, door/
   window detection, roof inference — driven by a public override API.
   (Phase 2)
3. **Crossed-quad foliage + wall prisms + surface overlays.** Free-3D
   trees/rocks/clouds + real 3D walls + textured surface overlays.
   (Phase 3a/3b)
4. **Mesh water with Gerstner waves.** Per-tile water mask drives a
   displaced mesh layer; shoreline foam from depth gradient. (Phase 4)
5. **Real directional sun + cascaded shadow maps.** Voxel actors cast real
   shadows; the old flat-quad `SpriteShadow` path is dropped when the
   shadow stack is active. (Phase 5)
6. **Auto-rigged skeletal animation.** 12-bone humanoid / 9-bone quadruped
   rigs assigned from sprite anatomy + driven by WorldBox's existing
   `AnimationFrameData`. GPU compute-skinning path with CPU fallback.
   (Phase 6)
7. **Worldspace UI.** Nameplates, HP bars, damage popups, selection rings
   in actual world space, not screen-space overlay. (Phase 7)
8. **Day/night driver + procedural sky + fog.** Color-temperature gradient
   tied to sun rotation; `MapBox.world_time` reflection probe with
   autonomous fallback. (Phase 8)
9. **Particle bursts + decal pools + URP PostFX.** Real volumetric-looking
   explosions; footprint/scorch/blood decals; opt-in PostFx volume.
   (Phase 9)
10. **LOD ladder + impostor billboard fallback.** Voxel → Proxy → Impostor
    tiers with hysteresis. Crucially, the impostor path is also the
    compatibility fallback for hardware that fails the compute-shader
    gate — upstream's failure mode (red icon, mod inert) becomes a
    degraded-but-functional sprite-billboard mode here. (Phase 10)

## What this fork explicitly is *not*

- Not a simulation mod. Zero gameplay changes; pure rendering + UI.
- Not a re-shader of the terrain. The CompoundSpheres backend is retained
  in v1; the submodule fork (`Compound-Spheres-3D`) is the upgrade path,
  not a rewrite.
- Not multiplayer-aware. Out of scope.

## Distribution channels

GitHub Releases + GameBanana. Steam Workshop is *not* a target until the
cause of upstream's Workshop removal is understood.
