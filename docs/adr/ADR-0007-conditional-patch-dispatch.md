# ADR-0007: Conditional Harmony patch dispatch

**Status:** Proposed

**Date:** 2026-05-23

**Author:** WorldSphereMod maintainers

**Stakeholders:** Harmony patch authors, phase-toggle UX (`WorldSphereTab`), CI E2E invariants

---

## Context

WorldSphereMod applies a large Harmony surface at `Core.Patch()`. Many detour classes belong to optional render phases gated by boolean fields on `SavedSettings` (`VoxelEntities`, `ProceduralBuildings`, …). Applying IL detours for disabled phases wastes init time (~80–150 ms per skipped phase) and increases compatibility risk with vanilla 2D paths.

Runtime phase toggles already route through `PhasePatchManager.ApplyPhaseToggle`, but init-time discovery and the gate predicate were duplicated inline in `Core.Patch()`.

### Problem Statement

How should Harmony patch types declare phase membership, and where should the “apply this patch now?” predicate live so init dispatch and runtime toggle stay consistent?

### Forces

- **Performance:** Skip patching when the phase flag is off at mod init.
- **Consistency:** Init gate and `PhasePatchManager` must agree on which types belong to which flag.
- **Discoverability:** `[Phase(nameof(SavedSettings.SomeFlag))]` on patch classes is the existing convention.
- **Compatibility:** Unconditional patches (no `[Phase]`) must still patch at init.

### Alternatives Considered

| Alternative | Pros | Cons | Why not chosen |
|---|---|---|---|
| `PatchAll` on whole assemblies | Simple | Patches disabled phases; slow init | Rejected |
| Per-phase Harmony IDs | Isolated unpatch | Fragmented IDs; harder toggling | Rejected |
| `[Phase]` + central `PhasePatchGate` | Single predicate; testable | Small refactor | **Chosen (scaffold)** |

## Decision

**Keep `[Phase]` on Harmony patch classes and centralize the init-time gate in `PhasePatchGate.ShouldApplyHarmonyPatch(Type, SavedSettings)`.** `Core.Patch()` consults that helper before `CreateClassProcessor(type).Patch()`. Types with `[HarmonyPatch]` and a matching enabled phase flag are patched and registered via `PhasePatchManager.MarkTypePatched`.

### Implementation Notes

- `WorldSphereMod/Code/PhasePatchGate.cs` — gate helper (scaffold).
- `WorldSphereMod/Code/PhaseAttribute.cs` — unchanged attribute contract.
- `WorldSphereMod/Code/Core.cs` — init loop uses `PhasePatchGate`.
- `WorldSphereMod/Code/PhasePatchManager.cs` — runtime apply/unpatch (unchanged behavior).
- E2E: `ConditionalPatchDispatchInvariantsTests` guards ADR + source wiring.

### Roll-out

1. Land scaffold + invariants (this ADR **Proposed**).
2. After smoke on safe-min / per-phase toggles, flip status to **Accepted**.
3. Optional follow-up: route `PhasePatchManager.GetPhaseTypes` filtering through the same helper.

## Consequences

### Positive

- One place to evolve gate rules (e.g. `IsWorld3D` preconditions).
- E2E can lock dispatch wiring without running the game.

### Negative

- Extra indirection in the hot init loop (negligible vs Harmony patch cost).

### Neutral

- NML precompiled detection remains documented in `ADR-0007-nml-precompiled-detection.md` (separate concern, same numeric prefix legacy).

## References

- `WorldSphereMod/Code/PhaseAttribute.cs`
- `WorldSphereMod/Code/Core.cs` — `Patch()`
- `WorldSphereMod/Code/PhasePatchManager.cs`
- [ADR 0005 — Default-off flags per phase](/adr/0005-default-on-flags-per-phase-ship-gate)
- `docs/HANDOFF.md` — recommended next steps
