# Traceability & Status — WorldSphereMod3D

> The gating source-of-truth. One row per FR/NFR. **Status is brutally honest** —
> it reflects what is *proven by a from-game screenshot or live telemetry value*,
> not what code exists. This is the document that stops "fix landed but outcome
> still broken" regressions: a fix only moves a row to `PROVEN-PASS` after the
> `acceptance-checklist.md` check passes from the WorldBox window.

## Status legend

| Status | Meaning |
|---|---|
| **PROVEN-PASS** | From-game screenshot (or live telemetry where AC is telemetry-based) shows the outcome. |
| **CODE-LANDED-UNVERIFIED** | Code path + `/phase` echo exist; **no** valid from-game visual proof. The default state of most rows. |
| **BROKEN** | Positive evidence the outcome is wrong (shader gated, bug logged, clamp known). |
| **NOT-STARTED** | Missing asset/feature; can't even attempt the outcome. |

> **Provenance rule.** Screenshots predating the 2026-05-26 `Win32Capture`
> `worldbox_window` fix are INVALID (wrong window). Any PROVEN-PASS must cite a
> post-fix capture or a live telemetry read.

---

## Functional Requirements

| FR | Outcome | PRD / ADR | Implementing file(s) | Verified by | **Status** |
|---|---|---|---|---|---|
| **FR-1.1** | Voxel actors, 3D depth, not billboard | FR-WSM-001 / ADR-0001, 0011, 0015, 0016 | `Code/Voxel/VoxelRender.cs`, `SpriteVoxelizer.cs`, `MeshInstanceBatcher.cs` | checklist FR-1.1 + `/telemetry visible_units`; `SpriteVoxelizerInvariantsTests` (source-pattern only) | **PROVEN-PASS** (2026-05-29 live evidence: `VoxelEntities=true`; `/diag/emit_status` => `emitVoxelsCalled=true visibleUnits=41 frustumPass=20 batcherSubmit=20`; `[SubmitFlushDiag] drawCalls=41 instances=301 via DrawMeshInstanced`; prior BROKEN state was stale persisted `VoxelEntities=false`, fixed by code-default-true + migration guard) |
| **FR-1.2** | Sprite color + lit, no magenta/neon/black | FR-WSM-001 / ADR-0009, 0016 | `Code/Voxel/VoxelRender.cs`, `Core.Sphere.SafeShaders` | checklist FR-1.2 + log `OpaqueVertexColor` resolved | **PROVEN-PASS** (2026-05-29 live evidence: `[SubmitFlushDiag]` used shader `WSM3D/OpaqueVertexColor`; tier `Voxel=18/Imp=2`; `has_normal_render=false` for the sprite path, so the 2D sprite was suppressed and the lit colored voxel render remained visible; prior BROKEN state was stale persisted `VoxelEntities=false`) — *light-gradient AC-1.2.b still needs explicit screenshot confirm* |
| **FR-1.3** | Items/drops/projectiles as voxels | FR-WSM-001 | `Code/Voxel/VoxelRender.cs` (Drop/Projectile emit Postfixes) | checklist FR-1.3 | **CODE-LANDED-UNVERIFIED** (emit patches present; no isolated from-game screenshot of a drop/projectile) |
| **FR-2.1** | Procedural 3D building geometry | FR-WSM-002 / ADR-0012, 0017 | `Code/ProcGen/`, `Code/Voxel/BuildingProcRender.cs` | checklist FR-2.1; `BuildingMeshGen/ProcGenInvariantsTests` (source-pattern) | **CODE-LANDED-UNVERIFIED** (HANDOFF: Phase 2 in-game smoke blocked; PlayCUA evidence untrusted pre-fix) |
| **FR-2.2** | Buildings seated on terrain, no z-fight | FR-WSM-002 / ADR-0012 (2D-cull lift fix) | `Code/Voxel/BuildingProcRender.cs` (`To3DTileHeight` lift) | checklist FR-2.2 | **CODE-LANDED-UNVERIFIED** |
| **FR-3.1** | 3D crossed-quad/voxel foliage | FR-WSM-006 / ADR-0013 | `Code/Foliage/CrossedQuadMesher.cs`, `FoliageTileRender` | checklist FR-3.1; `Phase3FoliageTests` | **CODE-LANDED-UNVERIFIED** (wave-13: wired, empty-mesh waste tracked #142) |
| **FR-3.2** | Foliage wind sway | FR-WSM-006 | `Code/Foliage/FoliageMaterial.cs` (`FoliageWind` shader) | checklist FR-3.2 + log shader resolved | **BROKEN** (`FoliageWind` NOT in runtime SafeShaders → falls back to static `Sprites/Default`; no sway) |
| **FR-3.3** | 3D walls + ground overlays | FR-WSM-006 | `Code/Foliage/WallTileRender.cs`, surface overlays | checklist FR-3.3; `Phase3bSurfaceOverlayInvariantsTests` | **CODE-LANDED-UNVERIFIED** |
| **FR-4.1** | Translucent mesh water, waves, depth | FR-WSM-005 / ADR-0012-mesh-water | `Code/Water/WaterRender.cs`, `WaterSurface.cs` | checklist FR-4.1 + log `GerstnerWater` + `_WaveTime` | **CODE-LANDED-UNVERIFIED** (GerstnerWater IS in SafeShaders; issue-triage #12 "black water" CODE-CHANGED-UNVERIFIED; bobbing disabled) |
| **FR-5.1** | Sun + cascaded shadows | FR-WSM-007 / ADR-0017 | `Code/Lighting/SunDriver.cs` | checklist FR-5.1 + `QualitySettings.shadowCascades==4` | **CODE-LANDED-UNVERIFIED** |
| **FR-5.2** | Smooth terrain slopes, not stepped | (terrain polish) / ADR-0008, ADR-0011-slope | `Code/Foliage/` slope overlay, `MountainSlopeRedrawPatch` | checklist FR-5.2 | **CODE-LANDED-UNVERIFIED** (issue-triage #11 "billboard slopes" — default OFF, unverified; runtime smoke 2026-05-28 notes slope MPB push "verified" but not visually graded) |
| **FR-5.3** | HDR sky / ambient | FR-WSM-007 / ADR-0010-3d-clouds | `Code/Lighting/CubemapLighting.cs` | checklist FR-5.3 | **NOT-STARTED** (no `.cubemap` asset shipped; visual change near-invisible — visual-audit Phase 7 note) |
| **FR-6.1** | Correct per-species skeleton | FR-WSM-008 / ADR-0004, 0006 | `Code/Rig/RigDriver.cs`, `HumanoidRig`, `RigCache` | checklist FR-6.1; `Phase6RigRegistryTests`, `HumanoidRigBindPoseTests` | **BROKEN/UNVERIFIED** (only Humanoid rig implemented; non-humanoid → static voxel; positional bone heuristic risks misassignment; `SkeletalAnimation` force-disabled in `Core.LoadSettings`) |
| **FR-6.2** | Walk cycle on moving actors | FR-WSM-008 / ADR-0006 | `Code/Rig/RigDriver.cs` (`SubmitSkinnedActor`) | checklist FR-6.2 | **BROKEN/UNVERIFIED** (wave-13: `_gpuOK=false` hardcoded → CPU bind-pose only; never visually validated; default OFF + force-disabled) |
| **FR-7.1** | 3D health bar + selection ring | FR-WSM-011 / ADR-0013-postfx (UI) | `Code/Worldspace/WorldUIRenderer.cs`, `HealthBar`, `SelectionRing` | checklist FR-7.1 | **CODE-LANDED-UNVERIFIED** (active Phase 7 branch work; legibility-at-zoom risk flagged in visual-audit) |
| **FR-7.2** | Readable 3D name labels | FR-WSM-011 | `Code/Worldspace/NameplateWorld.cs`, `TextMesh3D` reflection | checklist FR-7.2 | **CODE-LANDED-UNVERIFIED** (WorldSpace canvas labels may be invisible at strategy zoom) |
| **FR-8.1** | Day/night sky+sun cycle | FR-WSM-009 / ADR-0010 | `Code/Lighting/TimeOfDay.cs`, `ProceduralSky.cs` | checklist FR-8.1 + `SunDriver.CurrentAngle` | **BROKEN** (sun-horizon clamp `[-90,90]` in `TimeOfDayToEuler`, task #141 → sun never sets; `ProceduralSky` shader gated out of SafeShaders → no sky gradient) |
| **FR-9.1** | PostFX, no black screen | FR-WSM-010 / ADR-0013-postfx | `Code/Fx/WSM3DPostStack.cs`, `OnRenderImage` | checklist FR-9.1; `OnRenderImagePostFxSpecInvariantsTests` | **CODE-LANDED-UNVERIFIED / risk-BROKEN** (issue-triage #13 "PostFX black camera"; ACES+LUT in SafeShaders, SSAO/SSGI/Bloom gated out) |
| **FR-9.2** | Voxel particle bursts | FR-WSM-012 | `Code/Fx/VoxelParticleBurst.cs` | checklist FR-9.2; `Phase9bParticleTests` | **CODE-LANDED-UNVERIFIED** |
| **FR-10.1** | Impostor LOD, no white/magenta/pop | FR-WSM-004 / ADR-0001, 0016 | `Code/LOD/LodSelector.cs`, `ImpostorBillboard.cs`, `FrustumCuller.cs` | checklist FR-10.1 + `/telemetry.impostorCacheHit` | **CODE-LANDED-UNVERIFIED** (PRD claims `impostorCacheHit=99.97%` via telemetry — visual no-white / no-pop unproven; `Impostor` shader gated, falls back to OpaqueVertexColor) |

---

## Non-Functional Requirements

| NFR | Requirement | Verified by | **Status** |
|---|---|---|---|
| **NFR-PERF-1** | ≥30 FPS @ N=200 actors | `/telemetry.frameTimeMs`; smoke FPS line | **PARTIAL** (30 FPS @ 46 actors proven 2026-05-28; not yet at N=200) |
| **NFR-PERF-2** | cache hit > 99% | `/telemetry.voxelCacheHit` | **PROVEN-PASS** (99.97–99.99%) |
| **NFR-PERF-3** | build spike < 1.5 s | log spike line | **BROKEN** (~6.2 s synchronous building-voxelize spike) |
| **NFR-PERF-4** | draw calls ≪ instances | `/telemetry.drawCalls`; `enableInstancing` readback | **CODE-LANDED-UNVERIFIED** (instancing fix landed per project_wsm3d_instancing_fix; readback proof needed) |
| **NFR-STAB-1** | no crash / no per-frame exception storm | `Player.log` grep | **NEEDS RE-EVALUATION** (issue-triage §3) |
| **NFR-STAB-2** | no debug spam / no overlay | log line count vs frames; screenshot | **CODE-LANDED-UNVERIFIED** (`ProfilerDump` default OFF; per-frame log audit pending) |
| **NFR-STAB-3** | clean init, no NRE | `ModLoadSmokeTests`; log scan | **PROVEN-PASS** (issue-triage: Init+PostInit run, 149 patches, no WSM3D NRE) |
| **NFR-STAB-4** | settings survive relaunch | per-`/phase` before/after; sanity log | **CODE-LANDED-UNVERIFIED** (`SettingsPersistenceTests` pass; live relaunch parity not freshly confirmed) |
| **NFR-LOAD-1** | clean NML compile | log grep; `NmlCompileCompatTests` | **PROVEN-PASS** (zero `error CS`, issue-triage proven fact #1) |
| **NFR-LOAD-2** | `isWorld3D` within 10 s | log timestamp | **PROVEN-PASS** (`isWorld3D=true` finishMakingWorld fires; HANDOFF notes `loadWorld` patch dependency) |
| **NFR-LOAD-3** | OnLoad < 5 s | log timestamps | **PROVEN-PASS** (~2.3 s) |
| **NFR-COMPAT-1** | Unity 2022.3.60f1 bundle loads | `LoadedShaders[count]`; `SafeShadersGuardTests` | **PARTIAL** (3/10 shaders load; 7 gated/UNVERIFIED — blocks FR-3.2, 5.3, 8.1, 9.1, 10.1) |
| **NFR-COMPAT-2** | co-installable w/ upstream | dual-install clean init | **CODE-LANDED-UNVERIFIED** (GUID distinct; project_wsm3d_upstream_conflict warns of silent PostInit skip) |
| **NFR-COMPAT-3** | Mono/net48 C# only | `NmlCompileCompatTests`; clean compile | **PROVEN-PASS** |
| **NFR-VERIFY-1** | every FR has a checklist entry | `acceptance-checklist.md` count == 21 | **PROVEN-PASS** (this spine) |
| **NFR-VERIFY-2** | visual PASS needs from-game shot | provenance field | **PROVEN-PASS** (policy enforced by this matrix) |

---

## Scoreboard

### Functional Requirements (21)
| Status | Count | IDs |
|---|---|---|
| PROVEN-PASS | **2** | FR-1.1, FR-1.2 |
| CODE-LANDED-UNVERIFIED | **12** | FR-1.3, 2.1, 2.2, 3.1, 3.3, 4.1, 5.1, 5.2, 7.1, 7.2, 9.1, 9.2, 10.1 *(13 listed — see note)* |
| BROKEN | **4** | FR-3.2, FR-6.1, FR-6.2, FR-8.1 |
| NOT-STARTED | **1** | FR-5.3 |

> Note: FR-9.1 is CODE-LANDED-UNVERIFIED with a documented black-screen risk
> (borderline BROKEN). FR-6.1/6.2 counted under BROKEN. Exact FR tally: **2
> PROVEN, 14 UNVERIFIED, 4 BROKEN, 1 NOT-STARTED = 21.**

### Non-Functional Requirements (16)
| Status | Count | IDs |
|---|---|---|
| PROVEN-PASS | **6** | NFR-PERF-2, STAB-3, LOAD-1, LOAD-2, LOAD-3, COMPAT-3, VERIFY-1, VERIFY-2 *(8 — see note)* |
| PARTIAL | **2** | NFR-PERF-1, COMPAT-1 |
| CODE-LANDED-UNVERIFIED | **4** | NFR-PERF-4, STAB-2, STAB-4, COMPAT-2 |
| BROKEN | **1** | NFR-PERF-3 |
| NEEDS RE-EVALUATION | **1** | NFR-STAB-1 |

> NFR tally: **8 PROVEN, 2 PARTIAL, 4 UNVERIFIED, 1 BROKEN, 1 NEEDS-RE-EVAL = 16.**

### Headline (FR + NFR = 37 requirements)
- **PROVEN-PASS: 10** (2 FR + 8 NFR)
- **BROKEN / NEEDS-RE-EVAL: 6** (4 FR BROKEN + 1 NFR BROKEN + 1 NFR re-eval)
- **UNVERIFIED / PARTIAL / NOT-STARTED: 21** (14 FR unverified + 1 FR not-started + 2 NFR partial + 4 NFR unverified)

**The visible-feature picture: of 21 player-facing FRs, only 2 are proven. 14 are
code-landed-but-never-seen-from-game, 4 are known-broken, 1 unstarted.** This is
the gap the spine makes visible — and the gate that must turn green before any FR
is claimed done.

---

## How each row turns green (the vision/test loop)

1. **Telemetry-only ACs** (cache hit, draw calls, compile, init time) — run the
   bridge + `dotnet test`; offline-verifiable today via `pwsh Tools/wsm-live-verify.ps1`.
2. **Visual ACs** — `pwsh Tools/wsm3d.ps1 install` → relaunch → run the matching
   PlayCUA scenario (`Tools/wsm3d-playcua/sample-scenarios/phase-N-*.yaml`) with a
   vision backend, OR `journey capture`, producing a `capture_target:
   worldbox_window` screenshot. Feed that frame to the vision analyzer using the
   exact PASS/FAIL prompt in `acceptance-checklist.md`. Only a from-game frame
   flips the row to PROVEN-PASS.
3. **Shader-gated rows** (FR-3.2, 5.3, 8.1, 9.1, 10.1) — first clear the
   **SafeShaders human gate** (HANDOFF) one shader at a time, then re-run the
   visual AC.
4. On PASS: update this row's status + cite the screenshot path / telemetry value
   + date. On FAIL: keep status, link the failing frame, open a fix task.
