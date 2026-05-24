# Architecture Decision Records

ADRs capture *why* the fork looks the way it does. Each ADR is a single
decision with context, alternatives, and consequences. They are
**append-only**: superseded ADRs are marked, not deleted.

| ID | Title | Status |
|---|---|---|
| [0001](/adr/0001-hybrid-sprite-to-3d-strategy) | Hybrid sprite→3D strategy (voxel actors + procgen buildings + crossed-quad foliage) | Accepted |
| [0002](/adr/0002-defer-shader-bake-to-unity-2022-3) | Defer lit-shader bake to Unity 2022.3 (Phase 5b dependency) | Accepted |
| [0003](/adr/0003-reflective-urp-bindings) | Reflective URP bindings (`ShadowCascadeConfig` + `PostFxController`) | Accepted |
| [0004](/adr/0004-rigid-skinning-over-blended) | Rigid (one-bone-per-vertex) skinning over blended for voxel meshes | Accepted |
| [0005](/adr/0005-default-on-flags-per-phase-ship-gate) | Per-phase `SavedSettings` flag flips default-on only after in-game validation | Accepted |
| [ADR-0007](/adr/ADR-0007-conditional-patch-dispatch) | Conditional Harmony patch dispatch (`[Phase]` + `PhasePatchGate`) | Proposed |
| [0011](/adr/0011-phase-1-visibility-postmortem) | Phase 1 visibility postmortem | Accepted |
| [0012](/adr/0012-phase-2-procedural-not-rendering) | Phase 2 procedural-mesh path silent no-op | Accepted |
| [0013](/adr/0013-flush-gate-silently-drops-foliage) | MeshInstanceBatcher Flush gate silently dropped Phase 3+ submits | Accepted |
| [0014](/adr/0014-autotest-persist-and-tile-dirty) | AutoTest cycle artifacts: persisted toggles + tile-dirty methodology | Accepted |
| [0015](/adr/0015-actor-invisibility-final-root-causes) | Actor invisibility final root causes | Accepted |
| [0016](/adr/0016-phase-1-victory-chain) | Alpha.8 to Alpha.9 victory chain | Accepted |
| [0016](/adr/0016-thread-safe-meshinstancebatcher-submit-deferred-queue) | Thread-safe `MeshInstanceBatcher.Submit` via deferred queue | Accepted |
| [0017](/adr/0017-alpha-9-polish-wave) | Wave-19 outcomes (alpha.9 polish wave) | Accepted |
| [0018](/adr/0018-default-on-flag-cascade) | v2.0.0-alpha.10 phase-flag default-on cascade | Accepted |
| [0020](/adr/0020-wave-19-to-25-summary) | Wave-19 to Wave-25 polish cascade | Accepted |
| [ADR-0020](/adr/ADR-0020-wave-26-follow-up) | Wave-26 follow-up: docs posture + rig inventory | Accepted |

## How to add an ADR

1. Copy [`template.md`](/adr/template) to `docs/adr/NNNN-short-slug.md`.
2. Add it to the table above and to `docs/.vitepress/config.mts` sidebar.
3. Set status to **Proposed**. Open a PR.
4. After review, flip to **Accepted** in the same or a follow-up PR.

If a later ADR supersedes this one, **don't delete it** — change its
status to **Superseded by ADR-NNNN** with the date, and link the new ADR.
