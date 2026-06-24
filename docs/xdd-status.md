# xDD status (one-page rollup)

> Source of truth: `docs/coverage/Summary.txt` + `docs/requirements-traceability.md`
> + `docs/FR.md` / `docs/NFR.md`. This page is a rollup, not the table of record.

## Coverage baseline (commit `caa849aba`, 2026-06-05)

- **Line: 80%** (40 / 50) &nbsp; **Branch: 55.5%** (10 / 18) &nbsp; **Method: 100%** (16 / 16)
- Assembly in scope: `WorldSphereAPI` (only Unity-free surface
  compile-linked to test projects). Full report: `docs/coverage/index.html`.
- xUnit inventory: **538 tests** total — Unit 161 / Integration 69 / E2E 308.
- Pre-existing failures: **25** (Unit 8, Integration 1, E2E 16) — all
  assert exact `SavedSettings` / `wsm3d.ps1` defaults that drift as
  phase flags land. Tracked under `fix(xdd): reconcile phase-default
  source-shape drift`. Unrelated to the coverage scaffold.

## Per-FR test-class coverage

Status legend: **C** covered, **P** partial, **-** none. % = rough
fraction of public API surface touched by tests. Tier: U=Unit,
I=Integration, E=E2E. Full mapping in `docs/requirements-traceability.md`.

| FR | Title | Tier | Primary test class(es) | % | Status |
|---|---|---|---|---|---|
| FR-WSM-001 | Voxel actor/item/projectile | U | `SpriteVoxelizerTests`, `SpriteVoxelDepthExtrusionTests`, `AssetShapeRegistryTests` | ~70 | P |
| FR-WSM-002 | Procedural building meshes | U/E | `BuildingMeshGenInvariantsTests`, `BuildingProcGenInvariantsTests`, `BuildingStyleProcgenInvariantsTests` | ~40 | P |
| FR-WSM-003 | Per-sprite shape-hint routing | U | `AssetShapeRegistryTests`, `Phase6RigRegistryTests` | ~80 | C |
| FR-WSM-004 | LOD ladder + impostor fallback | E | `LodPhase10InvariantsTests` | ~30 | P |
| FR-WSM-005 | Mesh water surface | - | none | 0 | **-** |
| FR-WSM-006 | Crossed-quad foliage | E | `CloudCrossedQuadInvariantsTests`, `Phase3bSurfaceOverlayInvariantsTests` | ~30 | P |
| FR-WSM-007 | Cascaded shadows + sun | - | none | 0 | **-** |
| FR-WSM-008 | Skeletal animation | U/E | `HumanoidRigBindPoseTests`, `RigDriverSkinningInvariantsTests`, `SkeletalRigVariantInvariantsTests` | ~50 | P |
| FR-WSM-009 | Day/night + procedural sky | U/E | `DayNightSmoothCurveTests`, `DayNightSmoothInvariantsTests`, `DayNightFogInvariantsTests` | ~60 | P |
| FR-WSM-010 | Post-FX pipeline | E | `SsaoPostFxInvariantsTests`, `OnRenderImagePostFxSpecInvariantsTests`, `ForwardPlusRendererInvariantsTests` | ~40 | P |
| FR-WSM-011 | Worldspace UI | E | `Phase3bSurfaceOverlayInvariantsTests` | ~20 | P |
| FR-WSM-012 | Voxel-mesh particle bursts | E | `VoxelParticleBurstInvariantsTests` | ~30 | P |
| FR-WSM-013 | Settings persistence | U | `SettingsPersistenceTests`, `SavedSettingsTests`, `SavedSettingsJsonFuzzTests` | ~85 | C |
| FR-WSM-014 | Bridge HTTP API | E | `BridgeServerInvariantsTests`, `BridgeActionEndpointsInvariantsTests`, `BridgeSaveLoadStabilityInvariantsTests`, `BridgeRpcJsonFuzzTests` | ~70 | P |
| FR-WSM-015 | Clean mod init + API surface | U | `PublicApiSurfaceTests`, `DelegateBindingTests`, `ModLoadSmokeTests` (E2E) | ~80 | P |
| FR-WSM-016 | Diagnostics + render-error props | - | none direct (covered indirectly by `RenderErrorRegistry` assertions) | ~10 | **-** |
| FR-WSM-017 | AutoTest + screenshot capture | E | `LiveVerificationHarnessInvariantsTests`, `Wsm3dCliInvariantsTests` | ~30 | P |
| FR-WSM-018 | Input-capture substrate | I | `JourneyIntegrationTraceTests`, `LiveVerifyHarnessStructureTests` | ~25 | P |
| FR-WSM-019 | .mcpack texture-pack import | U | `TexturePackImporterTests`, `McPackLoaderManifestTests`, `McTexturePackImporterInvariantsTests` | ~70 | P |
| FR-WSM-020 | Heightfield terrain mesh | E | `HeightFieldTerrainTextureArrayInvariantsTests`, `BiomeBlendingInvariantsTests` | ~40 | P |

