# Functional Requirements

## FR-WSM-001: Voxel Actor Meshes
**Description:** Replace 2D actor sprites with voxel meshes built from the sprite via SpriteVoxelizer (extruded/balloon/lathe per shape hint).

**Acceptance Criteria:**  
- `curl /voxel/sprite?name=walk_0` returns `{vertexCount>0, triangleCount>0, distinctTriVerts:true, maxTriIndexLessThanVerts:true}`  
- `/telemetry.voxelCacheHit > 0.99` after warmup  
- `Actor.scales[i] != Vector3.zero` where the voxel branch took ownership  

**Related Tests:** `tests/WorldSphereMod.Tests.Unit/SpriteVoxelizerTests`, `tests/WorldSphereMod.Tests.Unit/AssetShapeRegistryTests`

## FR-WSM-002: Voxel Building Meshes
**Description:** Replace 2D building sprites with voxel meshes or procedural architectural meshes (BuildingProcRender behind BuildingStyleProcgen flag).

**Acceptance Criteria:**  
- `curl /voxel/sprite?name=main_0_0` returns valid mesh invariants  
- `/phase/ProceduralBuildings` shows `enabled=true` with patches>=1  
- `BuildingProcRender.EmitMeshes.Regular` count > 0 in log per frame  

**Related Tests:** `tests/WorldSphereMod.Tests.Unit/BuildingRulesRegistryTests`

## FR-WSM-003: Per-Sprite Shape-Hint Routing
**Description:** AssetShapeRegistry maps asset ID prefixes to ShapeHint (Cylinder, LongX, Tall, Flat, Mirror, Auto), routing voxelization appropriately.

**Acceptance Criteria:**  
- `AssetShapeRegistry.GetShapeHint("human_warrior")==Cylinder`  
- `GetShapeHint("boat_small")==Mirror`  
- `GetShapeHint("wall_stone")==LongX`  
- Default falls back to aspect-ratio heuristic  

**Related Tests:** `Phase6RigRegistryTests`, manual `/voxel/sprite` sample with directional sprite (no radial fan artifact)

## FR-WSM-004: LOD Tier Selection + Impostor Fallback
**Description:** LodSelector chooses Voxel / Procedural / Impostor tier per entity per frame based on screen-projected size.

**Acceptance Criteria:**  
- `/telemetry.impostorCacheHit > 0.99` when zoomed out  
- At close zoom, voxel tier active for ≥ 80% of visible entities  
- `LodSelector._entityHeight` scales with `VoxelScaleMultiplier`  

**Related Tests:** `LodSelectorTests`

## FR-WSM-005: Mesh Water
**Description:** Replace flat water plane with Gerstner-wave displaced mesh updated each frame via `_WaveTime` uniform.

**Acceptance Criteria:**  
- `/phase/MeshWater` enabled=true with patches >= 5  
- `WaterRender.UpdateLifecycle` produces visible mesh in Player.log  
- Wave amplitude > 0 at runtime  

**Related Tests:** Visual via `/voxel/dump_all`-style water-surface inspection endpoint (TBD)

## FR-WSM-006: Crossed-Quad Foliage
**Description:** Trees + bushes render as crossed-quad billboards with wind-sway shader.

**Acceptance Criteria:**  
- `/phase/CrossedQuadFoliage` enabled=true with patches >= 2  
- `FoliageTileRender + WallTileRender` Postfixes fire on `WorldTilemap.renderTile`  

**Related Tests:** `tests/WorldSphereMod.Tests.Unit/Phase3FoliageTests`

## FR-WSM-007: High Shadows with Cascade Mapping
**Description:** SunDriver configures `QualitySettings.shadowCascades=4` + shadowDistance tuned for voxel silhouettes; shadow strength + bias calibrated.

**Acceptance Criteria:**  
- `/phase/HighShadows` enabled=true with patches >= 1  
- `QualitySettings.shadowCascades == 4` when flag on  
- Shadow bias != default  

**Related Tests:** TBD — needs editor-mode assertion harness

## FR-WSM-008: Skeletal Animation
**Description:** Humanoid actors deform via bone matrices driven by animation curves; bone weights baked into voxel mesh per RigType.

**Acceptance Criteria:**  
- `/phase/SkeletalAnimation` enabled=true with patches >= 1  
- No vertex displacement > 10× sprite extent (dragonfly bug avoidance)  
- Walk-cycle visible on humanoid actors  
- **Status: BLOCKED** by dragonfly bug — bind-pose audit pending.  

**Related Tests:** `Phase6RigRegistryTests`, `tests/WorldSphereMod.Tests.Unit/SkeletalDeformationBoundsTests` (TBD)

## FR-WSM-009: Day/Night Cycle
**Description:** Continuous sun rotation + sky color interpolation driven by WorldBox time scale.

**Acceptance Criteria:**  
- `/phase/DayNightCycle` enabled=true with patches >= 1  
- `SunDriver.CurrentAngle` changes > 0.01 rad/sec during active gameplay  
- Skybox color gradients interpolate per sun position  

