# Docs vs Code Drift

Audit date: 2026-05-19

Scope: `README.md`, `docs/HANDOFF.md`, and `docs/PLAN.md` versus the current
phase-gate implementation in `WorldSphereMod/Code/SavedSettings.cs` and the
corresponding Harmony/runtime hooks.

## Findings

| Claim | Code state | Verdict |
|---|---|---|
| `README.md` calls Phase 1 and Phase 2 `landed`. | `SavedSettings.VoxelEntities` and `SavedSettings.ProceduralBuildings` both default `false`, and both phases are behind real patch gates: `VoxelRender.ActorVoxelEmit` / `VoxelRender.BuildingVoxelEmit` plus `BuildingProcRender.ProcMeshEmit`. | Drift. The code exists, but the phases are still opt-in by default. |
| `README.md` calls Phase 4 / 5 / 8 `code-complete`, while the implementation is still off by default. | `SavedSettings.MeshWater`, `SavedSettings.HighShadows`, and `SavedSettings.DayNightCycle` all default `false`. Phase 4 has Harmony Postfix hooks in `WaterRender`; Phase 5 is driven by `SunDriver`/`ShadowCascadeConfig`; Phase 8 is driven by `TimeOfDay`/`ProceduralSky` and the `Mod.Init` ensure-created hooks. | Drift. The code is present, but the docs should mark these phases as opt-in / default-off. |
| `docs/HANDOFF.md` says Phase 4 is default-on. | `SavedSettings.MeshWater` is `false`, and the water phase is gated through `WaterRender` Postfixes. | Drift. |
| `docs/HANDOFF.md` lists `MeshWater` under default-on flags. | `SavedSettings.MeshWater` is `false`. | Drift. It belongs in default-off. |
| `docs/HANDOFF.md` says "All 10 phases are code-complete". | The current code has several default-off phases (`VoxelEntities`, `ProceduralBuildings`, `MeshWater`, `HighShadows`, `DayNightCycle`, `PostFX`) and Phase 6 still has the GPU path intentionally bypassed in `RigDriver`. | Overstatement. Better phrasing is "all 10 phases have code paths in place" plus explicit opt-in/default-off notes. |
| `README.md` uses `in-progress` / `scaffold` language for Phase 6. | `SavedSettings.SkeletalAnimation` defaults `false`, but `RigDriver` already has a bind-pose fallback and a disabled compute path with a clear Step 9 GPU TODO. | Soft drift only. The wording is not wrong, but it understates the amount of code already present. |
| `docs/PLAN.md` has phase-status claims. | `docs/PLAN.md` is only a pointer to the root `PLAN.md` and does not itself contain phase-status entries. | No drift in this file. |

## Evidence

- `WorldSphereMod/Code/SavedSettings.cs:27-27` shows `VoxelEntities = false`.
- `WorldSphereMod/Code/SavedSettings.cs:37-37` shows `ProceduralBuildings = false`.
- `WorldSphereMod/Code/SavedSettings.cs:41-43` shows `MeshWater = false` and `HighShadows = false`.
- `WorldSphereMod/Code/SavedSettings.cs:45-49` shows `SkeletalAnimation = false`, `WorldspaceUI = true`, and `DayNightCycle = false`.
- `WorldSphereMod/Code/SavedSettings.cs:52-53` shows `PostFX = false` and `ParticleEffects = true`.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:284-289` and `WorldSphereMod/Code/Voxel/VoxelRender.cs:440-445` are the Phase 1 gated Postfix hooks.
- `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs:14-21` is the Phase 2 gated Postfix hook.
- `WorldSphereMod/Code/Water/WaterRender.cs:35-103` contains the Phase 4 Postfix gate set.
- `WorldSphereMod/Code/Lighting/SunDriver.cs:18-41`, `WorldSphereMod/Code/Lighting/TimeOfDay.cs:15-55`, `WorldSphereMod/Code/Lighting/ProceduralSky.cs:16-57`, and `WorldSphereMod/Code/Mod.cs:75-81` show the Phase 5 and Phase 8 runtime drivers.
- `WorldSphereMod/Code/Rig/RigDriver.cs:9-15` and `WorldSphereMod/Code/Rig/RigDriver.cs:86-129` show the Phase 6 bind-pose fallback and disabled GPU path.
- `README.md:20-27` contains the phase status rows.
- `docs/HANDOFF.md:12-14` contains the TL;DR overstatement.
- `docs/HANDOFF.md:52-60` contains the phase rows with the default-off mismatches.
- `docs/HANDOFF.md:64-75` contains the default-on/default-off lists.
- `docs/PLAN.md:1-8` is a pointer only, not a source of phase status claims.
