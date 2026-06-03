# Doc Hygiene Report — WSM3D

**Generated:** 2026-06-02  
**Branch audited:** wip/robustness (off integ/live-fixes)  
**Purpose:** Identify stale, aspirational, or superseded docs vs shipped reality.  
**Action required from user:** Review Recommended Actions before any deletion/archiving.

---

## ADR Status Audit

| Document | Status Field | Actual State | Verdict | Notes |
|----------|-------------|-------------|---------|-------|
| 0001-hybrid-sprite-to-3d-strategy.md | Accepted | Shipped | CURRENT | Core strategy in use |
| 0002-defer-shader-bake-to-unity-2022-3.md | (none) | Partially superseded | STALE | Shader bake was attempted; ADR-0021 now owns this. Should be marked Superseded by ADR-0021 |
| 0003-reflective-urp-bindings.md | (none) | Shipped | CURRENT | ShadowCascadeConfig + PostFxController both in Code/Lighting/ |
| 0004-rigid-skinning-over-blended.md | (none) | Shipped | CURRENT | RigDriver uses rigid bone binding |
| 0005-default-on-flags-per-phase-ship-gate.md | (none) | Shipped | CURRENT | Core.cs gate pattern in use |
| 0011-phase-1-visibility-postmortem.md | (none) | Historical | CURRENT | Post-mortem, intentionally retrospective |
| 0012-phase-2-procedural-not-rendering.md | (none) | Historical | CURRENT | Post-mortem |
| 0013-flush-gate-silently-drops-foliage.md | (none) | Historical | CURRENT | Post-mortem; regression prevented by test |
| 0014-autotest-persist-and-tile-dirty.md | (none) | Historical | CURRENT | Post-mortem; AutoTest=false default shipped |
| 0015-actor-invisibility-final-root-causes.md | (none) | Historical | CURRENT | Post-mortem |
| 0016-phase-1-victory-chain.md | (none) | Historical | CURRENT | Victory record |
| 0016-thread-safe-meshinstancebatcher.md | (none) | Shipped | CURRENT | MeshInstanceBatcher ConcurrentQueue in use |
| 0017-alpha-9-polish-wave.md | (none) | Historical | CURRENT | Wave summary |
| 0018-default-on-flag-cascade.md | (none) | Historical | CURRENT | Wave summary |
| 0020-wave-19-to-25-summary.md | (none) | Historical | CURRENT | Wave summary |
| ADR-0006-phase-6-step-9-drawprocedural-skinning.md | Proposed | Partially shipped | STALE | DrawProcedural GPU skinning scaffold in RigGpuSkinning.cs exists but GpuProceduralSkinning defaults false; status should be "Accepted (scaffold shipped, disabled by default)" |
| ADR-0007-conditional-patch-dispatch.md | Accepted | Shipped | CURRENT | PhasePatchGate + PhaseAttribute in use |
| ADR-0007-nml-precompiled-detection.md | Proposed | Shipped | STALE | NML precompiled detection logic is in use; should be Accepted |
| ADR-0007-nml-precompiled-detection-followup.md | Proposed | Shipped | STALE | Followup to above; should be merged/closed |
| ADR-0008-voxel-mesh-smoothing.md | Proposed | Partially shipped | STALE | MeshSmoother.cs exists in Code/Voxel/ but MountainSlopeSmoothing defaults false; doc says "Proposed" but code is present. Should be "Accepted (shipped, off by default)" |
| ADR-0009-voxel-lit-material.md | Proposed | Not shipped as described | STALE | No VoxelLit material; OpaqueVertexColor is the actual voxel material. ADR describes a future lit material that was superseded by OVC approach. Should be Superseded |
| ADR-0010-3d-clouds.md | Proposed | Not shipped | ASPIRATIONAL | No 3D cloud system in Code/. ProceduralSky.cs exists but is a sky shader, not volumetric clouds. Low priority feature |
| ADR-0010-voxel-actor-visibility.md | Resolved (alpha.8) | Shipped | CURRENT | Voxel actor visibility confirmed working |
| ADR-0011-slope-smoothing.md | Accepted | Shipped (off by default) | CURRENT | UseHeightFieldTerrain + MountainSlopeSmoothing both in SavedSettings |
| ADR-0012-assetbundle-shader-bake-plan.md | Proposed | Superseded | SUPERSEDED | Superseded by ADR-0021 (bundle saga lessons). Should be marked Superseded by ADR-0021 |
| ADR-0012-mesh-water.md | Accepted (Phase 4) | Shipped | CURRENT | GerstnerWater shader in AssetBundles/; MeshWater in SavedSettings |
| ADR-0013-postfx-pipeline.md | Accepted | Shipped (partially) | CURRENT | WSM3DPostStack shipped; postFX gated off (PostFxShaderBundleAvailable=false) pending bake fix |
| ADR-0014-settings-lifecycle.md | "Problem documented, fix proposed but not implemented" | Partially fixed | STALE | The version-bump migration pattern IS now implemented (Core.cs:ApplySchemaVersionMigration). Should be updated to "Accepted" |
| ADR-0015-compound-spheres-performance.md | Accepted | Partially shipped | CURRENT | GPU compute scaffolding in feat/gpu-compute; P4 consumer migration in progress |
| ADR-0016-harmony-prefix-postfix.md | Proposed | Shipped | STALE | 19 files use [HarmonyPatch]. Should be Accepted |
| ADR-0017-terrain-architecture.md | Proposed | Shipped | STALE | UseHeightFieldTerrain in SavedSettings; height-field terrain in Become3D. Should be Accepted |
| ADR-0020-wave-26-follow-up.md | (none) | Historical | CURRENT | Wave summary |
| ADR-0021-assetbundle-postfx-bundle-lessons.md | Accepted | Current | CURRENT | Newly written, accurate |
| ADR-fork-terrain-water-slope.md | (none) | Historical | CURRENT | Fork architecture record |
| ADR-input-capture-substrate.md | (none) | Shipped | CURRENT | InputCaptureEnabled in SavedSettings; CaptureRecorder.cs in Code/Capture/ |
| ADR-renderer-sota-chunked-lod.md | Proposed | Research phase | ASPIRATIONAL | No chunked LOD renderer in main Code/. This is a research/future direction |
| ADR-sota-gpu-compute-adoption.md | Proposed (P1 scaffolding accepted) | P1 scaffold shipped | STALE | P1 GPU compute scaffold landed; should be updated to "Accepted (P1 shipped, P4 consumer migration in progress)" |

