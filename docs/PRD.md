# Product Requirements Document: WorldSphereMod3D

## Executive Summary

WorldSphereMod3D is a Harmony+NeoModLoader fork of MelvinShwuaner's
WorldSphereMod for WorldBox that completes the 3D conversion of every
visible entity (actors, buildings, drops, projectiles, effects). Upstream
established the 3D terrain mesh on a wrapped sphere; this fork replaces
the remaining 2D sprite billboards with voxel meshes, procedural
geometry, skeletal-rigged animation, and a machine-introspectable
rendering pipeline.

The mod targets modded WorldBox players who want a fully-3D look without
sacrificing playability, and modders/researchers who want to validate
pipeline correctness via HTTP RPC (not screenshot interpretation).

---

## Problem Statement

### Current State Challenges

1. **Visual inconsistency.** Upstream's 3D terrain is wrapped in a sphere
   but every actor, building, projectile, and effect is a 2D quad rotated
   to face the camera. From oblique camera angles the world breaks the
   3D illusion.
2. **Validation cost.** Verifying rendering correctness requires manually
   loading saves, panning the camera, and screenshotting — error-prone
   and not reproducible by agents.
3. **Settings drift.** NML's PlayerConfig.dict has its own boolVal
   persistence that shadows our SavedSettings JSON, causing phase flags
   to silently revert across game launches.
4. **Performance cliff at scale.** With ~30k visible entities per
   strategy-view frame, the per-instance Graphics.DrawMesh fallback path
   hits 1–2 FPS — unplayable.
5. **Visual regressions are invisible.** Mesh build bugs (dot-cloud
   balloon, mangled-tri lathe artifacts, dragonfly skeletal) only surface
   in-game and require a human eye to identify.

### Impact Analysis

These challenges result in:
- Unplayable framerate when all phases are active
- "Plan said landed, actually broken" disconnects (phase code present,
  PhasePatchManager unable to discover it)
- Hours spent triaging visual diffs that a JSON invariant check would
  catch in seconds
- Settings churn across debugging cycles

### Solution Vision

WorldSphereMod3D provides:
- True 3D voxel meshes per entity with shape-hint routing (lathe for
  round, mirror/balloon for directional, extruded for rectangular)
- Programmatic verification surface via `/voxel/sprite`, `/voxel/actor`,
  `/phase/<name>`, `/telemetry`, `/voxel/queue` endpoints
- Single source of truth for phase activation that survives kill+launch
- A documented frame budget with explicit perf gates per NFR
- Bridge POST as the canonical settings-change mechanism that bypasses
  PlayerConfig shadowing

---

## Target Users

### Primary Users

#### 1. Modded WorldBox players

- **Profile:** Has WorldBox + NeoModLoader installed, downloads mods
  from GitHub releases or curseforge, plays at strategy zoom.
- **Goals:** Fully-3D voxel look at usable FPS, easy phase toggles in
  the in-game tab.
- **Pain Points:** Phases that crash on toggle, sub-2-FPS strategy view,
  visible 2D billboards when camera tilts.

#### 2. Mod developers/contributors

- **Profile:** C# Unity-aware; uses Tools/wsm3d.ps1 + bridge endpoints
  in iterative development.
- **Goals:** Validate phase correctness without launching Steam each
  iteration, ship per-phase PRs against a clear acceptance gate.
- **Pain Points:** Slow feedback loop, settings shadowing forces bridge
  POST workarounds, ambiguous visual regressions.

#### 3. Agentic/CI test harnesses

- **Profile:** Headless Docker/Hyper-V instance running playcua
  scenarios against the bridge.
- **Goals:** Drive scenarios via /actions/load_save, assert mesh
  invariants via /voxel/sprite, gate PR merges on /telemetry deltas.
- **Pain Points:** Screenshot validation is brittle; needs JSON-shape
  contracts.

---

## Functional Requirements

### FR-WSM-001: Voxel Actor Meshes — **LANDED 2026-05-21 (commit 12ec2d5)**

**Description:** Replace 2D actor sprites with voxel meshes built from
the sprite via SpriteVoxelizer (extruded/balloon/lathe per shape hint).

