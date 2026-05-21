# ADR 0016: Alpha.8 to Alpha.9 Victory Fix Chain

## Status
Accepted

## Context
This ADR records the fix chain from alpha.8 to alpha.9 and the deterministic validation path used to stabilize Phase 1 rendering behavior. The scope is the cumulative rendering+automation hardening pass introduced in this chain, including:
- 7 cumulative material-system fixes
- 12 transpiler guards
- Y-lift adjustments
- LOD pipeline progression
- Emission progression hardening

## Decision
Ship the alpha.9 cumulative chain as a gated progression against the alpha.8 baseline, preserving behavior while removing fragile render-path and patch-time failure classes introduced during the transition.

## Commit chain and impact
- Replace material swap and render-state assumptions in one cumulative sequence to keep the fix surface in `WorldSphereMod` coherent from alpha.8 to alpha.9.
- Apply transpiler-side defensive guards before mutating material/renderer state to avoid invalid IL-time side effects.
- Land Y-lift and LOD behavior changes in the same release train to avoid reintroducing clipping/visibility regressions.
- Keep emission progression changes coupled with LOD progression so emissive artifacts do not regress in long-running scenes.

## Cumulative material fixes (7)
1. **Material lifetime stabilization**
   - Prevent unstable material reuse and ensure references are consistently normalized before runtime writes.
   - Commit: `<COMMIT_SHA_1>`
2. **Shader keyword normalization**
   - Enforce deterministic keyword enable/disable ordering on affected materials.
   - Commit: `<COMMIT_SHA_2>`
3. **Texture slot mapping hardening**
   - Fix texture/property index mapping paths that were misaligned during alpha.8 migrations.
   - Commit: `<COMMIT_SHA_3>`
4. **RenderQueue / sorting-order stabilization**
   - Stabilize queue and sort-order state updates when patching renderer material states.
   - Commit: `<COMMIT_SHA_4>`
5. **Material property defaulting**
   - Ensure default fallback values are applied when source data is absent or transient.
   - Commit: `<COMMIT_SHA_5>`
6. **Material cache invalidation**
   - Invalidate stale cached material-derived state during object lifecycle transitions.
   - Commit: `<COMMIT_SHA_6>`
7. **Renderer state propagation**
   - Keep material propagation consistent through parent/child and pooled renderer flows.
   - Commit: `<COMMIT_SHA_7>`

## Transpiler guards (12)
1. Guard against null enumerable inputs before entering emission/transforms loops — `<COMMIT_SHA_8>`
2. Guard against zero-length material arrays in transpiled call sites — `<COMMIT_SHA_9>`
3. Guard against non-UnityEngine types at dynamic material-write boundaries — `<COMMIT_SHA_10>`
4. Guard against out-of-range index access on renderer/material collections — `<COMMIT_SHA_11>`
5. Guard against missing component assumptions in Harmony transpiler entrypoints — `<COMMIT_SHA_12>`
6. Guard against patched method signature drift across versions — `<COMMIT_SHA_13>`
7. Guard against empty shader tags before keyword mutation — `<COMMIT_SHA_14>`
8. Guard against null property IDs before setter emission — `<COMMIT_SHA_15>`
9. Guard against duplicate transpiler application on already-patched methods — `<COMMIT_SHA_16>`
10. Guard against non-canonical material handles after API de-synchronization — `<COMMIT_SHA_17>`
11. Guard against stale references inside enumerator rewrites — `<COMMIT_SHA_18>`
12. Guard against runtime null on late-bound bridge callback targets — `<COMMIT_SHA_19>`

## Y-lift, LOD, and emission progression
- **Y-lift progression:** adjust vertical lift offsets to reduce baseline world seam and terrain interaction artifacts while preserving existing camera and culling assumptions.
  - Commit: `<COMMIT_SHA_20>`
- **LOD progression:** sequence LOD selection and render quality scaling with stable transition thresholds, then tie transitions to guard logic.
  - Commit: `<COMMIT_SHA_21>`
- **Emission progression:** unify emissive intensity and keyword/parameter lifecycle so emission states remain monotonic across scene lifecycle transitions.
  - Commit: `<COMMIT_SHA_22>`

## 7-step alpha.8 victory audit checklist
1. Validate base/parent material integrity pre-patch.
2. Replay transpiler sequence for each targeted method.
3. Confirm null/empty guards are present at every material boundary.
4. Validate Y-lift deltas under seam and terrain edge scenarios.
5. Validate LOD transition continuity and no pop/noise regressions.
6. Verify emission progression state machine across load/unload transitions.
7. Run end-to-end alpha.8→alpha.9 pass with zero regression deltas.

## References
- Project memory: `project_wsm3d_alpha8_victory` (7-step checklist source)
- `docs/adr/0016-phase-1-victory-chain.md` as the canonical ADR location for this chain
