# WSM3D requirement traceability

Tracera-style requirement -> code -> test -> PR trace map. Intentionally
honest about gaps. Sibling registries:

- Functional requirements: [`docs/FR.md`](./FR.md) (`FR-WSM-NNN`)
- Non-functional requirements: [`docs/NFR.md`](./NFR.md) (`NFR-WSM-NNN`)
- Rollup + per-FR % + commit graph: [`docs/xdd-status.md`](./xdd-status.md)
- Coverage baseline: [`docs/coverage/Summary.txt`](./coverage/Summary.txt)

## Trace key

- `FR` = functional requirement (`docs/FR.md`)
- `NFR` = non-functional requirement (`docs/NFR.md`)
- `Code` = primary implementation surface
- `Test` = current automated verification (tier in parens: U=Unit, I=Integration, E=E2E)
- `PR` = the kind of change that should carry the trace link
- **Status**: C=covered, P=partial, **-**=none

## Functional traceability

| Requirement | Feature | Code surface | Current tests | Status | PR TraceLink |
|---|---|---|---|---|---|
| FR-WSM-001 | Voxel actor meshes | `WorldSphereMod/Code/Voxel/VoxelRender.cs`, `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs` | `tests/WorldSphereMod.Tests.Unit/SpriteVoxelizerTests`, `SpriteVoxelDepthExtrusionTests`, `AssetShapeRegistryTests` (U) | P | `feat(phase-1)` |
| FR-WSM-002 | Voxel/procgen building meshes | `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs`, `WorldSphereMod/Code/ProcGen/BuildingRules.cs` | `tests/WorldSphereMod.Tests.E2E/BuildingMeshGenInvariantsTests`, `BuildingProcGenInvariantsTests`, `BuildingStyleProcgenInvariantsTests` (E) | P | `feat(phase-2)` |
| FR-WSM-003 | Per-sprite shape-hint routing | `WorldSphereMod/Code/Import/AssetShapeRegistry.cs` | `tests/WorldSphereMod.Tests.Unit/AssetShapeRegistryTests`, `Phase6RigRegistryTests` (U) | C | `feat(phase-3)` |
| FR-WSM-004 | LOD tier selection + impostor fallback | `WorldSphereMod/Code/LOD/LodSelector.cs`, `WorldSphereMod/Code/LOD/ImpostorBillboard.cs` | `tests/WorldSphereMod.Tests.E2E/LodPhase10InvariantsTests` (E) | P | `feat(phase-4)` |
| FR-WSM-005 | Mesh water | `WorldSphereMod/Code/Water/WaterRender.cs` | none | **-** | `feat(phase-4)` |
| FR-WSM-006 | Crossed-quad foliage | `WorldSphereMod/Code/Foliage/FoliageTileRender.cs`, `WorldSphereMod/Code/Foliage/WallTileRender.cs` | `tests/WorldSphereMod.Tests.E2E/CloudCrossedQuadInvariantsTests`, `Phase3bSurfaceOverlayInvariantsTests` (E) | P | `feat(phase-3)` |
| FR-WSM-007 | High shadows with cascade mapping | `WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs` | none | **-** | `feat(phase-5)` |
| FR-WSM-008 | Skeletal animation | `WorldSphereMod/Code/Rig/HumanoidRig.cs`, `RigDriver.cs` | `tests/WorldSphereMod.Tests.Unit/HumanoidRigBindPoseTests`, `RigDriverSkinningInvariantsTests` (U); `tests/WorldSphereMod.Tests.E2E/SkeletalRigVariantInvariantsTests` (E) | P | `feat(phase-6)` |
| FR-WSM-009 | Day/night cycle | `WorldSphereMod/Code/Lighting/TimeOfDay.cs`, `Lighting/ProceduralSky.cs` | `tests/WorldSphereMod.Tests.Unit/DayNightSmoothCurveTests` (U); `tests/WorldSphereMod.Tests.E2E/DayNightSmoothInvariantsTests`, `DayNightFogInvariantsTests` (E) | P | `feat(phase-8)` |
| FR-WSM-010 | Post-FX pipeline | `WorldSphereMod/Code/Fx/PostFxController.cs`, `Fx/OnRenderImagePatch.cs` | `tests/WorldSphereMod.Tests.E2E/SsaoPostFxInvariantsTests`, `OnRenderImagePostFxSpecInvariantsTests`, `ForwardPlusRendererInvariantsTests` (E) | P | `feat(phase-9)` |
| FR-WSM-011 | Worldspace UI | `WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs` | `tests/WorldSphereMod.Tests.E2E/Phase3bSurfaceOverlayInvariantsTests` (E) | P | `feat(ui)` |
| FR-WSM-012 | Voxel-mesh particle bursts | `WorldSphereMod/Code/Fx/VoxelParticleBurst.cs` | `tests/WorldSphereMod.Tests.E2E/VoxelParticleBurstInvariantsTests` (E) | P | `feat(phase-9)` |
| FR-WSM-013 | Settings persistence across launches | `WorldSphereMod/Code/SavedSettings.cs`, `SavedSettingsJson.cs` | `tests/WorldSphereMod.Tests.Unit/SettingsPersistenceTests`, `SavedSettingsTests`, `SavedSettingsJsonFuzzTests` (U) | C | `feat(ui)` |
| FR-WSM-014 | Bridge HTTP API | `WorldSphereMod/Code/Bridge/BridgeServer.cs`, `BridgeActions.cs`, `BridgeLoadSaveHooks.cs`, `BridgeSettingParser.cs` | `tests/WorldSphereMod.Tests.E2E/BridgeServerInvariantsTests`, `BridgeActionEndpointsInvariantsTests`, `BridgeSaveLoadStabilityInvariantsTests` (E); `tests/WorldSphereMod.Tests.Unit/BridgeRpcJsonFuzzTests` (U) | P | `feat(infra)` |
| FR-WSM-015 | Clean mod init + API surface | `WorldSphereMod/Code/Mod.cs`, `WorldSphereMod/Code/WorldSphereAPI.cs` | `tests/WorldSphereMod.Tests.Unit/PublicApiSurfaceTests`, `DelegateBindingTests` (U); `tests/WorldSphereMod.Tests.E2E/ModLoadSmokeTests` (E) | P | `fix(init)` |
| FR-WSM-016 | Render-error observability | `WorldSphereMod/Code/RenderErrorRegistry.cs`, `RenderDiagOverlay` | none direct | **-** | `fix(diag)` |
| FR-WSM-017 | AutoTest + screenshot capture | `WorldSphereMod/Code/AutoTest.cs`, `AutoScreenshotDriver.cs`, `ScreenshotCapture.cs` | `tests/WorldSphereMod.Tests.E2E/LiveVerificationHarnessInvariantsTests`, `Wsm3dCliInvariantsTests` (E) | P | `feat(test)` |
| FR-WSM-018 | Input-capture substrate | `WorldSphereMod/Code/InputCapture/*` | `tests/WorldSphereMod.Tests.Integration/JourneyIntegrationTraceTests`, `LiveVerifyHarnessStructureTests` (I) | P | `feat(infra)` |
| FR-WSM-019 | .mcpack texture-pack import | `WorldSphereMod/Code/Import/TexturePackImporter.cs` | `tests/WorldSphereMod.Tests.Unit/TexturePackImporterTests`, `McPackLoaderManifestTests` (U); `tests/WorldSphereMod.Tests.E2E/McTexturePackImporterInvariantsTests` (E) | P | `feat(textures)` |
| FR-WSM-020 | Heightfield terrain mesh | `WorldSphereMod/Code/HeightField/HeightFieldTerrain.cs` | `tests/WorldSphereMod.Tests.E2E/HeightFieldTerrainTextureArrayInvariantsTests`, `BiomeBlendingInvariantsTests` (E) | P | `feat(terrain)` |