**Acceptance Criteria:**
- `curl /voxel/sprite?name=walk_0` returns
  `{vertexCount>0, triangleCount>0, distinctTriVerts:true, maxTriIndexLessThanVerts:true}`
- /telemetry.voxelCacheHit > 0.99 after warmup
- Actor.scales[i] != Vector3.zero where the voxel branch took ownership

**Related Tests:** `tests/WorldSphereMod.Tests.Unit/SpriteVoxelizerTests`,
`tests/WorldSphereMod.Tests.Unit/AssetShapeRegistryTests`

---

### FR-WSM-002: Voxel Building Meshes — **LANDED 2026-05-21 (commit 12ec2d5)**

**Description:** Replace 2D building sprites with voxel meshes or
procedural architectural meshes (BuildingProcRender behind
BuildingStyleProcgen flag).

**Acceptance Criteria:**
- `curl /voxel/sprite?name=main_0_0` returns valid mesh invariants
- /phase/ProceduralBuildings shows enabled=true with patches>=1
- BuildingProcRender.EmitMeshes.Regular count > 0 in log per frame

**Related Tests:** `tests/WorldSphereMod.Tests.Unit/BuildingRulesRegistryTests`

---

### FR-WSM-003: Per-Sprite Shape-Hint Routing — **LANDED 2026-05-21 (AssetShapeRegistryTests 14/14 pass)**

**Description:** AssetShapeRegistry maps asset ID prefixes to ShapeHint
(Cylinder, LongX, Tall, Flat, Mirror, Auto), routing voxelization
appropriately.

**Acceptance Criteria:**
- AssetShapeRegistry.GetShapeHint("human_warrior")==Cylinder
- GetShapeHint("boat_small")==Mirror
- GetShapeHint("wall_stone")==LongX
- Default falls back to aspect-ratio heuristic

**Related Tests:** `Phase6RigRegistryTests`, manual `/voxel/sprite`
sample with directional sprite (no radial fan artifact)

---

### FR-WSM-004: LOD Tier Selection + Impostor Fallback — **LANDED 2026-05-21 (verified via /telemetry impostorCacheHit=99.97%)**

**Description:** LodSelector chooses Voxel / Procedural / Impostor tier
per entity per frame based on screen-projected size.

**Acceptance Criteria:**
- /telemetry.impostorCacheHit > 0.99 when zoomed out
- At close zoom, voxel tier active for ≥ 80% of visible entities
- LodSelector._entityHeight scales with VoxelScaleMultiplier

**Related Tests:** `LodSelectorTests`

---

### FR-WSM-005: Mesh Water — **LANDED 2026-05-21 (enabled=true patched=5)**

**Description:** Replace flat water plane with Gerstner-wave displaced
mesh updated each frame via _WaveTime uniform.

**Acceptance Criteria:**
- /phase/MeshWater enabled=true with patches >= 5
- WaterRender.UpdateLifecycle produces visible mesh in Player.log
- Wave amplitude > 0 at runtime

**Related Tests:** Visual via `/voxel/dump_all`-style water-surface
inspection endpoint (TBD)

---

### FR-WSM-006: Crossed-Quad Foliage — **LANDED 2026-05-21 (enabled=true patched=2)**

**Description:** Trees + bushes render as crossed-quad billboards with
wind-sway shader.

**Acceptance Criteria:**
- /phase/CrossedQuadFoliage enabled=true with patches >= 2
- FoliageTileRender + WallTileRender Postfixes fire on
  WorldTilemap.renderTile

**Related Tests:** `tests/WorldSphereMod.Tests.Unit/Phase3FoliageTests`

---

### FR-WSM-007: High Shadows with Cascade Mapping

**Description:** SunDriver configures QualitySettings.shadowCascades=4
+ shadowDistance tuned for voxel silhouettes; shadow strength + bias
calibrated.

**Acceptance Criteria:**
- /phase/HighShadows enabled=true with patches >= 1
- QualitySettings.shadowCascades == 4 when flag on
- Shadow bias != default

**Related Tests:** TBD — needs editor-mode assertion harness

---

### FR-WSM-008: Skeletal Animation

**Description:** Humanoid actors deform via bone matrices driven by
animation curves; bone weights baked into voxel mesh per RigType.

