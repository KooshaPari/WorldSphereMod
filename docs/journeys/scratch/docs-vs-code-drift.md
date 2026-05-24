# Docs vs Code Drift

**Status: Resolved** (re-audited 2026-05-23)

Original audit: 2026-05-19 (pre–ADR-0018 default-on cascade). At that time
`SavedSettings` still defaulted several phase flags to `false` while
`README.md` and `docs/HANDOFF.md` described them as landed or default-on.
The v2.0.0-beta default-on cascade ([ADR-0018](/adr/0018-default-on-flag-cascade))
aligned `WorldSphereMod/Code/SavedSettings.cs` with README, HANDOFF, and PRD.
`docs/PLAN.md` remains a pointer to root `PLAN.md` only (no phase-status table).

## Re-audit (2026-05-23)

Scope: `README.md`, `docs/HANDOFF.md`, `docs/PLAN.md` vs
`WorldSphereMod/Code/SavedSettings.cs` and `HandoffDefaultsAlignmentTests`.

| Claim | Code state | Verdict |
|---|---|---|
| README phase table cites live `SavedSettings` defaults for phases 1–9. | Phase flags default `true` except `SSGIEnabled = false`; README rows cite matching `= true` / `= false` literals. | **Aligned** |
| HANDOFF “Current defaults matrix” matches `SavedSettings.cs`. | Matrix and category lists match bool/float defaults; `MeshWater` under Default-on. | **Aligned** (guarded by E2E `Handoff_defaults_matrix_matches_SavedSettings_cs`) |
| HANDOFF phase rows vs defaults. | Rows document `= true` for shipped phases; smoke-test notes still distinguish verification from defaults. | **Aligned** |
| README beta line: “all 10 phases code-complete, default-on behavior active”. | Code paths exist; phase flags default-on per ADR-0018; Phase 6 GPU compute path still intentionally disabled in `RigDriver` (bind-pose fallback). | **Acceptable** — “code-complete” refers to wired paths, not every GPU sub-path |
| `docs/PLAN.md` phase-status claims. | Pointer only to `/PLAN.md`; no conflicting status table. | **No drift** |
| Stale inline comment on `VoxelEntities`. | Comment previously said “Defaults OFF” while field was `true`. | **Fixed** 2026-05-23 |

## Intentional default-off (unchanged)

These remain `false` in `SavedSettings.cs` and are documented under HANDOFF
“Default-off / opt-in”: `SSGIEnabled`, `WeatherSnow`, `WeatherLightning`,
plus non-phase flags (`UseBRG`, `ForwardPlusRenderer`, `VoxelMeshSmoothing`,
`BuildingStyleProcgen`, etc.).

## Evidence (current)

- `WorldSphereMod/Code/SavedSettings.cs` — `VoxelEntities` through `DayNightCycle` default `true`; `SSGIEnabled` default `false`.
- `README.md` — phase table rows 20–28 cite matching defaults.
- `docs/HANDOFF.md` — “Current defaults matrix” and Default-on/off categories.
- `tests/WorldSphereMod.Tests.E2E/HandoffDefaultsAlignmentTests.cs` — matrix, category, and README Phase 4 guards.

## Resolution actions (2026-05-23)

1. Confirmed README / HANDOFF / PRD already match post-cascade `SavedSettings`.
2. Corrected stale “Defaults OFF” comment on `VoxelEntities`.
3. Marked this audit **Resolved**; use `HandoffDefaultsAlignmentTests` in CI to prevent regression.
