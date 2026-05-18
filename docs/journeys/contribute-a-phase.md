# Journey: Contribute a phase

**Persona:** A C# / Unity developer who wants to ship code for one of the
remaining phases (or a refinement on a landed phase).
**Time:** Hours to weeks depending on phase scope.
**Prerequisites:** A local WorldBox install (build needs reference DLLs),
.NET 8 SDK, optional Unity 2022.3 (needed for AssetBundle / shader work on
Phase 5b).

## Goal

Pick a phase from the 10-phase plan, implement it in-place using the
fork's conventions, get the per-phase `SavedSettings` flag flipped to
default-on, and ship a PR against `claude/research-ultraplan-fork-DdgI5`.

## Steps

1. **Cold-start orientation.** Read in this order:
   1. [`CLAUDE.md`](https://github.com/KooshaPari/WorldSphereMod/blob/main/CLAUDE.md) at the repo root — conventions, pitfalls.
   2. [`HANDOFF`](/HANDOFF) — the live state of every phase, what's blocked.
   3. [`PLAN`](/PLAN) — the canonical phase-by-phase plan with file paths and verification steps.

2. **Pick a phase.** Skim [`HANDOFF`](/HANDOFF) phase table. Phases marked
   `🔄 awaits smoke test` or `research` are the best candidates for new
   contributors; `landed` phases only accept refinements/bugfixes.

3. **Open the corresponding architecture doc.** Each phase has a dedicated
   architecture page (e.g. [phase 2](/phase2-architecture),
   [phase 5](/phase5-architecture), [phase 6](/phase6-architecture)). These
   are the spec; deviations need an ADR.

4. **Local build setup.**
   ```bash
   export WORLDBOX_PATH="$HOME/.steam/steam/steamapps/common/worldbox"   # or
   $env:WORLDBOX_PATH = "C:/Program Files (x86)/Steam/steamapps/common/Worldbox"
   dotnet build WorldSphereMod.csproj -c Release
   ```
   The `WorldSphereAPI.csproj` (netstandard2.0) builds in CI without
   WorldBox DLLs — keep API changes buildable there.

5. **Find the right place to make changes.** From `CLAUDE.md`:

   | You want to… | Look in |
   |---|---|
   | Add a render-mode flag | `WorldSphereMod/Code/SavedSettings.cs` |
   | Add a public API method | both `WorldSphereAPI/WorldSphereAPI.cs` (external) *and* `WorldSphereMod/Code/WorldSphereAPI.cs` (internal) |
   | Add a per-frame driver | new `MonoBehaviour`; `AddComponent` to `Mod.Object` in `Mod.Init`. Pattern: `Voxel/VoxelFrameDriver` |
   | 2D↔3D coords | `Tools.To3D` / `Tools.To2D` — never re-derive |
   | Mesh draw | `Voxel/MeshInstanceBatcher.Submit(...)` then `Flush()` once per frame |
   | Voxelize a sprite | `Voxel/VoxelMeshCache.Get(sprite)` |

6. **Write the code.** Keep the existing sprite-billboard path as fallback
   behind the `SavedSettings` flag. New phases ship **default-OFF**.

7. **Wire up the flag.** Add a `SavedSettings` field; expose a toggle in
   `WorldSphereTab.cs`; gate the new code path on it.

8. **Verify in-game.** Each architecture doc has a "Verify" section. Walk
   through it. Capture before/after screenshots for the PR.

9. **Open a PR.** Single phase per PR. Push to
   `claude/research-ultraplan-fork-DdgI5` (or a fresh branch off it), not
   `main`. Use the [PR checklist](/PR_CHECKLIST). Mark as draft until
   in-game smoke test passes.

10. **CodeRabbit + review.** CI builds only the API project — that must be
    green. The rest is human review.

11. **Ship gate.** Once a real human (or agent in a desktop session) has
    smoke-tested the in-game behavior, flip the flag to default-on
    ([ADR-0005](/adr/0005-default-on-flags-per-phase-ship-gate)) in a
    follow-up commit. Update the `README.md` phase table from
    `code-complete` to `landed`.

## Outcome

A phase ships. The settings flag is on by default for new users. The
architecture doc gains a "Status: landed" footer with the PR link.

## Variants

- **Refinement on a landed phase** (e.g. fixing a foliage flicker): same
  flow, but the flag is already on; you may need to add a sub-flag if the
  change is risky.
- **API-only change** (new method on `WorldSphereAPI`): you can ship CI-only
  (no WorldBox needed). Both the external delegate file and internal
  implementation file must change in the same PR.
- **External submodule work** (Phase 5b, `Compound-Spheres-3D`): you'll
  need Unity 2022.3 locally; AssetBundle bake is the bottleneck. See
  [phase 5 prep](/phase5-prep) and [ADR-0002](/adr/0002-defer-shader-bake-to-unity-2022-3).

## Pitfalls

- **Don't naively add Z**. `Constants.ZDisplacement = 100` is a sentinel
  used to mark "already converted to 3D". Re-converting a position
  silently breaks render order.
- **X wraps cylindrically** when `CurrentShape == 0`. Use `Tools.Dist` /
  `Tools.WrappedDist`, never `Vector3.Distance`.
- **Parallel render passes**. `precalculateRenderDataParallel` runs on a
  worker pool. Postfix code runs *after* the join, but be explicit about
  thread-safety in any state you mutate.
- **Patcher.PatchAll()** picks up new `[HarmonyPatch]` types automatically
  from anywhere under `Code/`. Putting a misplaced patch in a misnamed file
  still patches.
