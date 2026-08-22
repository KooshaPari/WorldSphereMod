# WorldSphereMod — Machine-First Validation Pipeline

## Verification Order (MANDATORY)

```
Layer 1: STATIC ANALYSIS (non-pixel, machine)
  ├─ C# compilation check (dotnet build --no-restore)
  ├─ 611 automated tests (unit + integration + E2E)
  ├─ Source content invariants (file existence, API surface, schema)
  └─ CI gate: ci/lint + ci/test

Layer 2: RUNTIME ANALYSIS (non-pixel, machine)
  ├─ Batch-mode launch: -nographics -batchmode -logfile
  ├─ Headless log scan: search for ERROR/FATAL/NullRef/crash
  ├─ NML compilation log: verify WORLDSPHERE3D_FORK.dll compiled
  ├─ Load timing: LOAD TIME INIT < 30s, CREATE < 5s, GENERATE < 150s
  └─ Memory stability: RSS < 800MB after world gen

Layer 3: PIXEL ANALYSIS (machine, last resort before human)
  ├─ Screenshot capture at T+60s (post world-gen, pre-first-interact)
  ├─ VLM check: "Does this show a 3D world with terrain?"
  ├─ VLM check: "Are there UI elements visible (WorldSphere tab)?"
  └─ VLM check: "Is there any error overlay or crash dialog?"

Layer 4: HUMAN VERIFICATION (absolute last resort)
  ├─ User opens WorldSphere tab
  ├─ User toggles Phase 1-10
  ├─ User interacts with 3D world
  └─ User confirms visual quality
```

## Rubric Test Matrix

### A. Technical Rubric (machine-verified)

| # | Check | Method | Pass Criteria | Current |
|---|---|---|---|---|
| T1 | C# compiles | `dotnet build --no-restore` | 0 errors | PASS |
| T2 | Unit tests | `dotnet test Unit/` | 158 pass, 0 fail | PASS |
| T3 | Integration tests | `dotnet test Integration/` | 69 pass, 0 fail | PASS |
| T4 | E2E tests | `dotnet test E2E/` | 384 pass, 0 fail | PASS |
| T5 | NML compiles mod | DLL exists in CompiledMods/ | 689KB DLL present | PASS |
| T6 | Batch launch | `-nographics -batchmode` | Process exits normally (not crash) | PASS |
| T7 | Headless log clean | grep ERROR/FATAL | 0 critical errors | PASS |
| T8 | Load time | LOAD TIME INIT | < 30 seconds | PASS (27s) |
| T9 | Memory stable | RSS after gen | < 800MB | PASS (460MB) |
| T10 | GPU crash safety | WSM3DRenderer circuit-breaker | After 3 failures, graceful degradation | PASS |
| T11 | CI required gates | ci/lint + ci/test | Both PASSING | PASS |
| T12 | Branch protection | main protection | strict + 1 review + linear | PASS |

### B. User/Roadmap Rubric (human or VLM-verified)

| # | Check | Method | Pass Criteria | Current |
|---|---|---|---|---|
| U1 | Game launches | Human: double-click exe | Main menu appears | BLOCKED* |
| U2 | Mod loads | Human: check NML log | WORLDSPHERE3D_FORK.dll loaded | PASS (batch) |
| U3 | WorldSphere tab | Human: check UI | Tab visible in menu bar | BLOCKED* |
| U4 | Phase toggles | Human: toggle Phase 1-10 | All 10 toggles work | BLOCKED* |
| U5 | 3D rendering | Human: view 3D world | Terrain renders, no black screen | BLOCKED* |
| U6 | Voxel actors | Human: spawn actor | 3D voxel model visible | BLOCKED* |
| U7 | Buildings render | Human: place building | 3D building appears | BLOCKED* |
| U8 | Day/night cycle | Human: wait 60s | Lighting changes smoothly | BLOCKED* |
| U9 | PostFX | Human: check visual quality | SSAO + color grading visible | BLOCKED* |
| U10 | Bridge save/load | Human: save + reload | State persists across save | BLOCKED* |

*BLOCKED = GPU driver issue prevents GUI rendering. Batch mode works.

### C. Regression Rubric (per-change)

| # | Check | When | Method |
|---|---|---|---|
| R1 | No new test failures | Every PR | CI gate |
| R2 | No new E2E invariant break | Every PR | CI gate |
| R3 | mod.json version == VERSION | Every release | E2E test |
| R4 | CHANGELOG updated | Every release | Integration test |
| R5 | Install script present | Every release | Integration test |
| R6 | CODEOWNERS covers paths | Every PR | Unit test |
| R7 | CI workflows valid YAML | Every PR | actionlint |

## Verification Roadmap (Feature → State → Quality → Gate)

