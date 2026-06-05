# WSM3D requirement traceability

This is a Tracera-style requirement -> code -> test -> PR trace map. It is intentionally honest about gaps.

## Trace key

- `FR` = functional requirement
- `NFR` = non-functional requirement
- `Code` = primary implementation surface
- `Test` = current automated verification
- `PR` = the kind of change that should carry the trace link

## Functional traceability

| Requirement | Feature | Code surface | Current tests | Coverage status | PR TraceLink |
|---|---|---|---|---|---|
| FR-WSM-001 | Voxel actor meshes | `WorldSphereMod/Code/Voxel/VoxelRender.cs`, `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs` | `tests/WorldSphereMod.Tests.Unit/SpriteVoxelizerTests`, `tests/WorldSphereMod.Tests.Unit/AssetShapeRegistryTests` | Partial | `feat(phase-1)` |
| FR-WSM-002 | Voxel building meshes | `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs`, `WorldSphereMod/Code/ProcGen/BuildingRules.cs` | `tests/WorldSphereMod.Tests.Unit/BuildingRulesRegistryTests` | Partial | `feat(phase-2)` |
| FR-WSM-003 | Per-sprite shape-hint routing | `WorldSphereMod/Code/Import/AssetShapeRegistry.cs` | `tests/WorldSphereMod.Tests.Unit/AssetShapeRegistryTests`, `tests/WorldSphereMod.Tests.Unit/Phase6RigRegistryTests` | Covered | `feat(phase-3)` |
| FR-WSM-004 | LOD tier selection + impostor fallback | `WorldSphereMod/Code/LOD/LodSelector.cs`, `WorldSphereMod/Code/LOD/ImpostorBillboard.cs` | `tests/WorldSphereMod.Tests.Unit/LodSelectorTests` | Partial | `feat(phase-4)` |
| FR-WSM-005 | Mesh water | `WorldSphereMod/Code/Water/WaterRender.cs` | none | No test coverage | `feat(phase-4)` |
| FR-WSM-006 | Crossed-quad foliage | `WorldSphereMod/Code/Foliage/FoliageTileRender.cs`, `WorldSphereMod/Code/Foliage/WallTileRender.cs` | `tests/WorldSphereMod.Tests.E2E/Phase3FoliageTests` | Partial | `feat(phase-3)` |
| FR-WSM-007 | High shadows with cascade mapping | `WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs` | none | No test coverage | `feat(phase-5)` |
| FR-WSM-008 | Skeletal animation | `WorldSphereMod/Code/Rig/` and rig driver paths | `tests/WorldSphereMod.Tests.Unit/HumanoidRigBindPoseTests`, `tests/WorldSphereMod.Tests.Unit/RigDriverSkinningInvariantsTests` | Partial | `feat(phase-6)` |
| FR-WSM-009 | Day/night cycle | `WorldSphereMod/Code/Lighting/TimeOfDay.cs`, `WorldSphereMod/Code/Lighting/ProceduralSky.cs` | `tests/WorldSphereMod.Tests.E2E/DayNightFogInvariantsTests`, `tests/WorldSphereMod.Tests.E2E/DayNightSmoothInvariantsTests` | Partial | `feat(phase-8)` |
| FR-WSM-010 | Post-FX pipeline | `WorldSphereMod/Code/Fx/PostFxController.cs`, `WorldSphereMod/Code/Fx/OnRenderImagePatch.cs` | `tests/WorldSphereMod.Tests.E2E/SsaoPostFxInvariantsTests`, `tests/WorldSphereMod.Tests.E2E/OnRenderImagePostFxSpecInvariantsTests` | Partial | `feat(phase-9)` |
| FR-WSM-011 | Worldspace UI | `WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs` | `tests/WorldSphereMod.Tests.E2E/Phase3bSurfaceOverlayInvariantsTests` | Partial | `feat(ui)` |
| FR-WSM-012 | Voxel-mesh particle bursts | `WorldSphereMod/Code/Fx/VoxelParticleBurst.cs` | `tests/WorldSphereMod.Tests.E2E/VoxelParticleBurstInvariantsTests` | Partial | `feat(phase-9)` |
| FR-WSM-013 | Settings persistence across launches | `WorldSphereMod/Code/SavedSettings.cs`, `WorldSphereMod/Code/SavedSettingsJson.cs` | `tests/WorldSphereMod.Tests.Unit/SettingsPersistenceTests` | Covered | `feat(ui)` |
| FR-WSM-014 | Bridge POST phase activation | `WorldSphereMod/Code/Bridge/` | `tests/WorldSphereMod.Tests.E2E/BridgeSettingsPostTests` | Partial | `feat(infra)` |
| FR-WSM-015 | Clean mod init | `WorldSphereMod/Code/Mod.cs`, init hooks | `tests/WorldSphereMod.Tests.E2E/ModLoadSmokeTests` | Partial | `fix(init)` |

## Non-functional traceability

| Requirement | Metric | Code / telemetry surface | Current tests | Coverage status | PR TraceLink |
|---|---|---|---|---|---|
| NFR-WSM-001 | Frame budget | `WorldSphereMod/Code/Perf/FrameProfiler.cs`, telemetry hooks | `tests/WorldSphereMod.Tests.E2E/LodPhase10InvariantsTests` | Partial | `perf(phase-N)` |
| NFR-WSM-002 | Cache hit rate | `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs`, `WorldSphereMod/Code/LOD/ImpostorBillboard.cs` | `tests/WorldSphereMod.Tests.Unit/VoxelMeshCacheTests` if present, plus telemetry assertions in E2E | Partial | `perf(phase-N)` |
| NFR-WSM-003 | Mod.OnLoad time | startup logging | no direct automated timing gate | No test coverage | `docs(state|proof|audit)` |
| NFR-WSM-004 | Memory footprint | `/memory` bridge endpoint | `tests/WorldSphereMod.Tests.E2E/RepositoryArtifactsTests` and related bridge checks | Partial | `docs(state|proof|audit)` |
| NFR-WSM-005 | Machine-readable phase health | `/phase/<name>` bridge endpoints | `tests/WorldSphereMod.Tests.E2E/CiWorkflowInvariantsTests`, phase inventory assertions across E2E suite | Covered | `feat(infra)` |
| NFR-WSM-006 | Non-visual validation coverage | bridge and repo-shape checks | `tests/WorldSphereMod.Tests.Unit/*`, `tests/WorldSphereMod.Tests.Integration/*`, `tests/WorldSphereMod.Tests.E2E/*` | Partial | `docs(state|proof|audit)` |

## Current no-coverage items

- FR-WSM-005
- FR-WSM-007
- NFR-WSM-003

## Notes

- The table is a template, not a final authority. It should be updated whenever a new PR lands new verification.
- If a requirement moves from partial to covered, the PR should add or update the exact test reference rather than only the prose.
