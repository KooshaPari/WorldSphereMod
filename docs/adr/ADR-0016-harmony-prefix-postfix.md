# ADR-0016: Replace `precalculateRenderDataParallel` skip-prefixes with body-level Harmony patches

**Status:** Proposed

**Date:** 2026-05-26

**Author:** Claude / KooshaPari

**Stakeholders:** WorldSphereMod3D voxel render path, Harmony patch consumers, future render/postprocess patches

---

## Context

`WorldSphereMod/Code/Voxel/VoxelRender.cs` currently hooks
`ActorManager.precalculateRenderDataParallel` and
`BuildingManager.precalculateRenderDataParallel` from the `ActorVoxelEmit`
and `BuildingVoxelEmit` patch classes. The current implementation uses
Harmony prefixes that short-circuit the original method with `return false`
and then manually calls `EmitVoxels(...)` before exiting.

That works for the current voxel submission path, but it is architecturally
fragile: any future `HarmonyPostfix` on the same target method will never
run if an earlier prefix returns `false`. The patch therefore creates a
silent extension hazard for any later instrumentation, cleanup, telemetry,
or rendering logic added by this mod or by another mod.

### Problem Statement

Should the voxel render hooks keep using skip-prefixes that manually invoke
`EmitVoxels(...)`, or should they be rewritten so the patch preserves the
original method body semantics and keeps Harmony extensibility intact?

### Forces

- `precalculateRenderDataParallel` is a shared extension point, not a
  private helper.
- `return false` prefixes suppress the original method and all downstream
  postfixes on that target.
- Manual `EmitVoxels(...)` invocation is easy to forget during future
  edits, especially when additional patch logic is added.
- The desired behavior is "modify the method body" rather than "replace the
  method and own the entire hook contract."
- The fix must be durable for future postprocessing, debugging, and
  compatibility patches.

### Alternatives Considered

| Alternative | Pros | Cons | Why not chosen |
|---|---|---|---|
| Keep `Prefix` + `return false` + manual `EmitVoxels(...)` | Works today; simple control flow | Silently blocks all later postfixes; fragile to future edits; easy to regress when method body changes | Too brittle for a shared Harmony hook |
| Convert to `Postfix` only | Preserves original method and downstream postfixes | Runs after the original body, which may be too late if voxel emission must happen before later consumers observe render data | Good for non-intercepting side effects, but not the best fit if we need to alter behavior inside the method flow |
| Convert to `Transpiler` | Preserves method extensibility while still allowing the method body to be rewritten at the exact injection point | More complex than a prefix; requires careful IL maintenance | Best balance of durability and control |

## Decision

Replace the skip-prefix pattern on `precalculateRenderDataParallel` with a
`HarmonyTranspiler`-based patch that rewrites the target method body at the
required injection point instead of returning `false` from a prefix.

The transpiler approach is the preferred architectural fix because it:

- preserves the original Harmony contract for other patches,
- avoids silently skipping postfixes,
- removes the need for manual "call the replacement method then bail out"
  control flow,
- and keeps the behavior localized to the exact instructions that need to
  change.

If the implementation later proves that the voxel work only needs to observe
the completed original method, a `Postfix` is acceptable as a secondary
option. The default recommendation remains `Transpiler` because it is the
only option that both modifies the method body and preserves downstream
patchability.

### Implementation Notes

- Target methods:
  - `WorldSphereMod/Code/Voxel/VoxelRender.cs` `ActorVoxelEmit` on
    `ActorManager.precalculateRenderDataParallel`
  - `WorldSphereMod/Code/Voxel/VoxelRender.cs` `BuildingVoxelEmit` on
    `BuildingManager.precalculateRenderDataParallel`
- Current hazard:
  - prefix short-circuiting with `return false`
  - manual `EmitVoxels(...)` execution in the prefix body
- Required end state:
  - no `return false` prefix behavior on these hooks
  - no reliance on manually simulating the original Harmony call chain
  - downstream postfixes remain callable

## Consequences

### Positive

- Future postfixes on `precalculateRenderDataParallel` will continue to run.
- The patch becomes safer for long-term maintenance and mod compatibility.
- Behavior is expressed as an explicit method-body transformation instead of
  a hidden prefix-side replacement.

### Negative

- The implementation is more complex than a plain prefix.
- Transpilers are harder to read and maintain than a simple prefix/postfix
  pair.
- The IL patch must be kept in sync with upstream method changes.

### Neutral

- The voxel emission logic itself does not need to change for this ADR;
  only the Harmony attachment strategy changes.

## References

- Code anchors: `WorldSphereMod/Code/Voxel/VoxelRender.cs:506`
- Related Harmony policy: `docs/adr/ADR-0007-conditional-patch-dispatch.md`
- Related render path docs: `docs/adr/0015-actor-invisibility-final-root-causes.md`

---

> Phenotype ADR conventions: keep ADRs short (1-3 screens), one decision
> per ADR, link out to architecture / journey docs rather than restating
> them. Status changes (`Accepted` -> `Superseded`) are appended at the top
> with a date; don't delete history.
