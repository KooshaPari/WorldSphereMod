# Functional Requirements — WorldSphereMod3D

> **Purpose.** This is the **user-facing outcome spine**. It exists because the
> repo has machine-validation requirements (`PRD.md` FR-WSM-NNN: `/phase/<name>`
> returns `enabled=true patches>=1`) that say "LANDED" while the *visible*
> result is broken. A `/phase` endpoint echoing `enabled=true` proves a Harmony
> patch was registered — it does **not** prove the player sees a textured, lit,
> non-billboard voxel actor. Every FR here is gated on what a **screenshot or a
> runtime telemetry value** shows, not on what code path exists.
>
> **Verification precedence (overrides PRD EP-9 for this spine).** A visual FR is
> only `PROVEN-PASS` when a screenshot captured *from the WorldBox window* (not
> the desktop, not a stale artifact) shows the outcome AND any cited telemetry
> value backs it. Code presence + `/phase` echo = `CODE-LANDED-UNVERIFIED`, never
> PASS. See `traceability.md` for current per-FR status and `acceptance-checklist.md`
> for the exact screenshot test each FR's vision loop runs.
>
> **Structure (AgilePlus).** Epics = phases. Stories = FRs (`FR-N.x`). Each Story
> has a player-voice statement + concrete, machine-or-screenshot-verifiable ACs.
>
> **ID scheme.** `FR-N.x` where `N` = phase number (Epic), `x` = story within the
> phase. These are the *outcome* IDs; they map back to PRD `FR-WSM-NNN`
> *mechanism* IDs in `traceability.md`.

---

## Epic 1 — Voxel Entities (Phase 1, `VoxelEntities`, default ON)

### FR-1.1 — Actors render as 3D voxel models, not billboards
**As a player,** when I zoom in on an actor, it is a solid 3D voxel model with
visible depth — not a flat sprite that rotates to face the camera.

**Acceptance criteria:**
- AC-1.1.a (screenshot): With ≥10 actors in view at close zoom, each actor shows
  **side faces / silhouette depth** when the camera orbits ~30°+ off-axis. The
  actor does **not** stay flat-facing the camera (no cardboard-cutout / 2.5D slab).
- AC-1.1.b (telemetry): `/telemetry` reports `visible_units > 0` and
  `lastNonZeroDrawCalls > 0` with actors on screen.
- AC-1.1.c (telemetry): `/voxel/sprite?name=<walk frame>` returns
  `vertexCount > 0`, `triangleCount > 0`, and a Z-extent (depth) > 1 voxel — a
  flat slab (depth == 1) fails.

### FR-1.2 — Voxel actors use sprite-derived color and are lit (not neon/magenta/black)
**As a player,** voxel actors are colored from their original sprite and respond
to scene lighting — never solid magenta (missing shader), neon-emissive
wash-out, or pure black.

**Acceptance criteria:**
- AC-1.2.a (screenshot): No actor is magenta (`Hidden/InternalErrorShader`), no
  actor is pure black, and per-vertex colors visibly match the source sprite
  palette (e.g. a green-clad warrior is green).
- AC-1.2.b (screenshot): A light gradient is visible across each model — the lit
  face is brighter than the shadowed face. Flat full-bright (emission wash-out
  from the `Standard`+emission=1.5 fallback) fails.
- AC-1.2.c (log): `Player.log` shows `OpaqueVertexColor` resolved from the
  `wsm3d-shaders` bundle (not the `Standard` fallback) for the actor material.

### FR-1.3 — Items, drops, and projectiles render as voxels
**As a player,** dropped items and in-flight projectiles are voxel meshes too,
consistent with actors.

**Acceptance criteria:**
- AC-1.3.a (screenshot): A dropped item on the ground shows 3D voxel depth.
- AC-1.3.b (screenshot): A projectile mid-flight is a voxel mesh, not a 2D streak.

---

## Epic 2 — Procedural Buildings (Phase 2, `ProceduralBuildings`, default OFF)

### FR-2.1 — Buildings render as procedural 3D architecture
**As a player,** when I enable Procedural Buildings, structures have real 3D
geometry (walls, roofs with pitch) — not a voxelized flat sprite and not a 2D
billboard.

**Acceptance criteria:**
- AC-2.1.a (screenshot): A house shows distinct wall planes and a pitched/hipped
  roof with visible depth when the camera tilts. A flat extruded sprite fails.
- AC-2.1.b (telemetry): `/telemetry` building draw count > 0 with buildings in
  view; `/phase/ProceduralBuildings` returns `enabled=true`.
- AC-2.1.c (screenshot): No false-positive roof artifacts (gables on a wall
  segment, inverted hips) on the vanilla `BuildingAsset` set.

### FR-2.2 — Buildings sit on terrain without z-fighting or floating
**As a player,** building bases meet the ground cleanly.

