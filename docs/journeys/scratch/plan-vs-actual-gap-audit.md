# PLAN vs Actual Gap Audit

**Last updated:** 2026-05-24

Scope: root `PLAN.md` vs current `WorldSphereMod` code and `README.md`.
“Landed” means visible in a fresh install with current `SavedSettings` defaults
(no extra manual toggles). Beta defaults flipped most phase flags ON since the
prior audit; remaining gaps are mostly opt-in polish (voxel Laplacian smoothing,
style procgen branch) or partial Phase 10 proxy tier.

**Offline gate:** 476 tests total (473 pass / 3 skip) — Unit 151 (+ 3 skip), Integration 67, E2E 255. Journey mock (20 manifests) via `pwsh Tools/wsm-live-verify.ps1`.

## 2026-05-24 status summary

| Area | Prior audit (pre-beta defaults) | Current code / README |
|---|---|---|
| Phase defaults | Many flags default `false` | Phases 1–9 default ON per `SavedSettings.cs` and README phase table |
| Terrain polish | “Missing” biome/mountain smoothing | `BiomeBlending` + `MountainSlopeSmoothing` implemented, default ON (`TileMapToSphere.cs`, `Terrain/TerrainSmoothing.cs`) |
| Voxel mesh smoothing | Not called for terrain | Still voxel-only via `MeshSmoother` when `SmoothingIterations > 0` (default 0); `VoxelMeshSmoothing` default off |
| Clouds (PLAN Phase 3) | Particle burst only | **LANDED** — `CloudCrossedQuadRender.cs` + `Effects.cs` (`EmitCrossedQuad` on `fx_cloud`); E2E: `CloudCrossedQuadInvariantsTests`; PlayCUA: `phase-3b-cloud-crossed-quad.yaml` |
| Water / shadows / fog / PostFX | “Hidden by defaults” | Default ON: `MeshWater`, `HighShadows`, `DayNightCycle`, `FogDensity=0.05f`, `PostFX`, `ParticleEffects` |
| Buildings / skeletal | Default off | Default ON: `ProceduralBuildings`, `SkeletalAnimation`; `BuildingStyleProcgen` still opt-in off |
| Phase 10 LOD | — | README: code-complete; Proxy tier still routes to Voxel |

**E2E guard:** `PlanReadmePhaseCoverageTests` asserts README phase table rows exist for PLAN phases 0–10.

## Top gaps (plan vs actual, 2026-05-24)

1. **Voxel Laplacian smoothing (PLAN-adjacent).** `MeshSmoother` runs on voxel cache meshes when `SmoothingIterations > 0` (`VoxelMeshCache.cs`). Defaults: `VoxelMeshSmoothing=false`, `SmoothingIterations=0`. Not a terrain-mesh pass. Code: yes (opt-in). Flag: off. Gate: iteration count.

2. **Building style procgen branch.** `BuildingProcRender` can use `BuildingStyleProcgen` stylized path (`BuildingProcRender.cs`). Default off while `ProceduralBuildings=true`. Users get voxelized buildings unless style toggle is on. Code: yes. Flag: style off. Gate: secondary toggle.

3. **Phase 10 Proxy LOD tier.** README documents Proxy still routes to Voxel; `LodSelector` / impostor path exists for hardware-gate fallback. Code: partial. Not a default-visible proxy mesh swap. Detail: [`phase10-proxy-tier-status.md`](phase10-proxy-tier-status.md). E2E: `LodPhase10InvariantsTests.Proxy_tier_emit_uses_full_voxel_path_until_BuildProxy_ships`.

4. **Phase 9 breadth.** Particle bursts cover fixed IDs (`fx_meteorite`, `fx_explosion_wave`, `fx_fire_smoke`, `fx_antimatter_effect`, `fx_napalm_flash`, `fx_cloud`) — not a general effect→mesh converter. Defaults ON. Gate: `BaseEffectController.GetObject` + `ParticleEffects`.

5. **Water shader asset bundle.** `WaterSurface` + `WaterGerstner.shader` exist; README notes AssetBundle bake deferred to Phase 5b. Runtime may fall back if shader resource missing (`WaterSurface.EnsureMaterial`). Default ON (`MeshWater=true`).

6. **PostFX / SSAO pipeline deps.** `PostFxController` wired from frame driver; no-ops if URP types absent. Defaults ON (`PostFX`, `SSAOEnabled`). `SSGIEnabled` default off.

7. **Non-humanoid skeletal fallback.** `RigDriver` inline-gated in `VoxelRender`; unknown rigs use static voxel mesh. Default ON (`SkeletalAnimation=true`).

8. **Live journey / smoke verification.** HANDOFF and README distinguish code defaults from live smoke-test proof. Journeys exist under `docs/journeys/manifests/`; strict-assets / capture validation still documented as open work.

9. **docs/PLAN.md vs root PLAN.md.** `docs/PLAN.md` is a pointer; canonical plan is `/PLAN.md`. README links `docs/PLAN.md` (resolves via pointer). No functional gap.

## Resolved since prior audit

- **Cloud crossed-quads (PLAN Phase 3).** `CloudCrossedQuadRender.cs` submits `fx_cloud` via shared crossed-quad cache; `Effects.cs` wires `TryStart` / `Update` / `Clear` on spawn, per-frame refresh, and destroy. `Constants.cs` sets `EmitCrossedQuad` on `fx_cloud`. E2E: `CloudCrossedQuadInvariantsTests`. PlayCUA scenario: `Tools/wsm3d-playcua/sample-scenarios/phase-3b-cloud-crossed-quad.yaml`. Default: ON (`CrossedQuadFoliage=true`, `ParticleEffects=true`).
- **Defaults cascade:** Water, shadows, procedural buildings, skeletal animation, day/night, fog, PostFX, particle effects, foliage — now default ON (see `SavedSettings.cs`, README table).
- **Biome blending:** `TileMapToSphere` + `BiomeBlending=true` by default.
- **Mountain slope smoothing:** `Terrain/TerrainSmoothing.cs` + `MountainSlopeSmoothing=true` by default.

## Bottom line (2026-05-24)

- **Working by default:** voxel actors, procedural buildings, crossed-quad foliage, crossed-quad clouds (`fx_cloud`), mesh water, sun/shadows/sky, skeletal (humanoid path), worldspace UI, day/night + fog, particle bursts (limited IDs), PostFX/SSAO when pipeline present, biome + mountain terrain polish.
- **Opt-in / partial:** voxel Laplacian smoothing, `BuildingStyleProcgen`, Phase 10 proxy tier, general effect mesh conversion, live journey capture proof.