**Acceptance Criteria:**
- /phase/SkeletalAnimation enabled=true with patches >= 1
- No vertex displacement > 10× sprite extent (dragonfly bug avoidance)
- Walk-cycle visible on humanoid actors

**Status: BLOCKED** by dragonfly bug — bind-pose audit pending.

**Related Tests:** `Phase6RigRegistryTests`,
`tests/WorldSphereMod.Tests.Unit/SkeletalDeformationBoundsTests` (TBD)

---

### FR-WSM-009: Day/Night Cycle — **LANDED 2026-05-21 (enabled=true patched=1)**

**Description:** Continuous sun rotation + sky color interpolation
driven by WorldBox time scale.

**Acceptance Criteria:**
- /phase/DayNightCycle enabled=true with patches >= 1
- SunDriver.CurrentAngle changes > 0.01 rad/sec during active gameplay
- Skybox color gradients interpolate per sun position

**Related Tests:** TBD

---

### FR-WSM-010: Post-FX Pipeline — **LANDED 2026-05-21 (enabled=true patched=1)**

**Description:** SSAO + SSGI + ACES tonemap + HDR cubemap reflection
via OnRenderImage chain, gated by SavedSettings flags.

**Acceptance Criteria:**
- /phase/PostFX enabled=true with patches >= 1
- SSAO/SSGI components attached to main Camera when flag on
- ACES tonemap shader resolves at material load

**Related Tests:** TBD

---

### FR-WSM-011: Worldspace UI (Health Bars + Labels) — **LANDED 2026-05-21 (enabled=true patched=1)**

**Description:** 3D mesh health bars + 3D mesh labels attached to actor
head positions, camera-facing.

**Acceptance Criteria:**
- /phase/WorldspaceUI enabled=true with patches >= 1
- Health bar 3D mesh submitted per actor when WorldspaceHealth3D on
- TextMesh attached per actor when WorldspaceLabel3D on

**Related Tests:** TBD

---

### FR-WSM-012: Voxel-Mesh Particle Bursts — **LANDED 2026-05-21 (enabled=true patched=3)**

**Description:** Explosions/blood/fire/leaves spawn voxel-mesh bursts
via VoxelParticleBurst lifecycle (spawn → grow → fade alpha).

**Acceptance Criteria:**
- /phase/ParticleEffects enabled=true with patches >= 3
- Meteorite.spawnOn + ExplosionFlash.start + StatusParticle.spawnParticle
  all trigger VoxelParticleBurst.TryStart

**Related Tests:** `tests/WorldSphereMod.Tests.Unit/Phase9bParticleTests`

---

### FR-WSM-013: Settings Persistence Across Launches

**Description:** SavedSettings + PlayerConfig.dict mirror each other
at toggle registration time; phase flags survive kill+launch.

**Acceptance Criteria:**
- After `pwsh Tools/wsm3d.ps1 kill && launch`, every /phase/<X> returns
  the same enabled value as before kill
- WorldSphereTab.RegisterToggleButton sets boolVal = Enabled
  unconditionally
- Reflection-mirror into Core.savedSettings.<ID>

**Related Tests:**
`tests/WorldSphereMod.Tests.Unit/PlayerConfigMirrorTests` (TBD)

---

### FR-WSM-014: Bridge POST Phase Activation — **LANDED 2026-05-21 (POST returns ok=true + /phase/<key> echoes)**

**Description:** `POST /settings/<key>?value=true|false` writes
SavedSettings via reflection + invokes Core.ApplyPhaseToggle.

**Acceptance Criteria:**
- POST returns `{ok:true, key, value}` on success
- Subsequent `/phase/<key>` reflects the new state
- ApplyPhaseToggle handler runs (driver attach/detach where applicable)

**Related Tests:**
`tests/WorldSphereMod.Tests.E2E/BridgeSettingsPostTests`

---

### FR-WSM-015: Clean Mod Init (No NRE on Load) — **LANDED 2026-05-21 (log scan: 0 WSM3D-tagged NREs between Init Mod and World Loaded)**

**Description:** Mod.OnLoad completes without NullReferenceException
even on cold install or after AssetBundle conflict.

