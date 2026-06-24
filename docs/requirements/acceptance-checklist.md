# Acceptance Checklist (Vision Loop) — WorldSphereMod3D

> One runnable check per FR. The vision loop captures the **setup** frame(s) from
> the WorldBox window (`capture_target: worldbox_window` — reject desktop/stale
> captures) and applies the **PASS criteria** to the screenshot. Each entry is
> concrete enough that reading the frame gives an unambiguous PASS/FAIL. Telemetry
> values, where listed, are read from the bridge in the same run.
>
> **Capture how:** `pwsh Tools/wsm3d.ps1 install` → relaunch →
> `pwsh Tools/wsm3d.ps1 journey capture -Id us-wsm-phase-N-...` or
> `pwsh Tools/wsm3d.ps1 playcua run Tools/wsm3d-playcua/sample-scenarios/phase-N-*.yaml`.
> **Analyze how:** feed the named frame + the PASS prompt to the vision backend
> (OmniRoute combo / Anthropic). Record result + frame path in `traceability.md`.
>
> **Default-OFF phases:** toggle the phase ON via the in-game 3D Phases window (or
> `POST /settings/<key>?value=true`) and regenerate/reload the world before the
> capture frame.

---

## Epic 1 — Voxel Entities

- [ ] **FR-1.1 actor-not-billboard** — Capture a frame with **≥10 actors zoomed in**,
  camera tilted ~30–45° off top-down. **PASS** if each actor shows 3D depth
  (visible side faces / non-zero silhouette thickness) and they do NOT all appear
  as flat planes square-on to the camera. **FAIL** = cardboard cutouts / 2.5D slabs.
  *Telemetry:* `visible_units > 0`, `lastNonZeroDrawCalls > 0`.

- [ ] **FR-1.2 actor-color-and-light** — Same close-zoom frame. **PASS** if (a) no
  actor is magenta, (b) no actor is pure black, (c) per-actor colors match their
  sprite (e.g. a warrior reads as its sprite palette), and (d) a light/shadow
  gradient is visible across each model (lit face brighter than far face).
  **FAIL** = magenta, black, or flat full-bright wash-out. *Log:* `OpaqueVertexColor`
  resolved from bundle.

- [ ] **FR-1.3 items-projectiles-voxel** — Capture a frame with a dropped item on
  the ground AND a projectile in flight (e.g. an archer firing). **PASS** if both
  show voxel 3D depth, not 2D sprites.

## Epic 2 — Procedural Buildings  *(toggle `ProceduralBuildings` ON)*

- [ ] **FR-2.1 buildings-3d-architecture** — Capture a village with the camera
  tilted. **PASS** if houses show distinct wall planes + a pitched/hipped roof with
  depth; **FAIL** = flat extruded sprite, billboard, or wrong roofs (gable on a wall
  segment). *Telemetry:* `/phase/ProceduralBuildings.enabled==true`, building draws > 0.

- [ ] **FR-2.2 buildings-seated** — Slow 360° camera sweep around one building.
  **PASS** if the base meets terrain with no floating gap, no sinking, and no
  z-fight flicker across the sweep frames.

## Epic 3 — Crossed-Quad Foliage  *(toggle `CrossedQuadFoliage` ON)*

- [ ] **FR-3.1 foliage-3d-volume** — Capture a forest from an oblique angle. **PASS**
  if trees show crossed-quad X structure (or voxel volume) and retain thickness as
  the camera orbits; **FAIL** = single flat billboard that flips to face camera.

- [ ] **FR-3.2 foliage-wind-sway** — Capture **two frames ~1 s apart** of the same
  tree canopy. **PASS** if canopy vertices visibly shift between frames (sway).
  **FAIL** = identical static canopy. *Log:* `FoliageWind` resolved from bundle
  (currently expected FAIL — shader gated).

- [ ] **FR-3.3 walls-overlays-3d** — Capture a walled town with a road. **PASS** if
  walls show extruded height/depth and the road lies flat following terrain slope
  (not a billboard, not a floating decal).

## Epic 4 — Mesh Water  *(toggle `MeshWater` ON)*

- [ ] **FR-4.1 water-translucent-waves-depth** — Capture a coastline. **PASS** if
  (a) water is blue-tinted and **translucent** (shore/terrain visible through
  shallows), (b) **two frames ~1 s apart** show wave crests moving, (c) a depth
  gradient (shallow lighter than deep) is visible. **FAIL** = flat opaque slab,
  black water, or static surface. *Log:* `GerstnerWater` resolved, `_WaveTime` advances.

