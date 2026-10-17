# WorldSphereMod3D — Deep Audit (2026-10-17)

## IDENTITY
WorldSphereMod3D (`worldsphere3d.fork`, repo `KooshaPari/WorldSphereMod`, ID `1241972761`), v2.0.0-beta.10 — hard fork of MelvinShwuaner/WorldSphereMod that converts WorldBox's 2D sprite pipeline to real 3D voxel/procgen rendering, GUID-co-installable with upstream.

## INTENT
Land a real 3D pipeline (voxel actors, procedural building meshes, crossed-quad foliage, mesh water, sun/shadow cascade, skeletal anim, worldspace UI) on top of upstream's terrain-only 3D base, in 10 phased commits, so a player sees solid 3D units/buildings/water/shadows instead of billboards (parent outcome per dossier §1 + §8).

## CURRENT STATE
Sampled head `ed04e2cf5` (2026-09-20, "chore(deps): enable Dependabot via universal config") is ~15 days ahead of the STATE.md snapshot (`5157797518`, 2026-09-05) — STATE.md is stale. Tree is clean, single `main` branch. 119 .cs files / ~36.6k LOC in `WorldSphereMod/Code`. Manifest claims "611+ automated tests"; xUnit count shows 529 `[Fact]` + 16 `[Theory]` (545 base annotations, `TestCase` data rows push actual runs higher — claim is approximately accurate). 9 CI workflows present; depth-1 clone only exposes the latest commit so full history not inspected.

## CODE REALITY
- Core is the central 2,708-line file (`WorldSphereMod/Code/Core.cs`) — single-class god object with phase defaults, save-migration, force-on patches for `VoxelEntities=true` and `WorldspaceUI=true` (`Core.cs:90, 120-125`) — belt-and-suspenders against stale saved JSON.
- Rendering is multi-file: `Voxel/VoxelRender.cs` (2,251 LOC, `VoxelMeshCache` + `RenderErrorRegistry`), `Renderer/WSM3DRenderer.cs` (278 LOC, deferred-render skeleton), `Water/` (Gerstner shader source landed, AssetBundle bake deferred), `LOD/` (FrustumCuller + LodSelector + ImpostorBillboard), `PostFx/` (URP volume + SSGI/SSAO controllers).
- Bridge IPC (`Bridge/BridgeServer.cs` + `BridgeActions.cs`) listens on :8766 and is the verification surface for the `wsm-live-verify.ps1` and `playcua run-all` gates; tests (`xUnit [Fact]=529`) cover API surface, manifest contracts, bridge invariants.
- Explicit stub/deferred markers in code:
  - `Renderer/WSM3DRenderer.cs:214` — "TileLightCull / ColorPass / PostFXChain / Composite — deferred."
  - `Renderer/WSM3DRenderer.cs:275` — "Stub: depth-only pass into DepthRtId... Mesh submit + early-Z wiring deferred."
  - `Water/WaterRender.cs:122` — per-tile dirty tracking "deferred to Phase 4 polish"
  - `Fx/ParticleEffectLibrary.cs:51` — "actual VFX Graph routing is deferred"
  - `Fx/EffectPatches9.cs:10` — "visual sprite-suppression hook is deferred to Step 5; this file is plumbing only"
  - `Core.cs:2014` — `Sphere.PrepareWorld` deferred when `_map_layers` empty (save-load race)
- Phase 1 PROVEN claim (`docs/issue-triage.md:3-44`, dated 2026-05-27): `visible_units=46`, `lastNonZeroDrawCalls=53`, `voxelCacheSize=52` — real telemetry, ~5 months stale. Phases 2,3,4,5,6,7,8,9,10 are "CODE_LANDED" only; phases 3/5/6/7/9 ship with `default=false`. No TODO/FIXME/NotImplementedException in source (0 matches) — code that is shipped reads as intentional, with deferred work flagged in prose.