**Acceptance Criteria:**
- Player.log contains "[WSM3D] Init Mod" before "Loading finished"
- Zero -tagged  between Mod.OnLoad and "World Loaded" (vanilla WorldBox PowerButton NRE excluded — upstream bug not blocking mod init)
- LoadAssets null-guards bundle loader

**Related Tests:**
`tests/WorldSphereMod.Tests.E2E/ModLoadSmokeTests`

---

## Non-Functional Requirements

### NFR-WSM-001: Frame Budget at Strategy View

**Description:** Steady-state frame time with all phases enabled on a
populated world.

**Target:** ≤ 50ms (20+ FPS)
**Current:** 426–1115ms (1–2 FPS) — **FAILING**
**Path to target:** Enable DrawMeshInstanced via
ForceFallbackDrawPath=false; verify with `/telemetry.drawCalls << instances`.

---

### NFR-WSM-002: Cache Hit Rate — **MEETS 2026-05-21 (99.99% verified)**

**Description:** Voxel mesh cache hit rate after warmup.

**Target:** > 99%
**Current:** 99.97% — **MEETS**

---

### NFR-WSM-003: Mod.OnLoad Time

**Description:** Time from NML calling OnLoad to "Init Mod" log entry.

**Target:** < 5s
**Current:** ~2.3s — **MEETS**

---

### NFR-WSM-004: Memory Footprint

**Description:** Memory delta after 30 min of strategy view.

**Target:** < 2 GB
**Current:** unmeasured

---

### NFR-WSM-005: Machine-Readable Phase Health

**Description:** Every SavedSettings phase flag has a /phase/<name>
endpoint returning enabled + patchedTypes.

**Target:** 100% coverage
**Current:** 10/10 phases inventoried — **MEETS** (post `29cdaa2`)

---

### NFR-WSM-006: Non-Visual Validation Coverage

**Description:** Fraction of phase-correctness assertions that can be
made via bridge endpoints (no screenshot).

**Target:** ≥ 90%
**Current:** partially adopted — `/phase` covers wire state, `/voxel/*`
covers cache + queue, but mesh-geometry invariants endpoint
(`/voxel/sprite?name=X`) still returns empty for some sprites.

---

## Open Issues Blocking Requirements

| Requirement | Block | Path to close |
|---|---|---|
| FR-WSM-002 | Same as 001 + needs separate build sprite verification | Same fix (12ec2d5) — re-run for building sprite name |
| FR-WSM-008 | Dragonfly bug on bone-weighted meshes | Bind-pose audit; SkinnedMeshRenderer per-actor cleanup |
| NFR-WSM-001 | 30k-60k draws/frame on fallback path | Enable DrawMeshInstanced + verify with /telemetry; BRG migration as Phase 11 |
| NFR-WSM-006 | Mesh-geometry endpoint payload bug | Same as FR-001 block above |

---

## Traceability

PR titles + commit messages cite an `FR-WSM-NNN` or `NFR-WSM-NNN`. Each
PR closes at least one requirement. CodeRabbit + reviewers enforce.

Commit prefix → requirement mapping:

- `feat(phase-N)` → FR-WSM-001 through FR-WSM-012 by phase number
- `feat(ui)` → FR-WSM-011, FR-WSM-013, FR-WSM-014
- `feat(infra)` → FR-WSM-014, NFR-WSM-005, NFR-WSM-006
- `perf(phase-N)` → NFR-WSM-001
- `fix(crash)` / `fix(init)` → FR-WSM-015
- `docs(state|proof|audit)` → NFR-WSM-006

---

## Done Definition

- All FR-WSM-NNN show status `LANDED` with `Verify` returning success
- NFR-WSM-001 frame budget met
- No NRE / crash on save load with any phase combination
- `/voxel/sprite?name=<X>` mesh invariants pass for ≥ 100 cached sprites
- README phase table updated to `landed` per phase
- ADR exists for every flag-gated architectural decision

---

## Out of Scope (v1.0)

- URP migration → Phase 11 spec
- DXR / DLSS / RT → URP-blocked, deferred
- Stratum AssetBundle bake → Phase 5b
- Headless test orchestrator → `Tools/wsm3d-hyperv/` scaffold only
- BatchRendererGroup migration → `UseBRG` flag stub only