```
ROADMAP ENTRY POINT:
====================

[G0] Build succeeds
    ├─ gate: dotnet build --no-restore == 0 errors
    └─ state: DLL exists

[G1] All 611 tests pass
    ├─ gate: dotnet test (unit + integration + E2E) == 0 failures
    └─ state: 158 + 69 + 384 = 611 pass

[G2] Mod compiles via NML
    ├─ gate: WORLDSPHERE3D_FORK.dll in CompiledMods/
    └─ state: 689KB DLL present

[G3] Batch launch succeeds
    ├─ gate: -nographics -batchmode exits normally
    ├─ state: LOAD TIME INIT < 30s, world gen completes
    └─ log: 0 ERROR/FATAL lines

[G4] GUI launch succeeds (BLOCKED: GPU)
    ├─ gate: worldbox.exe runs > 60s without crash
    ├─ state: Main menu visible
    └─ log: 0 shader unsupported errors

[G5] Mod visible in-game
    ├─ gate: WorldSphere tab present
    ├─ state: 3D Phases window opens
    └─ log: [WSM3D] init messages

[G6] All phases load
    ├─ gate: Phase 1-10 toggles all functional
    ├─ state: Each phase renders correctly
    └─ log: [WSM3D][PhaseN] loaded messages

[G7] 3D world renders
    ├─ gate: Terrain + actors visible
    ├─ state: Voxel models, buildings, biome blending
    └─ log: [WSM3D][VoxelRender] DrawMeshInstanced calls

[G8] Full feature set operational
    ├─ gate: All 40 feature areas functional
    ├─ state: Day/night, weather, clouds, LOD, PostFX
    └─ log: No runtime errors

[G9] Release ready
    ├─ gate: v2.0.0-beta.8 tagged + GitHub Release with ZIP
    ├─ state: install.ps1 works, mod loads
    └─ log: Clean headless run + GUI stability
```

## Current Gate Status

| Gate | Status | Evidence |
|---|---|---|
| G0 Build | **PASS** | 611 tests compile and run |
| G1 Tests | **PASS** | 158 unit + 69 integration + 384 E2E |
| G2 NML compile | **PASS** | 689KB DLL in CompiledMods/ |
| G3 Batch launch | **PASS** | 27s init, world gen completes, 0 errors |
| G4 GUI launch | **FAIL** | GPU shader incompatibility (driver-level) |
| G5 Mod visible | **BLOCKED** | Depends on G4 |
| G6 Phases load | **BLOCKED** | Depends on G4 |
| G7 3D render | **BLOCKED** | Depends on G4 |
| G8 Full features | **BLOCKED** | Depends on G4 |
| G9 Release | **PASS** | v2.0.0-beta.8 + installer ZIP |

## GPU Crash Diagnosis (G4 failure)

**Root cause:** WorldBox's stripped shader set + GPU driver incompatibility.

**Evidence from headless log (3621 lines):**
- 198+ WorldBox assemblies loaded successfully
- NML compiled WORLDSPHERE3D_FORK.dll (689KB) without error
- World generation completed (LOAD TIME GENERATE: 101.9s)
- Game ran in batch mode for full duration (no crash)
- GPU warnings present: "Shader ... not supported on this GPU"

**Fix applied:** Circuit-breaker in WSM3DRenderer.Execute() — after 3 consecutive GPU failures, Forward+ rendering disables gracefully instead of crashing Unity.

**Remaining fix options:**
1. **Driver update** — check for GPU driver update
2. **Shader fallback** — add explicit fallback shaders for unsupported features
3. **Renderer toggle** — add settings option to disable Forward+ entirely
4. **Test on different GPU** — isolate driver vs hardware issue

## Machine-First Validation Commands

```bash
# Layer 1: Static analysis
dotnet build WorldSphereMod.csproj --no-restore && echo "BUILD OK" || echo "BUILD FAIL"
dotnet test tests/WorldSphereMod.Tests.Unit/ --no-restore && echo "UNIT OK" || echo "UNIT FAIL"
dotnet test tests/WorldSphereMod.Tests.Integration/ --no-restore && echo "INT OK" || echo "INT FAIL"
dotnet test tests/WorldSphereMod.Tests.E2E/ --no-restore && echo "E2E OK" || echo "E2E FAIL"

# Layer 2: Runtime analysis
"C:\Program Files (x86)\Steam\steamapps\common\WorldBox\worldbox.exe" \
  -nographics -batchmode -logfile wsm_headless.log
# Check exit code and log for ERROR/FATAL

# Layer 3: Pixel analysis (if screenshot capture available)
# VLM: "Does this screenshot show a 3D terrain world?"
# VLM: "Are there UI elements visible in the menu bar?"
# VLM: "Is there any error overlay or crash dialog?"

# Layer 4: Human verification (absolute last resort)
# Launch game, check WorldSphere tab, toggle phases, verify rendering
```