**Related Tests:** TBD

## FR-WSM-010: Post-FX Pipeline
**Description:** SSAO + SSGI + ACES tonemap + HDR cubemap reflection via OnRenderImage chain, gated by SavedSettings flags.

**Acceptance Criteria:**  
- `/phase/PostFX` enabled=true with patches >= 1  
- SSAO/SSGI components attached to main Camera when flag on  
- ACES tonemap shader resolves at material load  

**Related Tests:** TBD

## FR-WSM-011: Worldspace UI (Health Bars + Labels)
**Description:** 3D mesh health bars + 3D mesh labels attached to actor head positions, camera-facing.

**Acceptance Criteria:**  
- `/phase/WorldspaceUI` enabled=true with patches >= 1  
- Health bar 3D mesh submitted per actor when `WorldspaceHealth3D` on  
- TextMesh attached per actor when `WorldspaceLabel3D` on  

**Related Tests:** TBD

## FR-WSM-012: Voxel-Mesh Particle Bursts
**Description:** Explosions/blood/fire/leaves spawn voxel-mesh bursts via VoxelParticleBurst lifecycle (spawn → grow → fade alpha).

**Acceptance Criteria:**  
- `/phase/ParticleEffects` enabled=true with patches >= 3  
- `Meteorite.spawnOn + ExplosionFlash.start + StatusParticle.spawnParticle` all trigger `VoxelParticleBurst.TryStart`  

**Related Tests:** `tests/WorldSphereMod.Tests.Unit/Phase9bParticleTests`

## FR-WSM-013: Settings Persistence Across Launches
**Description:** SavedSettings + PlayerConfig.dict mirror each other at toggle registration time; phase flags survive kill+launch.

**Acceptance Criteria:**  
- After `pwsh Tools/wsm3d.ps1 kill && launch`, every `/phase/` returns same enabled value as before kill  
- `WorldSphereTab.RegisterToggleButton` sets `boolVal = Enabled` unconditionally  
- Reflection-mirror into `Core.savedSettings`.  

**Related Tests:** `tests/WorldSphereMod.Tests.Unit/PlayerConfigMirrorTests` (TBD)

## FR-WSM-014: Bridge POST Phase Activation
**Description:** `POST /settings/?value=true|false` writes SavedSettings via reflection + invokes `Core.ApplyPhaseToggle`.

**Acceptance Criteria:**  
- POST returns `{ok:true, key, value}` on success  
- Subsequent `/phase/` reflects the new state  
- ApplyPhaseToggle handler runs (driver attach/detach where applicable)  

**Related Tests:** `tests/WorldSphereMod.Tests.E2E/BridgeSettingsPostTests`

## FR-WSM-015: Clean Mod Init (No NRE on Load)
**Description:** `Mod.OnLoad` completes without `NullReferenceException` even on cold install or after AssetBundle conflict.

**Acceptance Criteria:**  
- Player.log contains `"[WSM3D] Init Mod"` before "Loading finished"  
- Zero `NullReferenceException` between Mod.OnLoad and "World Loaded"  
- `LoadAssets` null-guards bundle loader  

**Related Tests:** `tests/WorldSphereMod.Tests.E2E/ModLoadSmokeTests`

## NFR-WSM-001: Frame Budget at Strategy View
**Description:** Steady-state frame time with all phases enabled on a populated world.  
**Target:** ≤ 50ms (20+ FPS)  
**Current:** 426–1115ms (1–2 FPS) — **FAILING**  
**Path to target:** Enable DrawMeshInstanced via ForceFallbackDrawPath=false; verify with `/telemetry.drawCalls << instances`.

## NFR-WSM-002: Cache Hit Rate
**Description:** Voxel mesh cache hit rate after warmup.  
**Target:** > 99%  
**Current:** 99.97% — **MEETS**

## NFR-WSM-003: Mod.OnLoad Time
**Description:** Time from NML calling OnLoad to "Init Mod" log entry.  
**Target:** < 5s  
**Current:** ~2.3s — **MEETS**

## NFR-WSM-004: Memory Footprint
**Description:** Memory delta after 30 min of strategy view.  
**Target:** < 2 GB  
**Current:** unmeasured  

## NFR-WSM-005: Machine-Readable Phase Health
**Description:** Every SavedSettings phase flag has a `/phase/` endpoint returning enabled + patchedTypes.  
**Target:** 100% coverage  
**Current:** 10/10 phases inventoried — **MEETS** (post `29cdaa2`)

## NFR-WSM-006: Non-Visual Validation Coverage
**Description:** Fraction of phase-correctness assertions that can be made via bridge endpoints (no screenshot).  
**Target:** ≥ 90%  
**Current:** partially adopted — `/phase` covers wire state, `/voxel/*` covers cache + queue, but mesh-geometry invariants endpoint (`/voxel/sprite?name=X`) still returns empty for some sprites.
