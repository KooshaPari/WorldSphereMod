# ADR Index — WorldSphereMod3D

This is the **root index** of Architecture Decision Records. Per-decision
records live under `docs/adr/`. Each ADR captures *why* the fork looks the
way it does — context, alternatives, consequences — and is append-only:
superseded ADRs are marked, not deleted.

## Active ADRs

| ID | Title | Status | File |
|---|---|---|---|
| ADR-0001 | Hybrid sprite-to-3D strategy (voxel actors + procgen buildings + crossed-quad foliage) | Accepted | [`docs/adr/0001-hybrid-sprite-to-3d-strategy.md`](./docs/adr/0001-hybrid-sprite-to-3d-strategy.md) |
| ADR-0002 | Defer lit-shader bake to Unity 2022.3 (Phase 5b dependency) | Accepted | [`docs/adr/0002-defer-shader-bake-to-unity-2022-3.md`](./docs/adr/0002-defer-shader-bake-to-unity-2022-3.md) |
| ADR-0003 | Reflective URP bindings (`ShadowCascadeConfig` + `PostFxController`) | Accepted | [`docs/adr/0003-reflective-urp-bindings.md`](./docs/adr/0003-reflective-urp-bindings.md) |
| ADR-0004 | Rigid (one-bone-per-vertex) skinning over blended for voxel meshes | Accepted | [`docs/adr/0004-rigid-skinning-over-blended.md`](./docs/adr/0004-rigid-skinning-over-blended.md) |
| ADR-0005 | Per-phase `SavedSettings` flag flips default-on only after in-game validation | Accepted | [`docs/adr/0005-default-on-flags-per-phase-ship-gate.md`](./docs/adr/0005-default-on-flags-per-phase-ship-gate.md) |

## Filename convention

The Phenotype org convention is `ADR-NNN-<kebab-title>.md` (per
`docs/phenotype-conventions.md` §5). The five ADRs above predate the
convention and ship as `NNNN-kebab.md` — renaming would break inbound
links from `docs/adr/index.md` and `docs/.vitepress/config.mts`, so the
existing files are grandfathered. **New ADRs should use
`ADR-NNN-<kebab>.md`** going forward; see
[`docs/adr/template.md`](./docs/adr/template.md) for the canonical
template.

## How to add an ADR

1. Copy [`docs/adr/template.md`](./docs/adr/template.md) to
   `docs/adr/ADR-NNN-<short-slug>.md`.
2. Add a row to the table above (and to `docs/adr/index.md` and
   `docs/.vitepress/config.mts` sidebar).
3. Set status to **Proposed**. Open a PR.
4. After review, flip to **Accepted** in the same or a follow-up PR.

If a later ADR supersedes an earlier one, **don't delete it** — change its
status to **Superseded by ADR-NNN** with the date, and link the new ADR.

## Status legend

- **Proposed** — under review
- **Accepted** — decision in force; code reflects it
- **Superseded by ADR-NNN** — later ADR has replaced this one
- **Deprecated** — no longer applies but kept for historical context
- **Rejected** — considered and not adopted; recorded to prevent re-litigation
