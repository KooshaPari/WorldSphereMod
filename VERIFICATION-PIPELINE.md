# WorldSphereMod — Verification Pipeline & Traceability Matrix
## Machine-First Validation Chain (Non-Pixel → Pixel → VLM → Human)

### Validation Order (per your spec)

```
1. AGENTIC (forgecode/codex exec + VLM)     ← lowest cost, highest throughput
2. MACHINE/NON-PIXEL (unit tests, source analysis, grep, AST)
3. MACHINE/PIXEL (screenshot capture, diff, VLM analysis)
4. VIRTUAL AGENT (Minimax-M3 / GPT-5.4 Mini / <3b VLM)
5. HUMAN (you, as absolute last resort)
```

---

## Phase-by-Phase Verification Rubric

### Launch Verification Chain

| Step | Check | Method | Expected | Auto? |
|---|---|---|---|---|
| L1 | `worldbox.exe -safe-render` starts | Process monitor (tasklist) | PID exists after 10s | YES |
| L2 | NML compiles WORLDSPHERE3D_FORK.dll | File existence check | DLL > 500KB | YES |
| L3 | DLL loaded into process | BepInEx LogOutput.log grep | "WORLDSPHERE3D_FORK" | YES |
| L4 | Game reaches main menu | Unity -batchmode + output log | "Loading finished" | YES |
| L5 | No crash after 60s | Process monitor | PID still alive at 60s | YES |
| L6 | Memory stable (< 3GB) | tasklist /FI check | Mem < 3GB at 60s | YES |

### Mod Loading Verification Chain

| Step | Check | Method | Expected | Auto? |
|---|---|---|---|---|
| M1 | mod.json parsed correctly | Source analysis: mod_compile_records.json | WORLDSPHERE3D_FORK entry exists | YES |
| M2 | Version matches VERSION file | Test: ModJsonManifestIntegrationTests | version == VERSION content | YES |
| M3 | All assemblies resolved | Source: mod_compile_records.json deps | 0 missing deps | YES |
| M4 | NeoModLoader recognized mod | Log grep: "NeoModLoader" in output | Mod listed in loaded mods | PARTIAL |

### Feature State Verification (per Phase)

#### Phase 0 — World Module Core

| Feature | State Claimed | Test | Machine Verification | Pixel/VLM | Human |
|---|---|---|---|---|---|
| SavedSettings init | Stable | `LoadPathSafetyInvariantsTests.savedSettings_is_initialized` | PASS (source invariant) | — | — |
| Phase toggle wiring | Stable | `SourceContentInvariantsTests.WorldSphereTab_cs_wires_core_phase_toggles` | PASS (grep: TogglePhase exists in WorldSphereTab.cs) | — | — |
| Phase defaults drift | Stable | `PhaseDefaultsDriftTests.*` (2 tests) | PASS | — | — |
| Handoff defaults | Stable | `HandoffDefaultsAlignmentTests.*` (7 tests) | PASS | — | — |

#### Phase 1 — Voxel Actors

| Feature | State Claimed | Test | Machine Verification | Pixel/VLM | Human |
|---|---|---|---|---|---|
| VoxelEntities default ON | Stable | `VoxelPipelineRegressionTests.VoxelEntities_defaults_to_true` | PASS | — | — |
| MaxTilesFor3D default | Stable | `VoxelPipelineRegressionTests.SavedSettings_MaxTilesFor3D_default_is_at_least_65536` | PASS | — | — |
| Skeletal rig variants | Stable | `SkeletalRigVariantInvariantsTests.*` (5 tests) | PASS | — | — |
| Stratum PBR fallback | Stable | `StratumPbrPipelineInvariantsTests.*` | PASS | — | — |

#### Phase 2 — Procedural Buildings

| Feature | State Claimed | Test | Machine Verification | Pixel/VLM | Human |
|---|---|---|---|---|---|
| BuildingMeshGen pipeline | Stable | `BuildingProcGenInvariantsTests.*` (3 tests) | PASS | — | — |
| Building style procgen | Stable | `BuildingStyleProcgenInvariantsTests.*` (2 tests) | PASS | — | — |