**Acceptance criteria:**
- AC-2.2.a (screenshot): No building floats above terrain or sinks into it; no
  flicker (z-fight) at the base seam during a 360° camera sweep.

---

## Epic 3 — Crossed-Quad / Voxel Foliage (Phase 3, `CrossedQuadFoliage`, default OFF)

### FR-3.1 — Trees and bushes render as 3D crossed-quad / voxel foliage
**As a player,** trees and bushes have volume from any angle — not a single 2D
billboard that flips to face me.

**Acceptance criteria:**
- AC-3.1.a (screenshot): A tree shows two intersecting quads (or voxel volume)
  forming an X when viewed from above/oblique — it keeps apparent thickness as
  the camera orbits.
- AC-3.1.b (telemetry): `/phase/CrossedQuadFoliage` `enabled=true`; foliage draw
  calls > 0 with foliage in view.

### FR-3.2 — Foliage sways with wind
**As a player,** tree canopies animate with a subtle wind sway.

**Acceptance criteria:**
- AC-3.2.a (screenshot pair): Two frames ~1s apart show canopy vertices displaced
  (visible sway). Static foliage fails.
- AC-3.2.b (log): `FoliageWind` shader resolved from bundle (not the
  `Sprites/Default` static fallback). **Currently a known gap — see traceability.**

### FR-3.3 — Walls and surface overlays render as 3D
**As a player,** walls are extruded prisms and roads/paths are ground-conforming
overlays, not flat tile recolors.

**Acceptance criteria:**
- AC-3.3.a (screenshot): A wall segment shows extruded height/depth; a road lies
  flat on and follows terrain slope.

---

## Epic 4 — Mesh Water (Phase 4, `MeshWater`, default OFF)

### FR-4.1 — Water is a translucent animated mesh with waves and depth
**As a player,** water is a 3D surface with moving waves and see-through depth —
not a flat opaque blue billboard, and never black.

**Acceptance criteria:**
- AC-4.1.a (screenshot): Water surface is **translucent** (terrain/shore visible
  through shallow water) and **blue-tinted** — never black, never flat opaque.
- AC-4.1.b (screenshot pair): Gerstner wave crests visibly move between two frames
  ~1s apart (non-zero wave amplitude).
- AC-4.1.c (screenshot): Depth gradient — shallow water near shore is lighter/clearer
  than deep water.
- AC-4.1.d (log): `GerstnerWater` resolved from bundle; `_WaveTime` uniform advances.

---

## Epic 5 — Lighting, Shadows, Sky (Phase 5, `HighShadows`/`HdrSkybox`/`ColorGradingLut`, default OFF)

### FR-5.1 — Directional sun casts cascaded shadows on voxel entities
**As a player,** actors and buildings cast directional shadows onto the terrain.

**Acceptance criteria:**
- AC-5.1.a (screenshot): Each standing actor/building casts a visible shadow on
  the ground, offset opposite the sun direction.
- AC-5.1.b (screenshot): Shadow edges are reasonably crisp (cascade mapping), not
  blocky/aliased across the whole map; `QualitySettings.shadowCascades == 4`.

### FR-5.2 — Terrain slope smoothing (smooth, not stepped)
**As a player,** mountain and cliff transitions are smooth slopes, not hard
stair-step billboard terraces.

**Acceptance criteria:**
- AC-5.2.a (screenshot): At a height transition, the surface interpolates smoothly
  between levels — no visible stair-stepping / terracing at cliff edges.
- AC-5.2.b (screenshot): No z-fighting flicker between the smoothing overlay mesh
  and the base terrain.

### FR-5.3 — HDR sky / ambient lighting
**As a player,** the sky and ambient lighting look like a real outdoor scene.

**Acceptance criteria:**
- AC-5.3.a (screenshot): Ambient lighting visibly changes when `HdrSkybox` is
  toggled (scene picks up sky-derived ambient/reflection). If no cubemap ships,
  this FR is **NOT-STARTED** until an asset is provided — see traceability.

---

## Epic 6 — Skeletal Animation (Phase 6, `SkeletalAnimation`, default OFF)

### FR-6.1 — Creatures use the correct skeleton for their species
**As a player,** a humanoid animates like a humanoid, a bird like a bird — every
creature does **not** get the same (bird/butterfly) rig.

**Acceptance criteria:**
- AC-6.1.a (screenshot): A humanoid actor shows limbs in human proportion
  (head/torso/two arms/two legs), not insectoid wings. Distinct rigs map to
  distinct creature classes.
- AC-6.1.b (screenshot): No limb is displaced more than ~1 body-length from the
  body (the "dragonfly stretch" regression must not recur).

### FR-6.2 — Walking actors show a walk cycle
**As a player,** a moving actor visibly animates its limbs.

