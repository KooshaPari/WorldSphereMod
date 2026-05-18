# Research — WorldSphereMod3D

Index of all investigation findings, decompile artifacts, and prior-art
notes captured while planning the fork. Source files live under `docs/`;
this file is the pointer.

## Investigation findings

| Topic | File | Summary |
|---|---|---|
| Phase 1 voxel pipeline review | [`docs/phase1-review.md`](./docs/phase1-review.md) | 5 review issues found post-implementation; all five fixed (tail-batch tint, yaw-only rotation, deferred destroy, material reset, eviction big-O). |
| Phase 3 decompile pass | [`docs/phase3-decompile-findings.md`](./docs/phase3-decompile-findings.md) | `WorldTilemap.renderTile` call sites + `QuantumSpriteLibrary.drawWallType` signature; informs Phase 3a/3b Prefix targets. |
| `render_data` field mapping | [`docs/render-data-fields.md`](./docs/render-data-fields.md) | Full field map of the parallel-precalculated render-data struct used by `ActorManager`/`BuildingManager.precalculateRenderDataParallel`. |
| Phase 5 prep (lit-shader bake) | [`docs/phase5-prep.md`](./docs/phase5-prep.md) | Unity 2022.3 + `Compound-Spheres-3D` submodule plan; what blocks per-vertex-normal terrain and the cascaded-shadow ship gate. |
| Phenotype conventions baseline | [`docs/phenotype-conventions.md`](./docs/phenotype-conventions.md) | Reference map of recurring patterns across KooshaPari org repos (Dino, PhenoSpecs, phenodocs, TestingKit, phenotype-dep-guard). |
| Phenotype compliance checklist | [`docs/phenotype-baseline.md`](./docs/phenotype-baseline.md) | Per-convention compliance state for this fork. |

## Per-phase architecture

Located at `docs/phase{1..10}-architecture.md`. Each is a phase-scoped
technical spec written before code lands, then updated in-place as ship
state changes.

| Phase | File |
|---|---|
| 1 (Voxel) | [`docs/phase1-review.md`](./docs/phase1-review.md) (review supersedes original arch doc) |
| 2 (ProcGen) | [`docs/phase2-architecture.md`](./docs/phase2-architecture.md) |
| 3 (Foliage) | [`docs/phase3-architecture.md`](./docs/phase3-architecture.md) |
| 4 (Water) | [`docs/phase4-architecture.md`](./docs/phase4-architecture.md) |
| 5 (Lighting) | [`docs/phase5-architecture.md`](./docs/phase5-architecture.md) |
| 6 (Rig) | [`docs/phase6-architecture.md`](./docs/phase6-architecture.md) |
| 7 (Worldspace UI) | [`docs/phase7-architecture.md`](./docs/phase7-architecture.md) |
| 8 (Day/night) | [`docs/phase8-architecture.md`](./docs/phase8-architecture.md) |
| 9 (Fx + decals + PostFX) | [`docs/phase9-architecture.md`](./docs/phase9-architecture.md) |
| 10 (LOD + impostor) | [`docs/phase10-architecture.md`](./docs/phase10-architecture.md) |

## Verification + smoke-test artifacts

| File | Purpose |
|---|---|
| [`docs/smoke-test-phase1.md`](./docs/smoke-test-phase1.md) | In-game smoke-test checklist (gates Phase 1 default-on flip). |
| [`docs/performance.md`](./docs/performance.md) | Performance budgets per system. |
| [`docs/HANDOFF.md`](./docs/HANDOFF.md) | Canonical "next session starts here" doc. |

## User journeys

`docs/journeys/` — 5 user-journey pages backfilled during the Phenotype
retrofit. Each journey is the user-visible verification flow for a phase.
See `docs/phenotype-conventions.md` §6 for journey-ID format
(`us-fN-n-kebab-slug`) and the manifest schema.

## Decompile + reference targets

| Target | Notes |
|---|---|
| `WorldBox.exe` (managed assemblies under `worldbox_Data/Managed/`) | Decompiled with ILSpy/dnSpy. Patches and field accesses cite line numbers from those decompilations; lines drift across WorldBox releases. |
| Upstream `MelvinShwuaner/WorldSphereMod` | Source: github.com/MelvinShwuaner/WorldSphereMod. Inherited files (`Core.cs`, `QuantumSprites.cs`, `Tools.cs`, etc.) tracked in `CLAUDE.md` §"What's a fork-specific concern vs. upstream". |
| Upstream `MelvinShwuaner/Compound-Spheres` | Terrain backend source. Phase 5 plan forks this to `External/Compound-Spheres-3D/`. |
| NeoModLoader docs | Mod loading + runtime-compile semantics. |

## External references

- Harmony patching: `pardeike/Harmony` documentation
- Greedy meshing reference: Mikola Lysenko's "Meshing in a Minecraft Game"
- Gerstner waves: Tessendorf's "Simulating Ocean Water" (FFT and Gerstner sections)
- Hosek-Wilkie sky model: original SIGGRAPH paper (Phase 8 keyword reserved, not yet implemented)
- URP cascaded shadow maps: Unity 2022.3 LTS documentation

## State of the art

See [`SOTA.md`](./SOTA.md) for the competitive landscape (upstream
WorldSphereMod, HaxeBox-style ports, total-conversion forks).