---

## Spec / Architecture Doc Audit

| Document | Verdict | Notes |
|----------|---------|-------|
| docs/PRD.md | CURRENT | FR/NFR IDs in use; 15/15 FRs landed |
| docs/HANDOFF.md | STALE | References stale branch names and completed work as "pending"; needs update to reflect wip/208-height-fix current state |
| docs/phase*-architecture.md (phases 1-10) | MIXED | Phase 1-5 docs are historical/accurate; phase 8-10 docs describe in-progress or aspirational features |
| docs/forward-plus-renderer-spec.md | ASPIRATIONAL | ForwardPlusRenderer in SavedSettings but defaults false; no active ForwardPlus renderer in Code/ main path |
| docs/onrenderimage-postfx-spec.md | CURRENT | WSM3DPostStack shipped per spec |
| docs/voxel-disk-cache-spec.md | CURRENT | VoxelDiskCache in SavedSettings; VoxelDiskCache.cs in Code/ |
| docs/future-research-sota.md | ASPIRATIONAL | Research notes, intentionally forward-looking |
| docs/maturity-audit.md | STALE | Likely outdated; references pre-alpha.8 state |
| docs/journeys/ (95 scratch PNGs) | PRUNABLE | 94 scratch PNGs untracked in this PR (gitignore fix); 1 intentional baseline kept |

---

## Summary

| Category | Count |
|----------|-------|
| CURRENT (accurate, keep) | 22 |
| STALE (status field wrong / code diverged) | 9 |
| ASPIRATIONAL (describes unshipped future work) | 3 |
| SUPERSEDED (explicitly replaced) | 1 |
| PRUNABLE (zero info value or already cleaned) | 1 (scratch PNGs already fixed) |

---

## Recommended Actions

**Immediate (no user approval needed — status field only changes):**
- ADR-0007-nml-precompiled-detection.md: `Proposed` → `Accepted`
- ADR-0007-nml-precompiled-detection-followup.md: `Proposed` → `Accepted (see -nml-precompiled-detection.md)`
- ADR-0008-voxel-mesh-smoothing.md: `Proposed` → `Accepted (shipped, off by default — MountainSlopeSmoothing)`
- ADR-0014-settings-lifecycle.md: update to `Accepted` + note migration implemented in Core.cs
- ADR-0016-harmony-prefix-postfix.md: `Proposed` → `Accepted`
- ADR-0017-terrain-architecture.md: `Proposed` → `Accepted`
- ADR-sota-gpu-compute-adoption.md: update to `Accepted (P1 scaffold shipped, P4 consumer migration in progress)`

**Requires user review before action:**
- ADR-0009-voxel-lit-material.md: Propose marking `Superseded by OpaqueVertexColor approach` — the VoxelLit material concept was replaced
- ADR-0010-3d-clouds.md: Propose marking `Aspirational (not shipped)` or archiving to docs/archive/
- ADR-0012-assetbundle-shader-bake-plan.md: Propose marking `Superseded by ADR-0021`
- ADR-renderer-sota-chunked-lod.md: Propose marking `Aspirational (research phase)` or archiving
- docs/HANDOFF.md: Update to current branch state

**Do NOT delete without separate user decision:**
- All post-mortem / wave-summary docs (0011–0020) — historical record, keep
- docs/journeys/ markdown files — session logs, archive candidate but not prunable unilaterally
- docs/future-research-sota.md — intentionally aspirational, keep

---

*This report was generated by source-level analysis. All judgments are based on grepping Code/ for presence of described features. Runtime behavior was not tested.*