**Acceptance criteria:**
- AC-6.2.a (screenshot pair): Two frames of a walking humanoid show limb
  positions changing (stride). A rigid sliding model fails.

---

## Epic 7 — Worldspace UI (Phase 7, `WorldspaceUI`/`WorldspaceLabel3D`, default OFF)

### FR-7.1 — Health bars and selection render in 3D worldspace
**As a player,** a selected actor shows a health bar and selection ring anchored
above/around it in the world, scaled readably at strategy zoom.

**Acceptance criteria:**
- AC-7.1.a (screenshot): Selecting an actor shows a health bar above its head and
  a ring at its feet; both track the actor as the camera moves.
- AC-7.1.b (screenshot): The health bar is **legible** at strategy zoom (not a
  sub-pixel speck, not screen-huge).

### FR-7.2 — 3D name labels face the camera and stay readable
**As a player,** actor name labels are visible in worldspace and always readable.

**Acceptance criteria:**
- AC-7.2.a (screenshot): A name label renders near the actor, camera-facing, and
  legible at strategy zoom.

---

## Epic 8 — Day/Night Cycle (Phase 8, `DayNightCycle`, default OFF)

### FR-8.1 — Sky and sun cycle from dawn to noon to dusk
**As a player,** time of day progresses: the sun arcs across the sky and sky
color shifts through the day.

**Acceptance criteria:**
- AC-8.1.a (screenshot triple): Three captures (morning/noon/evening) show the
  sun at different elevations AND distinct sky colors.
- AC-8.1.b (correctness): Sun elevation is monotonic across a full cycle — it must
  set below the horizon in the evening, not clamp at zenith (regression guard for
  the `TimeOfDayToEuler` `[-90,90]` clamp bug).
- AC-8.1.c (telemetry): `SunDriver.CurrentAngle` changes > 0.01 rad/s during play.

---

## Epic 9 — PostFX & Particles (Phase 9, `PostFX`/`SSAO`/`ParticleEffects`, default OFF)

### FR-9.1 — PostFX (SSAO + tonemap) applies without blacking out the screen
**As a player,** enabling PostFX adds contact shadows and filmic tone — it must
**never** produce a black screen.

**Acceptance criteria:**
- AC-9.1.a (screenshot): With PostFX on, the scene renders (non-black); ACES tone
  is applied (no blown-out highlights).
- AC-9.1.b (screenshot): SSAO darkens crevices/contact points subtly; the rest of
  the image is unchanged in brightness.

### FR-9.2 — Voxel particle bursts on events
**As a player,** explosions/blood/fire spawn voxel-mesh particle bursts.

**Acceptance criteria:**
- AC-9.2.a (screenshot): An explosion produces a visible burst of voxel particles
  that grow then fade.

---

## Epic 10 — LOD & Impostors (Phase 10, LOD tuning, default ON-ish)

### FR-10.1 — Distant entities fall back to impostors without popping artifacts
**As a player,** zooming out keeps a smooth strategy view — distant actors become
impostors with no magenta, no flicker, no solid-white tiles.

**Acceptance criteria:**
- AC-10.1.a (screenshot): At strategy zoom-out, distant actors render as
  impostor billboards that still show sprite-derived color (not solid white,
  which indicates `enableInstancing` silent-fail).
- AC-10.1.b (telemetry): `/telemetry.impostorCacheHit > 0.99` when zoomed out;
  voxel tier active for ≥80% of entities at close zoom.
- AC-10.1.c (screenshot): No hard pop / flicker when an entity crosses the LOD
  threshold during a slow zoom.

---

## Coverage map

| Epic | Phase | FRs | User-cited symptom this gates |
|---|---|---|---|
| 1 | VoxelEntities | FR-1.1, 1.2, 1.3 | billboard actors, neon/magenta, flat slabs |
| 2 | ProceduralBuildings | FR-2.1, 2.2 | 2D buildings |
| 3 | CrossedQuadFoliage | FR-3.1, 3.2, 3.3 | flat-billboard trees, no wind |
| 4 | MeshWater | FR-4.1 | flat/black water, no waves/depth |
| 5 | HighShadows/Sky/Slope | FR-5.1, 5.2, 5.3 | no shadows, stepped terrain |
| 6 | SkeletalAnimation | FR-6.1, 6.2 | butterfly/bird rig on everything |
| 7 | WorldspaceUI | FR-7.1, 7.2 | missing/illegible worldspace UI |
| 8 | DayNightCycle | FR-8.1 | static sky, sun clamp |
| 9 | PostFX/Particles | FR-9.1, 9.2 | black-screen PostFX |
| 10 | LOD/Impostor | FR-10.1 | white/magenta impostors, popping |

**Total: 21 Functional Requirements across 10 Epics.**