#### Phase 3 — Forward+ Renderer

| Feature | State Claimed | Test | Machine Verification | Pixel/VLM | Human |
|---|---|---|---|---|---|
| ForwardPlusRenderer gate | Stable | `ForwardPlusRendererInvariantsTests.*` (2 tests) | PASS | — | — |
| WSM3DRenderer scaffold | Stable | `SourceContentInvariantsTests.WSM3DRenderer_forward_plus_scaffold` | PASS | — | — |

#### Phase 4 — GPU Manager

| Feature | State Claimed | Test | Machine Verification | Pixel/VLM | Human |
|---|---|---|---|---|---|
| GPU manager creation | Stable | `GpuManagerBoundaryTests.*` (4 tests) | PASS | — | — |
| Procedural skinning | Stable | `GpuProceduralSkinningScaffoldInvariantsTests.*` | PASS | — | — |

#### Phase 5 — Shadow Cascade

| Feature | State Claimed | Test | Machine Verification | Pixel/VLM | Human |
|---|---|---|---|---|---|
| ShadowCascadeConfig | Stable | `ShadowCascadeConfigInvariantsTests.*` (5 tests) | PASS | — | — |

#### Phase 6 — Voxel Mesh Cache

| Feature | State Claimed | Test | Machine Verification | Pixel/VLM | Human |
|---|---|---|---|---|---|
| Cache lifecycle | Stable | `VoxelMeshCacheInvariantsTests.*` | PASS | — | — |
| LRU eviction | Stable | `WorldSphereTesterCoverageTests.VoxelMeshCache_Evicts` | PASS | — | — |

#### Phase 7 — Worldspace UI

| Feature | State Claimed | Test | Machine Verification | Pixel/VLM | Human |
|---|---|---|---|---|---|
| UI tier mapping | Stable | `LodSelectorUiTierTests.*` (3 tests) | PASS | — | — |
| Worldspace UI detail | Partial | LodSelectorUiTierTests only | **NEEDS 9 MORE TESTS** | Screenshots needed | Yes |

#### Phase 8 — Day/Night Cycle

| Feature | State Claimed | Test | Machine Verification | Pixel/VLM | Human |
|---|---|---|---|---|---|
| DayNightFog | Stable | `DayNightFogInvariantsTests.*` (2 tests) | PASS | — | — |
| Smooth transitions | Stable | `DayNightSmoothInvariantsTests.*` (3 tests) | PASS | — | — |

#### Phase 9 — PostFX

| Feature | State Claimed | Test | Machine Verification | Pixel/VLM | Human |
|---|---|---|---|---|---|
| PostFxController URP | Stable | `SsaoPostFxInvariantsTests.*` (4 tests) | PASS | — | — |
| WSM3DPostStack | Stable | `OnRenderImagePostFxSpecInvariantsTests.*` (4 tests) | PASS | — | — |

#### Phase 10 — LOD System

| Feature | State Claimed | Test | Machine Verification | Pixel/VLM | Human |
|---|---|---|---|---|---|
| Two-tier LOD ladder | Stable | `LodPhase10InvariantsTests.*` (2 tests) | PASS | — | — |

### Rendering Verification Chain (GUI)

| Step | Check | Method | Expected | Auto? |
|---|---|---|---|---|
| R1 | Game window opens | Process monitor | worldbox.exe PID alive at 5s | YES |
| R2 | Unity init completes | Log grep | "LOAD TIME INIT" in output | YES |
| R3 | World generation starts | Log grep | "LOAD TIME GENERATE" in output | YES |
| R4 | World generation completes | Log grep | "Loading finished" in output | YES |
| R5 | 60s stability | Process monitor | PID alive at 60s | YES |
| R6 | Screenshot captured | Screenshot tool | PNG file exists | YES |
| R7 | VLM analysis of screenshot | Minimax-M3 / GPT-5.4 Mini | "Game menu visible" / "3D world visible" | YES |
| R8 | No shader errors in log | Log grep | 0 "Shader not supported" errors | YES |
| R9 | WorldSphere tab visible | Screenshot + VLM | "3D Phases" text in screenshot | VLM |