## Broken / needs-test (no coverage, public API exists)

- **FR-WSM-005** Mesh water — `WaterRender.cs` exists, no automated test.
- **FR-WSM-007** Cascaded shadows + directional sun — `Lighting/ShadowCascadeConfig.cs` exists, no automated test.
- **FR-WSM-016** Render-error diagnostics — `RenderErrorRegistry` referenced but never asserted; `RenderErrorProps`/`RenderDiagOverlay` flags not under test.

## Test categories — Tier split (no `[Trait]` attributes today)

The repo does **not** use xUnit `[Trait("Category", ...)]`. Categorization
is by assembly (U/I/E) and by class-name suffix (`*Tests` / `*InvariantsTests`).
Adding `[Trait]` is a follow-up tracked in §Prioritized plan below.

## Commit graph (since coverage baseline `caa849aba`)

`git log --oneline caa849aba..HEAD` (12 commits on `wip/208-height-fix`):

```
08aa17f8 fix(208): banner brush-zone-fix v2.12 on first HUD overlay scale
53c13caa fix(208): per-biome texture pattern via multi-texel sampling in GetTileColor
fdcbf56a fix: add terrain texture-array support for heightfield renderer
7da69717 fix(208): water surface flat at sea level, decoupled from seabed
e229f0cd Revert "fix: restore settings tab categories in settings panel"
d87b0082 fix: restore settings tab categories in settings panel
b206c1d3 fix(204): consistent WSM3D_POSTFX_KEEP pragma on all postFX shaders
d3f3bb8d fix: add procbuilding distance gate
13880df3 WSM3D: shrink worldspace nametags further (#208 wip -- supersedes #191)
edddd01a fix(hud): keep brush + kingdom icons visible when zoomed out
fd4fa9cd fix(208): distance-gate far buildings + lower BuildingRenderBudget to 50
caa849ab chore(xdd): scaffold coverage tooling + traceability templates   <- baseline
```

Reproduce locally: `git log --oneline caa849aba..HEAD` from
repo root (`E:/Dev/WorldSphereMod`).

## Prioritized plan (next 2 weeks)

1. **Add `[Trait("Category", "...")]`** to all test classes (Unit/Integration/E2E
   + a new `Visual` / `Perf` category for the missing tiers). Cheap; unlocks
   filtering and `dotnet test --filter Category=Visual`.
2. **Cover the 3 gap items**: FR-WSM-005 (water), FR-WSM-007 (shadows),
   FR-WSM-016 (render-error). Each gets a `*InvariantsTests` class.
3. **Wire coverage gate in CI**: uncomment the `TODO(xdd-coverage)` block
   in `.github/workflows/build.yml`; fail build on line-coverage regression
   > 2 percentage points week-over-week.
4. **Property-based tests** (`FsCheck`) for `SavedSettings` round-trip
   and `SpriteVoxelizer.Build()` on random opaque-pixel masks.
5. **Mutation tests** (`Stryker.NET`) on `WorldSphereAPI` (the one
   fully-covered assembly) to prove the suite is sensitive, not just present.