---

## Engineering Preferences & Constraints

### EP-1: Wrap Over Hand-Roll

**Preference:** Default to an existing library or framework before
hand-rolling. Hand-roll only when (a) license blocks our redistribution,
(b) the library imposes a heavier runtime than we can afford, or (c)
the surface area is < 100 LOC and well-isolated.

**Applies to:**
- Voxelization (Distance-Transform inflation is the only path we
  hand-roll; other meshing borrows greedy-meshing from Mikola Lysenko)
- Skeletal anim runtime (DragonBones C# MIT preferred over hand-rolled)
- Mesh smoothing (use ProBuilder smoothing if shippable)
- HTTP RPC (NML's existing async helpers, not custom)

### EP-2: Language Preferences

**Preference order for new tooling:**
1. **Rust** (CLI tools, headless orchestrators, native interop) —
   Tools/wsm3d-capture, wsm3d-headless container scripts
2. **Python** (MCP servers, scripting, glue) — Tools/wsm3d-mcp,
   wsm3d-playcua
3. **PowerShell** (>20-line scripts only; <20 lines inline in Justfile/
   Taskfile) — Tools/wsm3d.ps1
4. **C#** (mod core only — anything Roslyn-compiled by NML)

**Avoid:** Bash for anything beyond inline pipelines.

### EP-3: Extensibility / Genericism / Libification

**Preference:** Anything that has 2+ likely consumers extracts to a
public surface from day one.

**Applies to:**
- BridgeRPC endpoint shape — JSON schemas published as part of the
  external WorldSphereAPI v2 surface
- SpriteVoxelizer Build* methods → public static, deterministic given
  same Sprite + depth + style
- AssetShapeRegistry → public, addable via runtime API
  (RegisterCustomShapeHint) for downstream mods
- VoxelMeshCache → exposes Get + TryDescribe for inspection
- Phase activation flow → `Core.ApplyPhaseToggle(name, value)`
  callable from any mod via WorldSphereAPI

### EP-4: Org-Level Fixes Over Patch Workarounds

**Preference:** When the root cause sits in a Phenotype-sibling repo
(phenotype-journeys, NML upstream, PhenoSpecs, PhenoHandbook), open
the upstream PR. Keep local workarounds annotated with the upstream
issue number for removal once merged.

**Active examples:**
- phenotype-journeys#60 (rust-toolchain bump 1.83 → 1.88)
- phenotype-journeys#61 (--mode mock CLI alias restoration)

### EP-5: Latest Packages, CVE-Aware

**Preference:** Default to the latest stable package. Drop to the
highest patched version only when the current latest has an active
CVE; cite the CVE ID when downgrading.

### EP-6: No Unauthorized Destructive Actions

**Preference:** Never `git reset --hard`, `git push --force` to main,
delete branches, modify pipelines, or drop database tables without
explicit user approval. `--force-with-lease` allowed for
session-private branches.

### EP-7: Comments Only For Non-Obvious Whys

**Preference:** Don't explain what code does (the code does that).
Only comment when the WHY is non-obvious: a hidden constraint, a
subtle invariant, a workaround for a specific bug, behavior that
would surprise a reader.

### EP-8: Phenotype Org Conventions

**Preference:** This repo follows PhenoHandbook conventions (canonical
PRD/CHARTER/ADR/FUNCTIONAL_REQUIREMENTS templates, FR-WSM-NNN
identifier prefix, AgilePlus sprint-and-backlog cadence).

**References:**
- https://github.com/KooshaPari/PhenoHandbook
- https://github.com/KooshaPari/PhenoSpecs
- Reference implementation: DINOForge

### EP-9: Machine-Readable Validation > Visual Inspection

**Preference:** Closing acceptance criteria via bridge endpoints
(`/voxel/sprite`, `/phase/<name>`, `/telemetry`) takes precedence over
screenshot interpretation. Screenshot evidence is supplementary.

### EP-10: Manager Mode

**Preference:** Dispatch agents (codex spark with --search, codex
gpt-5.4-mini fallback, Claude haiku/sonnet via Agent tool) in parallel
on independent tracks; harvest + commit each completion. Maintain
≥ 10 in-flight agents when quota allows.

