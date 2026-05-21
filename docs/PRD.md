# WorldSphereMod3D — Product Requirements Document

Living document. Source of truth for what we're building, why, and how
we verify done. Issues + PR titles should cite an `FR-XX` or `NFR-XX`.

## 1. Product context

**WorldSphereMod3D** is a Harmony+NeoModLoader fork of
`MelvinShwuaner/WorldSphereMod` for the game **WorldBox**.

Upstream gives a 3D terrain mesh wrapped on a sphere. Every visible
ENTITY (actors, buildings, drops, projectiles, effects) is still a 2D
sprite billboard rotated to face the camera. This fork's job is to
finish that conversion in 10 phases.

**Target user:** modded WorldBox player who wants a fully-3D look
without sacrificing playability, and modders/researchers who want a
machine-introspectable rendering pipeline.

## 2. User-level requirements (URs)

| ID | User wants | Why it matters |
|---|---|---|
| UR-1 | "Every visible entity looks 3D from any camera angle" | Core selling point of the fork; differentiator vs upstream |
| UR-2 | "The game still runs at usable framerate" | Unplayable = uninstalled |
| UR-3 | "Toggling phases doesn't crash the game" | Settings UI must be safe |
| UR-4 | "Visual results match what the sprite looked like" | Trust — no garish color/shape distortions |
| UR-5 | "I can verify pipeline correctness without manually staring at the game" | Tooling for modders + agents |
| UR-6 | "Saves still load cleanly + worldgen still works" | Don't regress vanilla |

## 3. Functional requirements (FRs)

Each FR is a discrete, testable acceptance gate. `Status` is the
current state; `Verify` is the machine-readable check.

| ID | Description | Status | Verify |
|---|---|---|---|
| FR-1 | Actor voxel meshes render in place of 2D billboards | partial | `curl /voxel/sprite?name=walk_0` returns `{distinctTriVerts:true, maxTriIndexLessThanVerts:true, vertexCount>0}` |
| FR-2 | Building voxel meshes render in place of 2D building sprites | partial | `curl /voxel/sprite?name=main_0_0` invariants pass |
| FR-3 | Per-sprite shape-hint routes voxelization (trees lathe, boats mirror, buildings extruded) | landed | `curl /voxel/stats` shows >100 cached + variety; AssetShapeRegistry unit test green |
| FR-4 | LOD impostor fallback for distant entities | landed | `/telemetry impostorCacheHit > 0.99` at high zoom |
| FR-5 | Mesh water with Gerstner waves | enabled, visual unverified | `/phase/MeshWater enabled=true patched>=5` |
| FR-6 | Crossed-quad foliage with wind sway | enabled | `/phase/CrossedQuadFoliage enabled=true patched=2` |
| FR-7 | High-quality cascaded shadows | wired, content unverified | `/phase/HighShadows enabled=true patched>=1` + QualitySettings.shadowCascades==4 |
| FR-8 | Skeletal animation for humanoid actors | **broken** (dragonfly bug) | `/phase/SkeletalAnimation enabled=true` + no actor mesh vertex displacement > 10× sprite extent |
| FR-9 | Day/night cycle with continuous sun rotation | wired | `/phase/DayNightCycle enabled=true` + SunDriver.CurrentAngle changes >0.01rad/sec |
| FR-10 | Post-FX pipeline (SSAO/SSGI/ACES/HDR sky) | wired, asset-bake pending | `/phase/PostFX enabled=true patched>=1` |
| FR-11 | Worldspace UI (3D health bars + labels) | landed | `/phase/WorldspaceUI enabled=true patched>=1` |
| FR-12 | Voxel-mesh particle bursts (explosions, blood, leaves) | enabled | `/phase/ParticleEffects enabled=true patched>=3` |
| FR-13 | UI toggles map 1:1 to SavedSettings + persist across launches | landed | After kill+launch, `/phase/<X>` matches what was set pre-kill |
| FR-14 | Phase activation works via bridge POST without UI | landed | `POST /settings/<key>?value=true` returns ok=true + `/phase/<key>` reflects |
| FR-15 | Mod loads cleanly on fresh install (no NRE during Mod.OnLoad) | landed | `[WSM3D]` log entries present + no `NullReferenceException` in init segment |

## 4. Non-functional requirements (NFRs)

| ID | Description | Target | Current |
|---|---|---|---|
| NFR-1 | Frame budget steady-state with all phases on | < 50ms (20+ FPS) | 426-1115ms (1-2 FPS) — **failing** |
| NFR-2 | Cache hit rate after warmup | > 99% | 99.97% ✓ |
| NFR-3 | Mod.OnLoad time | < 5s | ~2.3s ✓ |
| NFR-4 | Memory footprint after 30min strategy view | < 2 GB delta | unmeasured |
| NFR-5 | Mod survives game version bumps (best-effort) | manual audit per WorldBox release | n/a |
| NFR-6 | Every phase has a machine-readable health endpoint | 100% | 10/10 ✓ (after 29cdaa2 fix) |
| NFR-7 | Visual regression evidence is machine-readable, not screenshot-based | 90%+ via /voxel/* + /phase/* endpoints | partially adopted |

## 5. Open issues blocking specific FRs

| FR | Block | Path |
|---|---|---|
| FR-1, FR-2 | Visible voxel mesh diffs (dot-cloud, billboard slab, shape artifacts) | Balloon solid-fill landed (`d46dd30`); needs A/B visual comparison via `/voxel/sprite` mesh stats |
| FR-8 | Bone weights produce dragonfly-limb deformation when SkeletalAnimation=true | RigDriver Update gated off; needs bind-pose audit + per-rig anim curve verification |
| NFR-1 | 60k draws/frame on fallback Graphics.DrawMesh path | `ForceFallbackDrawPath=false` opt-in; needs DrawMeshInstanced verification with current shader chain |
| Multiple | NML PlayerConfig.dict shadows SavedSettings on launch | Mitigated (`5a60013` mirrors at registration); still requires bridge POST for `Enabled=false` defaults |

## 6. Traceability

Commit prefix conventions encode the linkage:

- `feat(phase-N)` → FR-1 through FR-12 by phase number
- `feat(ui)` → FR-11, FR-13
- `feat(infra)` → FR-14, NFR-6, NFR-7
- `perf(phase-N)` → NFR-1
- `fix(crash)` → FR-15
- `fix(init)` → FR-15
- `docs(state|proof|audit)` → NFR-7

PRs MUST cite a requirement ID in title or body. CodeRabbit + reviewers
will check.

## 7. Done definition

- All FR-N show status `landed` and `Verify` returns success
- NFR-1 frame budget met (20+ FPS at strategy view with all phases on)
- No NRE / crash on save load with any phase combination
- `/voxel/sprite` mesh-invariant assertions pass for ≥100 cached sprites
- README phase table updated to `landed` for each shipped phase
- ADR exists for every flag-gated architectural decision

## 8. Out of scope (for v1.0)

- URP migration (deferred to Phase 11 spec)
- DXR / DLSS / RT (URP-blocked)
- Stratum AssetBundle bake (Phase 5b)
- Headless test orchestrator (`Tools/wsm3d-hyperv/` scaffolded, untested)
- BatchRendererGroup migration (`UseBRG` flag stub only)