## STUB/MOCK/MVP-LAZINESS INVENTORY
- `Renderer/WSM3DRenderer.cs:214, 275` — Forward+ renderer is a thin shell: depth-only stub, lighting/post/composite deferred. Lights/shadows run via Unity's default URP passes, not the spec'd custom chain.
- `Water/WaterRender.cs:122` — mesh water ships with shader source but no AssetBundle bake + no per-tile dirty tracking (rebuilds on each frame in dense tiles).
- `Fx/EffectPatches9.cs:10` + `Fx/ParticleEffectLibrary.cs:51` — Phase 9 particles are capability flags only; VFX Graph wiring deferred.
- `ProcGen/BuildingMeshGen.cs` + `BuildingProcRender.cs` — `default=true` but no in-game visual evidence beyond Phase 1 voxel actors; "PROVEN" status does not cover buildings.
- Crossed-quad foliage (`default=false`) — code path exists but disabled by default; no visible_units telemetry on it.
- `Bridge/BridgeServer.cs:1316, 1331` — deferred-apply queue paths log warnings rather than throw; silently swallows transient failures.
- Self-declared AI-slop repo (README badge + AI-DD-META block): README explicitly states "Slop issues are expected and intentionally present as part of an HITL-less AI-DD metaproject." This is honest, but the operator should not interpret "PROVEN" or "611+ tests" claims as audit-grade.

## FWD PATH TO FINISH
1. **Live qualification (highest value, blocking).** Install into a real WorldBox install (Windows runner per `CI-LIMITATIONS.md`), capture native screenshots of Phases 2-10 with `default=true`, compare to upstream `WorldSphereMod` baseline; populate `docs/issue-triage.md` rows 9-10 with real `visible_units`/frame-time telemetry instead of just Phase 1 from May 2026.
2. **Close the WSM3DRenderer deferred stubs.** Replace the depth-only stub at `WSM3DRenderer.cs:275` with actual mesh-submit + early-Z; add the deferred TileLightCull/ColorPass/PostFXChain/Composite at `:214` (or document them as out-of-scope and remove the misleading skeleton).
3. **Bake the Water AssetBundle + per-tile dirty tracking** (`WaterRender.cs:122`) so mesh water stops rebuilding every frame in dense regions; this is the single biggest perf cliff for Phase 4.
4. **Run the existing PlayCUA gates** (`Tools/wsm3d.ps1 playcua run-all -VisionBackend omniroute`) on a real install and commit the SSIM-vs-`phase-previews/` evidence to `Tools/.reports/`; promote from "CI green offline" to "live-verify green with screenshots".
5. **Refresh STATE.md / CURRENT-STATE.json** in `phenotype-registry/docs/governance/atlas/products/WorldSphereMod/` so the snapshot reflects the ed04e2c head (2026-09-20) instead of 5157797518 (2026-09-05); include actual `visible_units` per phase.

## TIME-TO-FINISH ESTIMATE
~3-4 weeks of focused work to land the live qualification + renderer-stub closure + water bake, assuming a Windows WorldBox runner is available (the gating blocker). 1 week minimum to close the WSM2 renderer skeleton + bake water bundle. The other 5-10 days are for PlayCUA live evidence and STATE.md refresh.

## SUNSET/KEEP CALL
KEEP. This is a real, shipped, installable product with ~36.6k LOC of working Unity/Harmony code, 545 xUnit tests, 9 CI gates, and Phase 1 game-runner evidence (visible_units=46, May 2026). The "PROVEN" Phase 1 is dated but exists; the fork has independent value vs upstream (3D units, procedural buildings, mesh water, worldspace UI). The self-declared AI-slop meta is a red flag for trusting marketing claims, not a sunset signal. Sunset would waste a functioning 36k-LOC codebase with real Phase 1 telemetry. The dossier's parent outcome ("A player can use the intended real 3D/world-shape behavior in the installed supported game with continuity and recovery") is plausibly achievable; the unfinished phases are normal scope, not abandonment.

## CONFIDENCE
HIGH on identity/code-reality/test-counts/stub-inventory (direct file inspection). MEDIUM on phase-PROVEN status (Phase 1 evidence is ~5 months stale; Phases 2-10 not independently re-verified this audit). LOW on time-to-finish estimate (depends on Windows runner availability — `CI-LIMITATIONS.md` flags this as the binding constraint). LOW on the 611-tests claim vs measured 545 base annotations (close but unverified exact count). STATE.md is stale by ~15 days; treat as observation, not proof.