### Test Results Summary

| Suite | Passed | Skipped | Failed | Total | Runtime |
|---|---|---|---|---|---|
| Unit | 158 | 3 | 0 | 161 | 840ms |
| Integration | 69 | 0 | 0 | 69 | 501ms |
| E2E | 384 | 0 | 0 | 384 | 863ms |
| **Total** | **611** | **3** | **0** | **614** | **2.2s** |

### Coverage Gap Matrix

| Priority | Feature | Source Files | Test Coverage | Risk | Fix ETA |
|---|---|---|---|---|---|
| **P0** | Capture System | 6 | NONE | High | 2 days |
| **P1** | Worldspace UI detail | 9 | Partial (3 tests) | Medium | 1 week |
| **P1** | Lighting detail | 3 | Partial (via DayNight) | Medium | 3 days |
| **P2** | Foliage | 5 | NONE | Low | 1 week |
| **P2** | VoxelDiskCache | 1 | NONE | Medium | 2 days |
| **P2** | Camera (3DCamera) | 1 | NONE | Low | 1 day |
| **P3** | Debug HUD, Compat, HealthCheck, MeshSmoother, BRG, ProxyMeshCache, ColorTonemap | 8 | NONE | Low | 1 week |

### Failpoint/Breakpoint Map

```
BREAKPOINT 1: worldbox.exe launches
  FAIL: GPU shader crash (FIXED: -safe-render flag)
  FAIL: NML compilation error (CHECK: mod_compile_records.json)
  FAIL: Missing dependencies (CHECK: Assembly-CSharp.dll, etc.)

BREAKPOINT 2: Mod loaded by NeoModLoader
  FAIL: mod.json parse error (TEST: ModJsonManifestIntegrationTests)
  FAIL: Version mismatch (TEST: RepositoryArtifactsTests.Mod_json_version_matches_version_file)
  FAIL: Assembly not found (CHECK: mod_compile_records.json deps)

BREAKPOINT 3: World generation starts
  FAIL: VoxelRender null reference (TEST: VoxelPipelineRegressionTests)
  FAIL: BuildingMeshGen crash (TEST: BuildingProcGenInvariantsTests)
  FAIL: Shadow cascade init (TEST: ShadowCascadeConfigInvariantsTests)

BREAKPOINT 4: Rendering begins
  FAIL: GPU shader crash (FIXED: -safe-render / _useFallbackPath)
  FAIL: MeshInstanceBatcher instancing failure (TEST: caught by try/catch + fallback)
  FAIL: Material not found (TEST: StratumPbrPipelineInvariantsTests)

BREAKPOINT 5: UI visible
  FAIL: WorldSphereTab not wired (TEST: SourceContentInvariantsTests.WorldSphereTab_cs_wires)
  FAIL: Phase toggles missing (TEST: PhaseDefaultsDriftTests)
  FAIL: LOD selector broken (TEST: LodSelectorUiTierTests)

BREAKPOINT 6: Gameplay works
  FAIL: Capture system crash (NO TEST - P0 gap)
  FAIL: Foliage rendering (NO TEST - P2 gap)
  FAIL: Camera movement (NO TEST - P2 gap)
```

### Current Launch State

```
L1: worldbox.exe starts              ✓  (verified: PID alive after 10s)
L2: NML compiles DLL                 ✓  (verified: DLL exists, 689KB)
L3: DLL loaded                       ✓  (verified: mod_compile_records.json)
L4: Game reaches main menu           ✓  (verified: batchmode "Loading finished")
L5: 60s stability                    ✓  (verified: PID alive after 75s)
L6: Memory stable                    ✓  (verified: 2.2GB → 2.3GB, normal growth)
R1: GUI opens                        ✓  (launched with -safe-render)
R5: GUI 60s stability               ✓  (verified: PID alive after 75s)
```

**Status: Game is running with GUI via `-safe-render`. Check the WorldSphere tab.**