## Epic 5 — Lighting / Shadows / Sky  *(toggle `HighShadows`, `MountainSlopeSmoothing`, `HdrSkybox`)*

- [ ] **FR-5.1 sun-shadows** — Capture standing actors + buildings on open ground.
  **PASS** if each casts a directional ground shadow offset opposite the sun, with
  reasonably crisp (cascaded) edges. **FAIL** = no shadows or whole-map blocky aliasing.
  *Telemetry:* `QualitySettings.shadowCascades==4`.

- [ ] **FR-5.2 terrain-slope-smooth** — Capture a mountain/cliff transition. **PASS**
  if the surface interpolates smoothly between height levels with no visible
  stair-stepping/terracing and no z-fight flicker. **FAIL** = stepped billboard terraces.

- [ ] **FR-5.3 hdr-sky-ambient** — Capture the scene with `HdrSkybox` OFF then ON.
  **PASS** if ambient/reflection visibly changes (scene picks up sky lighting).
  **FAIL/NOT-STARTED** = no visible difference (no cubemap asset shipped).

## Epic 6 — Skeletal Animation  *(toggle `SkeletalAnimation` ON — note: force-disabled in Core.LoadSettings; clear the gate first)*

- [ ] **FR-6.1 correct-skeleton** — Capture a mix of creature types (humanoid +
  non-humanoid). **PASS** if a humanoid shows human-proportioned limbs (head, torso,
  2 arms, 2 legs) and creatures do NOT all share one bird/butterfly/insect rig; no
  limb stretched >1 body-length (dragonfly-bug guard). **FAIL** = uniform wrong rig
  or exploded limbs.

- [ ] **FR-6.2 walk-cycle** — Capture **two frames** of one walking humanoid. **PASS**
  if limb positions change (stride visible) between frames. **FAIL** = rigid sliding.

## Epic 7 — Worldspace UI  *(toggle `WorldspaceUI`, `WorldspaceLabel3D`)*

- [ ] **FR-7.1 health-bar-selection** — Select an actor, capture, then move the
  camera and capture again. **PASS** if a health bar renders above the actor's head
  and a selection ring at its feet, both legible at strategy zoom and tracking the
  actor across frames. **FAIL** = missing, sub-pixel speck, or screen-huge.

- [ ] **FR-7.2 name-label-readable** — Capture an actor with labels on. **PASS** if a
  camera-facing name label renders near the actor and is legible at strategy zoom.

## Epic 8 — Day/Night  *(toggle `DayNightCycle` ON)*

- [ ] **FR-8.1 day-night-cycle** — Capture **three frames** at morning, noon, evening.
  **PASS** if (a) the sun is at three different elevations, (b) sky color differs
  across the three, and critically (c) the sun is **below the horizon / low** in the
  evening frame (NOT clamped at zenith — `TimeOfDayToEuler` regression guard).
  **FAIL** = static sky, or sun stuck high after noon. *Telemetry:* `SunDriver.CurrentAngle` Δ > 0.01 rad/s.

## Epic 9 — PostFX / Particles  *(toggle `PostFX`, `ParticleEffects`)*

- [ ] **FR-9.1 postfx-not-black** — Capture the scene with PostFX ON. **PASS** if the
  scene renders normally (NON-BLACK), with subtle SSAO contact shadows and ACES tone
  (no blown highlights). **FAIL** = black screen / blown-out frame.

- [ ] **FR-9.2 particle-burst** — Trigger an explosion (e.g. meteor/fireball) and
  capture. **PASS** if a burst of voxel particles spawns, grows, and fades.

## Epic 10 — LOD / Impostors

- [ ] **FR-10.1 impostor-clean** — Capture a fully zoomed-out strategy view with many
  entities. **PASS** if distant entities render as impostor billboards that still
  show sprite-derived color (NOT solid white = `enableInstancing` silent-fail, NOT
  magenta). Then slow-zoom and capture a sequence: **PASS** if no hard pop/flicker at
  the LOD threshold. *Telemetry:* `impostorCacheHit > 0.99` zoomed out; voxel tier
  ≥80% close zoom.

---

## NFR spot-checks (screenshot-relevant)

- [ ] **NFR-STAB-2 no-overlay** — Any default gameplay frame: **PASS** if no console /
  profiler text overlay is rendered (unless `ProfilerDump` on).
- [ ] **NFR-PERF-1 fps** — Telemetry, not screenshot: `frameTimeMs ≤ 33` over 100
  frames at N=200 actors.

---

**21 FR checks + 2 NFR spot-checks. Every FR in `FR.md` has exactly one entry here
(satisfies NFR-VERIFY-1).** Update `traceability.md` status after each run.
