# Branch triage

| branch | ahead | behind | purpose | verdict |
|---|---:|---:|---|---|
| claude/research-ultraplan-fork-DdgI5 | 363 | 0 | 2bbec73a Terrain: kill residual rebuild storm + fix night-black lowland (#35); 4ae80093 fix(terrain): resolve OpaqueVertexColor bundle shader for terrain (Gemini #35 — empty bundleName always fell back to Standard); 7169c87d perf+fix(terrain): eliminate residual rebuild re-trigger + lift dark-lowland lighting | EXPERIMENTAL |
| feat/gpu-compute-p4-consumer-migration | 377 | 0 | 039bfcce bench(#199): add GpuComputeBaselineBench — CPU-ref tile transform at N=1024; 17c0b298 docs(adr,#199): mark GPU-compute ADR Accepted — P0-P5 implemented, CI green; 6aa8c8d4 docs(adr+handoff,#199): mark GPU ADR Accepted + add active-track banner | EXPERIMENTAL |
| fix/live-deprecated | 366 | 0 | 6643985b fix(foliage): remove deprecated 2D billboard/crossed-quad fallback so voxel path is sole renderer; 976473a0 fix: gate wsm3d shader bundle load on verified hash; b724b40e fix(postfx): land valid shader bake + re-expand SafeShaders (#204) | AHEAD-VALUABLE |
| fix/live-lighting | 366 | 0 | 332c7d8f fix(lighting): create + register directional sun so terrain is lit (RenderSettings.sun was null); 976473a0 fix: gate wsm3d shader bundle load on verified hash; b724b40e fix(postfx): land valid shader bake + re-expand SafeShaders (#204) | AHEAD-VALUABLE |
| fix/live-shaderload | 366 | 0 | d09e081d fix(shaderload): correct manifest path probe so verified bundle shaders load; 976473a0 fix: gate wsm3d shader bundle load on verified hash; b724b40e fix(postfx): land valid shader bake + re-expand SafeShaders (#204) | AHEAD-VALUABLE |
| fix/live-voxel | 366 | 0 | e3feca66 fix(voxel): force VoxelEntities on via settings-version bump (2.5->2.6); 976473a0 fix: gate wsm3d shader bundle load on verified hash; b724b40e fix(postfx): land valid shader bake + re-expand SafeShaders (#204) | AHEAD-VALUABLE |
| fix/postfx-shaders-rebake | 365 | 0 | 976473a0 fix: gate wsm3d shader bundle load on verified hash; b724b40e fix(postfx): land valid shader bake + re-expand SafeShaders (#204); 2bbec73a Terrain: kill residual rebuild storm + fix night-black lowland (#35) | AHEAD-VALUABLE |
| fix/shader-standard-fallback | 36 | 0 | 3399ee36 fix(bridge): honor spawn count, camera x/y/zoom, and screenshot path params; bdf7aa0a feat(bridge): offscreen camera-RT screenshot bypassing debug-console overlay; 31a491f1 feat(capture): input-capture -> learn -> replay substrate (WSM3D side) | AHEAD-VALUABLE |
| fix/terrain-perf-residual-and-lighting | 362 | 0 | 4ae80093 fix(terrain): resolve OpaqueVertexColor bundle shader for terrain (Gemini #35 — empty bundleName always fell back to Standard); 7169c87d perf+fix(terrain): eliminate residual rebuild re-trigger + lift dark-lowland lighting; 74036d63 Reconcile lineages + recover runtime regressions → canonical trunk (#34) | AHEAD-VALUABLE |
| fix/voxel-flag-force | 377 | 0 | 893682b5 fix(settings): force VoxelEntities=true on migration (2.6→2.7) — stale-false JSON kept voxel actors disabled despite prior version bump; 4c8ef5e1 fix(postfx): master-gate postFX shader-bundle loads off — stub bake native-crashes on GetObject from PostStack/Sky/Cubemap, not just SafeShaders (#204); 10397bda fix(shaders): SafeShaders=OpaqueVertexColor only — postFX shaders are stub-baked + native-crash on load (#204 bake unsolved); restore crash-safety | AHEAD-VALUABLE |
| fix/worldspace-ui | 377 | 0 | e40215b3 fix(worldspace): healthbar world-anchor + sane scale + restore nameplates (#191); 4c8ef5e1 fix(postfx): master-gate postFX shader-bundle loads off — stub bake native-crashes on GetObject from PostStack/Sky/Cubemap, not just SafeShaders (#204); 10397bda fix(shaders): SafeShaders=OpaqueVertexColor only — postFX shaders are stub-baked + native-crash on load (#204 bake unsolved); restore crash-safety | AHEAD-VALUABLE |
| integ/live-fixes | 389 | 0 | 094adcf5 revert(shaders): #208 candidate (un-bundle SVC+POSTFX_KEEP) STILL stubs postFX in player — back to crash-safe; postFX bake unsolved after 3 approaches; a44fc8a3 test(shaders): enable #208 candidate bundle e6589a46 (un-bundled SVC + POSTFX_KEEP) for game-test; 1580c77e fix(bake): un-bundle SVC + add keep-keyword to postFX shaders so variants survive AssetBundle strip (#208) | EXPERIMENTAL |
| integrate/3d-on-research | 359 | 0 | f8119ad3 fix(review): address PR #34 AI-reviewer findings (do-all helper, cross-platform capture path, speed/invoke guards); 27091a41 test(unit): sync stale spec assertions to PR behavior changes; 857fb9fb test+fix: align final 6 E2E tests with landed regression fixes (re-gate land) | EXPERIMENTAL |
| main | 0 | 0 | no commits ahead of main | MERGED |
| rebuild/render-architecture | 468 | 0 | fda1df21 fix: consistent per-class scale factors (no raw 8x oversizing); 863b2d39 fix: frustum-only LOD policy (FAR_RING 4x) eliminates distance-flip flash; d186ad45 feat: actor sprite-card rendering (replaces voxel slab) | EXPERIMENTAL |
| wip/208-billboard-diag | 405 | 0 | b8e38e93 fix(terrain): average only opaque pixels for biome color — grass/dirt/sand gray fix (#208); 1ae7fcfd diag(voxel): log actor/building voxel emit counts (#208); 8ecdbdce fix(terrain): wire GetTileColor into sampleColor callback + add COLOR-DIAG (#208) | STALE-WIP |
| wip/208-emission-fix | 406 | 0 | d9894f3f fix(terrain): remove OVC emission floor that washes biome colors (#208); b8e38e93 fix(terrain): average only opaque pixels for biome color — grass/dirt/sand gray fix (#208); 1ae7fcfd diag(voxel): log actor/building voxel emit counts (#208) | STALE-WIP |
| wip/208-fresh-approach | 391 | 0 | 0d47d293 chore: phase 9 step 1 move postfx shaders to worldsphere bundle; 4c68a2de fix(shaders): OVC-only bundle-on (#208 safe-win) — ShaderBundleAvailable=true, SafeShaders=[OVC], compute+postFX gated behind PostFxShaderBundleAvailable=false; 094adcf5 revert(shaders): #208 candidate (un-bundle SVC+POSTFX_KEEP) STILL stubs postFX in player — back to crash-safe; postFX bake unsolved after 3 approaches | STALE-WIP |
| wip/208-fresh-worldsphere-fold | 395 | 0 | 8dc5bafa fix(terrain): use tile position index for vertex colors, not tile_id (#208); 90ac0b73 chore(bake): worldsphere-fold candidate bundle artifacts — 6 postFX→worldsphere, non-postFX→wsm3d-shaders (#208-fresh); b635fbc2 fix(shaders): update SafeShaders regression test + fix empty ResolveShader keys (#208) | STALE-WIP |
| wip/208-full-safe-shaders | 394 | 0 | 9e52b233 fix(terrain): use tile position index for vertex colors, not tile_id (#208); b635fbc2 fix(shaders): update SafeShaders regression test + fix empty ResolveShader keys (#208); 9e0b0a3d feat(shaders): expand SafeShaders to all confirmed non-postFX bundle shaders — CompoundSphere+GerstnerWater+FoliageWind+Impostor+StratumVoxelPBR (#208) | STALE-WIP |
| wip/208-height-diag | 404 | 0 | 1ae7fcfd diag(voxel): log actor/building voxel emit counts (#208); 8ecdbdce fix(terrain): wire GetTileColor into sampleColor callback + add COLOR-DIAG (#208); 83b0ce8a fix(terrain): bypass pixel buffer — use tile sprite texture avg for biome color (#208) | STALE-WIP |
| wip/208-height-fix | 466 | 0 | d186ad45 feat: actor sprite-card rendering (replaces voxel slab); c42907f2 fix: foliage voxel-or-invisible billboard suppression; dc0981ae fix: actor mesh organicblob for 3D volume | EXPERIMENTAL |
| wip/208-ovc-bundle-on | 390 | 0 | 4c68a2de fix(shaders): OVC-only bundle-on (#208 safe-win) — ShaderBundleAvailable=true, SafeShaders=[OVC], compute+postFX gated behind PostFxShaderBundleAvailable=false; 094adcf5 revert(shaders): #208 candidate (un-bundle SVC+POSTFX_KEEP) STILL stubs postFX in player — back to crash-safe; postFX bake unsolved after 3 approaches; a44fc8a3 test(shaders): enable #208 candidate bundle e6589a46 (un-bundled SVC + POSTFX_KEEP) for game-test | STALE-WIP |
| wip/208-ovc-good-bundle | 399 | 0 | 06771bd1 fix(terrain): CreateCachedColors before tilemap.redrawTiles — fixes transpiler null-dict crash (#208); 5553b2ad fix(terrain): force tilemap.redrawTiles() in PrepareWorld to populate biome pixel colors (#208); 48e73992 fix(voxel): bump SettingsVersion 2.7→2.8 + force VoxelEntities=true on load (#208) | STALE-WIP |
| wip/208-water-diag | 402 | 0 | 7506ae3f diag(voxel): log actor/building voxel emit counts (#208); 54dd6b02 diag(terrain): log TileHeight samples + HeightMult (#208); 45f876ee diag(water): document water path + enable if safe (#208) | STALE-WIP |
| wip/deploy-staging-queue | 366 | 0 | d7b70b39 chore: stash prior dirty doc changes on wip branch; 976473a0 fix: gate wsm3d shader bundle load on verified hash; b724b40e fix(postfx): land valid shader bake + re-expand SafeShaders (#204) | EXPERIMENTAL |
| wip/quality-tests | 390 | 0 | 03261702 test+docs(quality): regression coverage + ADR for session fixes (#204 #208 #191); 094adcf5 revert(shaders): #208 candidate (un-bundle SVC+POSTFX_KEEP) STILL stubs postFX in player — back to crash-safe; postFX bake unsolved after 3 approaches; a44fc8a3 test(shaders): enable #208 candidate bundle e6589a46 (un-bundled SVC + POSTFX_KEEP) for game-test | AHEAD-VALUABLE |
| wip/robustness | 391 | 0 | d8dafef5 docs(robustness): wave 2 — ADR status corrections + patch-substrate analysis; 9ffdc80f fix+test+docs(robustness): bloat strip, perf fix, load-path hardening, doc hygiene; 094adcf5 revert(shaders): #208 candidate (un-bundle SVC+POSTFX_KEEP) STILL stubs postFX in player — back to crash-safe; postFX bake unsolved after 3 approaches | AHEAD-VALUABLE |
| remotes/origin/chore/merge-upstream-2026-05-28 | 306 | 14 | 154d95fe Merge remote-tracking branch 'origin/claude/research-ultraplan-fork-DdgI5' into chore/merge-upstream-2026-05-28; bc4f387e Merge pull request #26 from KooshaPari/cursor/build-and-bake-issues-9daa; ec5277a6 fix: resolve three build and bake pipeline bugs | EXPERIMENTAL |
| remotes/origin/claude/research-ultraplan-fork-DdgI5 | 363 | 0 | 2bbec73a Terrain: kill residual rebuild storm + fix night-black lowland (#35); 4ae80093 fix(terrain): resolve OpaqueVertexColor bundle shader for terrain (Gemini #35 — empty bundleName always fell back to Standard); 7169c87d perf+fix(terrain): eliminate residual rebuild re-trigger + lift dark-lowland lighting | EXPERIMENTAL |
| remotes/origin/cursor/gpu-manager-and-editor-layout-d2ec | 371 | 0 | ce8e6efc fix: wire GPU manager shape sync, dirty marks, and null guard; 5665df5f Phase 5 (#199): compute integration — CreateGpuSettings passes CompoundCompute; 64695b45 Phase 4 (#199): BindGpu — push HeightField heights to GPU, re-activate layer | EXPERIMENTAL |
| remotes/origin/cursor/stale-docs-trigger-paths-a41f | 2 | 24 | a9cb7960 fix: update workflow trigger paths to reference docs subdirectory; 0b9061e8 fix: resolve workflow, pre-commit, and repo hygiene issues | EXPERIMENTAL |
| remotes/origin/cursor/versioned-changelog-header-match-2828 | 3 | 11 | cbe6c5f1 fix: match versioned CHANGELOG headers with date suffix; c70c63ed Merge remote-tracking branch 'origin/main' into cursor/workflow-and-repository-issues-cdcb; 0b9061e8 fix: resolve workflow, pre-commit, and repo hygiene issues | EXPERIMENTAL |
| remotes/origin/cursor/workflow-and-repository-issues-cdcb | 2 | 11 | c70c63ed Merge remote-tracking branch 'origin/main' into cursor/workflow-and-repository-issues-cdcb; 0b9061e8 fix: resolve workflow, pre-commit, and repo hygiene issues | EXPERIMENTAL |
| remotes/origin/cursor/world-sphere-logic-bugs-fb1c | 307 | 14 | a20e1b86 fix: correct Y-axis wrapping logic, struct equality check, and ref parameter; 154d95fe Merge remote-tracking branch 'origin/claude/research-ultraplan-fork-DdgI5' into chore/merge-upstream-2026-05-28; bc4f387e Merge pull request #26 from KooshaPari/cursor/build-and-bake-issues-9daa | EXPERIMENTAL |
| remotes/origin/feat/gpu-compute-p4-consumer-migration | 377 | 0 | 039bfcce bench(#199): add GpuComputeBaselineBench — CPU-ref tile transform at N=1024; 17c0b298 docs(adr,#199): mark GPU-compute ADR Accepted — P0-P5 implemented, CI green; 6aa8c8d4 docs(adr+handoff,#199): mark GPU ADR Accepted + add active-track banner | EXPERIMENTAL |
| remotes/origin/fix/live-deprecated | 366 | 0 | 6643985b fix(foliage): remove deprecated 2D billboard/crossed-quad fallback so voxel path is sole renderer; 976473a0 fix: gate wsm3d shader bundle load on verified hash; b724b40e fix(postfx): land valid shader bake + re-expand SafeShaders (#204) | AHEAD-VALUABLE |
| remotes/origin/fix/live-lighting | 366 | 0 | 332c7d8f fix(lighting): create + register directional sun so terrain is lit (RenderSettings.sun was null); 976473a0 fix: gate wsm3d shader bundle load on verified hash; b724b40e fix(postfx): land valid shader bake + re-expand SafeShaders (#204) | AHEAD-VALUABLE |
| remotes/origin/fix/live-shaderload | 366 | 0 | d09e081d fix(shaderload): correct manifest path probe so verified bundle shaders load; 976473a0 fix: gate wsm3d shader bundle load on verified hash; b724b40e fix(postfx): land valid shader bake + re-expand SafeShaders (#204) | AHEAD-VALUABLE |
| remotes/origin/fix/live-voxel | 366 | 0 | e3feca66 fix(voxel): force VoxelEntities on via settings-version bump (2.5->2.6); 976473a0 fix: gate wsm3d shader bundle load on verified hash; b724b40e fix(postfx): land valid shader bake + re-expand SafeShaders (#204) | AHEAD-VALUABLE |
| remotes/origin/fix/postfx-shaders-rebake | 364 | 0 | b724b40e fix(postfx): land valid shader bake + re-expand SafeShaders (#204); 2bbec73a Terrain: kill residual rebuild storm + fix night-black lowland (#35); 4ae80093 fix(terrain): resolve OpaqueVertexColor bundle shader for terrain (Gemini #35 — empty bundleName always fell back to Standard) | AHEAD-VALUABLE |
| remotes/origin/fix/shader-standard-fallback | 35 | 0 | bdf7aa0a feat(bridge): offscreen camera-RT screenshot bypassing debug-console overlay; 31a491f1 feat(capture): input-capture -> learn -> replay substrate (WSM3D side); 58c63236 feat(terrain): wire TERRAIN-OVERLAY snow/tumor/burnt/frozen into 3D land mesh | AHEAD-VALUABLE |
| remotes/origin/fix/terrain-perf-residual-and-lighting | 362 | 0 | 4ae80093 fix(terrain): resolve OpaqueVertexColor bundle shader for terrain (Gemini #35 — empty bundleName always fell back to Standard); 7169c87d perf+fix(terrain): eliminate residual rebuild re-trigger + lift dark-lowland lighting; 74036d63 Reconcile lineages + recover runtime regressions → canonical trunk (#34) | AHEAD-VALUABLE |
| remotes/origin/fix/voxel-flag-force | 377 | 0 | 893682b5 fix(settings): force VoxelEntities=true on migration (2.6→2.7) — stale-false JSON kept voxel actors disabled despite prior version bump; 4c8ef5e1 fix(postfx): master-gate postFX shader-bundle loads off — stub bake native-crashes on GetObject from PostStack/Sky/Cubemap, not just SafeShaders (#204); 10397bda fix(shaders): SafeShaders=OpaqueVertexColor only — postFX shaders are stub-baked + native-crash on load (#204 bake unsolved); restore crash-safety | AHEAD-VALUABLE |
| remotes/origin/fix/worldspace-ui | 377 | 0 | e40215b3 fix(worldspace): healthbar world-anchor + sane scale + restore nameplates (#191); 4c8ef5e1 fix(postfx): master-gate postFX shader-bundle loads off — stub bake native-crashes on GetObject from PostStack/Sky/Cubemap, not just SafeShaders (#204); 10397bda fix(shaders): SafeShaders=OpaqueVertexColor only — postFX shaders are stub-baked + native-crash on load (#204 bake unsolved); restore crash-safety | AHEAD-VALUABLE |
| remotes/origin/integ/live-fixes | 389 | 0 | 094adcf5 revert(shaders): #208 candidate (un-bundle SVC+POSTFX_KEEP) STILL stubs postFX in player — back to crash-safe; postFX bake unsolved after 3 approaches; a44fc8a3 test(shaders): enable #208 candidate bundle e6589a46 (un-bundled SVC + POSTFX_KEEP) for game-test; 1580c77e fix(bake): un-bundle SVC + add keep-keyword to postFX shaders so variants survive AssetBundle strip (#208) | EXPERIMENTAL |
| remotes/origin/integrate/3d-on-research | 359 | 0 | f8119ad3 fix(review): address PR #34 AI-reviewer findings (do-all helper, cross-platform capture path, speed/invoke guards); 27091a41 test(unit): sync stale spec assertions to PR behavior changes; 857fb9fb test+fix: align final 6 E2E tests with landed regression fixes (re-gate land) | EXPERIMENTAL |
| remotes/origin/main | 0 | 0 | no commits ahead of main | MERGED |
| remotes/origin/wip/208-height-fix | 424 | 0 | ee587f5b release: v2.0.0-beta.7; 2cb7038a docs: record quality + robustness lane merges in CANONICAL.md; 582df09a merge(robustness): wave 2 ADR corrections + load-path hardening | EXPERIMENTAL |
| remotes/upstream/main | 11 | 662 | da751bc4 Update README.md; 79bed66e update shader; 19e73d6c update compound spheres | EXPERIMENTAL |

## Verdict counts
- MERGED: 2
- AHEAD-VALUABLE: 20
- STALE-WIP: 9
- EXPERIMENTAL: 19

## Branches to decide on (non-merged)
- claude/research-ultraplan-fork-DdgI5: EXPERIMENTAL (ahead 363, behind 0)
- feat/gpu-compute-p4-consumer-migration: EXPERIMENTAL (ahead 377, behind 0)
- fix/live-deprecated: AHEAD-VALUABLE (ahead 366, behind 0)
- fix/live-lighting: AHEAD-VALUABLE (ahead 366, behind 0)
- fix/live-shaderload: AHEAD-VALUABLE (ahead 366, behind 0)
- fix/live-voxel: AHEAD-VALUABLE (ahead 366, behind 0)
- fix/postfx-shaders-rebake: AHEAD-VALUABLE (ahead 365, behind 0)
- fix/shader-standard-fallback: AHEAD-VALUABLE (ahead 36, behind 0)
- fix/terrain-perf-residual-and-lighting: AHEAD-VALUABLE (ahead 362, behind 0)
- fix/voxel-flag-force: AHEAD-VALUABLE (ahead 377, behind 0)
- fix/worldspace-ui: AHEAD-VALUABLE (ahead 377, behind 0)
- integ/live-fixes: EXPERIMENTAL (ahead 389, behind 0)
- integrate/3d-on-research: EXPERIMENTAL (ahead 359, behind 0)
- rebuild/render-architecture: EXPERIMENTAL (ahead 468, behind 0)
- wip/208-billboard-diag: STALE-WIP (ahead 405, behind 0)
- wip/208-emission-fix: STALE-WIP (ahead 406, behind 0)
- wip/208-fresh-approach: STALE-WIP (ahead 391, behind 0)
- wip/208-fresh-worldsphere-fold: STALE-WIP (ahead 395, behind 0)
- wip/208-full-safe-shaders: STALE-WIP (ahead 394, behind 0)
- wip/208-height-diag: STALE-WIP (ahead 404, behind 0)
- wip/208-height-fix: EXPERIMENTAL (ahead 466, behind 0)
- wip/208-ovc-bundle-on: STALE-WIP (ahead 390, behind 0)
- wip/208-ovc-good-bundle: STALE-WIP (ahead 399, behind 0)
- wip/208-water-diag: STALE-WIP (ahead 402, behind 0)
- wip/deploy-staging-queue: EXPERIMENTAL (ahead 366, behind 0)
- wip/quality-tests: AHEAD-VALUABLE (ahead 390, behind 0)
- wip/robustness: AHEAD-VALUABLE (ahead 391, behind 0)
- remotes/origin/chore/merge-upstream-2026-05-28: EXPERIMENTAL (ahead 306, behind 14)
- remotes/origin/claude/research-ultraplan-fork-DdgI5: EXPERIMENTAL (ahead 363, behind 0)
- remotes/origin/cursor/gpu-manager-and-editor-layout-d2ec: EXPERIMENTAL (ahead 371, behind 0)
- remotes/origin/cursor/stale-docs-trigger-paths-a41f: EXPERIMENTAL (ahead 2, behind 24)
- remotes/origin/cursor/versioned-changelog-header-match-2828: EXPERIMENTAL (ahead 3, behind 11)
- remotes/origin/cursor/workflow-and-repository-issues-cdcb: EXPERIMENTAL (ahead 2, behind 11)
- remotes/origin/cursor/world-sphere-logic-bugs-fb1c: EXPERIMENTAL (ahead 307, behind 14)
- remotes/origin/feat/gpu-compute-p4-consumer-migration: EXPERIMENTAL (ahead 377, behind 0)
- remotes/origin/fix/live-deprecated: AHEAD-VALUABLE (ahead 366, behind 0)
- remotes/origin/fix/live-lighting: AHEAD-VALUABLE (ahead 366, behind 0)
- remotes/origin/fix/live-shaderload: AHEAD-VALUABLE (ahead 366, behind 0)
- remotes/origin/fix/live-voxel: AHEAD-VALUABLE (ahead 366, behind 0)
- remotes/origin/fix/postfx-shaders-rebake: AHEAD-VALUABLE (ahead 364, behind 0)
- remotes/origin/fix/shader-standard-fallback: AHEAD-VALUABLE (ahead 35, behind 0)
- remotes/origin/fix/terrain-perf-residual-and-lighting: AHEAD-VALUABLE (ahead 362, behind 0)
- remotes/origin/fix/voxel-flag-force: AHEAD-VALUABLE (ahead 377, behind 0)
- remotes/origin/fix/worldspace-ui: AHEAD-VALUABLE (ahead 377, behind 0)
- remotes/origin/integ/live-fixes: EXPERIMENTAL (ahead 389, behind 0)
- remotes/origin/integrate/3d-on-research: EXPERIMENTAL (ahead 359, behind 0)
- remotes/origin/wip/208-height-fix: EXPERIMENTAL (ahead 424, behind 0)
- remotes/upstream/main: EXPERIMENTAL (ahead 11, behind 662) [PRs: 4:MERGED, 2:MERGED]

## Existing PRs
- #4: main (MERGED)
- #2: main (MERGED)