## Non-functional traceability

| Requirement | Metric | Code / telemetry surface | Current tests | Status | PR TraceLink |
|---|---|---|---|---|---|
| NFR-WSM-001 | Frame budget (60 fps) | `WorldSphereMod/Code/Perf/FrameProfiler.cs`, FPS HUD | `tests/WorldSphereMod.Tests.E2E/LodPhase10InvariantsTests` (E) | P | `perf(phase-N)` |
| NFR-WSM-002 | Voxel cache hit rate | `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs`, `SavedSettings.VoxelDiskCache` | `tests/WorldSphereMod.Tests.E2E/VoxelMeshCacheInvariantsTests` (E) | P | `perf(phase-N)` |
| NFR-WSM-003 | Mod init time | `WorldSphereMod/Code/InitProfiler.cs` | no direct automated timing gate | **-** | `docs(state|proof|audit)` |
| NFR-WSM-004 | Memory footprint | `/memory` bridge endpoint, `SavedSettings.MaxTilesFor3D` | `tests/WorldSphereMod.Tests.E2E/RepositoryArtifactsTests` (E) | P | `docs(state|proof|audit)` |
| NFR-WSM-005 | Hardware fallback | `WorldSphereMod/Code/Perf/ImpostorFallback.cs` | indirect via `LodPhase10InvariantsTests` (E) | P | `feat(phase-10)` |
| NFR-WSM-006 | Backwards-compatible public API | `WorldSphereMod/Code/WorldSphereAPI.cs` | `tests/WorldSphereMod.Tests.Unit/PublicApiSurfaceTests` (U) | C | `feat(api)` |
| NFR-WSM-007 | Mod coexistence (different GUID) | `WorldSphereMod/mod.json` | `tests/WorldSphereMod.Tests.E2E/ModLoadSmokeTests` (E) | P | `fix(init)` |
| NFR-WSM-008 | Reference-rig perf baselines | `Testing/PERFORMANCE.md` | `Testing/Scenes/*` (manual) | P | `docs(perf)` |
| NFR-WSM-009 | Visual regression coverage | `Tools/verify-visual.py` | `tests/WorldSphereMod.Tests.Integration/VisualRegressionHarnessTests` (I) | P | `feat(verify)` |
| NFR-WSM-010 | Reproducible build | `.github/workflows/{build.yml,bundles.yml}` | `tests/WorldSphereMod.Tests.E2E/CiWorkflowInvariantsTests` (E) | C | `feat(ci)` |
| NFR-WSM-011 | Determinism-on-launch, variety-in-world | `SavedSettings` JSON load | `tests/WorldSphereMod.Tests.Unit/SettingsPersistenceTests` (U) | C | `feat(emergence)` |
| NFR-WSM-012 | Coverage gate (xDD) | `Tools/coverage.ps1`, `docs/coverage/` | `tools/.../coverage` is the gate, not a test | C | `feat(xdd)` |

## Current no-coverage items

- FR-WSM-005 (mesh water)
- FR-WSM-007 (cascaded shadows + sun)
- FR-WSM-016 (render-error observability)
- NFR-WSM-003 (mod init time, no automated timing gate)

## Notes

- The table is a template, not a final authority. It should be updated
  whenever a new PR lands new verification.
- If a requirement moves from partial (P) to covered (C), the PR should
  add or update the exact test reference rather than only the prose.
- The rollup at `docs/xdd-status.md` is the one-page summary; this file
  is the table of record.
- Pre-existing test failures (25: U 8, I 1, E 16) are phase-default
  source-shape drift, NOT coverage regressions. Tracked separately.
