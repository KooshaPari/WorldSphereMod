# WSM3D Lineage Reconciliation — Merge Plan

**Date:** 2026-05-31
**Problem:** Four branches forked from a single common ancestor (`0c1f11e9`) and never re-merged, so work has diverged into parallel lineages. None can be deleted (all hold unique unmerged work); none may be squashed (we must preserve both histories).

## Divergence facts (vs `fix/shader-standard-fallback`, measured)

All four share merge-base `0c1f11e9`.

| Branch | Ahead of base | Unique vs `research` | Character of the work |
|---|---:|---:|---|
| `fix/shader-standard-fallback` (THIS session) | 49 | — (separate lineage) | 3D conversion: fork pivot (CompoundSpheres HeightField water/slope/liquids/overlays), crossed-quad eliminated → all-3D, GPU-compute adoption scaffold, headless bridge + camera-RT capture, /diag typed errors, spawn-persistence, input-capture substrate, rig stable-disable |
| `claude/research-ultraplan-fork-DdgI5` (documented mainline) | 308 | baseline | voxel-visibility line (emission override, VoxelScaleMultiplier=8.0, "fixed walls!"), runtime TDD harness (8 behavioral tests), CI voxel-regression workflow, GraphicRaycaster F9/F10, MeshInstanceBatcher stale-object crash fix, Become3D-on-save-load |
| `cursor/world-sphere-logic-bugs-fb1c` | 307 | **13 unique** | ~294 shared with research + 13 unique real bug fixes: Y-axis wrap in WrappedMoveTowards (used dx not dy), IsWrapped delegate-vs-struct ref compare, missing `ref` on pHeight in PrepareShape, F8 DebugHUD, ambient/skybox, Drop frustum-cull |
| `chore/merge-upstream-2026-05-28` | 306 | **0 unique** vs logic-bugs | strict subset of logic-bugs (1↔0). No independent value. |

### Key realization
The three "big" branches are **not** three independent bodies of work. `research`↔`logic-bugs` differ by only **14↔13** commits (they share ~294). `chore` is fully contained in `logic-bugs`. So there are really **TWO distinct bodies to reconcile**, plus one small cherry-pick set:

1. **`research` lineage** (the 308-mainline, ≈ research ∪ logic-bugs ∪ chore for the shared 294)
2. **`fix/shader-standard-fallback`** (this session's 49 — the actual 3D conversion + fork)
3. **+ `logic-bugs`'s 13 unique commits** (the Y-wrap/IsWrapped/ref bug fixes — small, valuable, grafted in)

## Recommended plan — `research` as canonical base, merge `fix/` into it

`research` is the documented mainline and the larger lineage (TDD harness + CI are infrastructure we want to keep running). `fix/` is the visually-verified 3D conversion. Make `research` the trunk and bring `fix/` in as a **real merge commit** (never squash — preserve both histories).

### Phase 0 — Safety + freeze (no history rewrite)
- [ ] Tag both tips before touching anything: `git tag pre-reconcile/fix fix/shader-standard-fallback` and `git tag pre-reconcile/research origin/claude/research-ultraplan-fork-DdgI5`. Push tags. (Recovery anchor.)
- [ ] Confirm no in-flight agent has uncommitted work in either tree (`git status` clean on both). Currently 2 agents owe commits: terrain-cube diagnosis (a53e10f) and bridge-param fix (a49dc died — re-dispatch). **Land/abandon those before the merge** so we don't merge a half-edited tree.

### Phase 1 — Integration branch
- [ ] `git checkout -b integrate/3d-on-research origin/claude/research-ultraplan-fork-DdgI5` (work on a throwaway integration branch, not on either real branch, so a bad merge is `git checkout -` away).

### Phase 2 — Merge `fix/` into the integration branch
- [ ] `git merge --no-ff fix/shader-standard-fallback` (real merge commit, preserves both histories).
- [ ] Resolve conflicts. **Expected hot spots** (both lineages touched these): `Core.cs` (research's voxel-visibility VoxelScaleMultiplier=8.0 + emission override vs fix/'s ConfigureHeightField/liquids/overlays wiring), `Voxel/VoxelRender.cs` (research's emission/scale vs fix/'s voxel-or-invisible + ResolveActorSprite), the submodule pointer `External/Compound-Spheres` (fix/ advanced it to 52fe656 — KEEP fix/'s, it has the fork water/liquids/overlays), CI workflow files (keep research's), and any shader/SafeShaders region. Resolution rule: **take fix/'s renderer/fork side, take research's TDD-harness + CI + voxel-visibility tuning, union where additive.**
- [ ] Build after resolution: `dotnet build WorldSphereMod.csproj -c Release` → 0 errors.

### Phase 3 — Graft `logic-bugs`'s 13 unique commits
- [ ] `git log --oneline origin/cursor/world-sphere-logic-bugs-fb1c ^origin/claude/research-ultraplan-fork-DdgI5` to list the 13 unique.
- [ ] Cherry-pick the genuine bug fixes (Y-wrap, IsWrapped, pHeight `ref`, F8 HUD, ambient/skybox, Drop cull). Skip any that conflict with fix/'s renderer rewrite (the Drop frustum-cull may already be superseded — check).

### Phase 4 — Verify on the integrated tree
- [ ] Run research's runtime TDD harness (the 8 behavioral tests) — they now exercise fix/'s renderer. Fix breaks.
- [ ] Run the camera-RT vision-verify (new_world → spawn → `?mode=camera` capture → read pixels): confirm all-3D voxel actors + (pending #201) smooth HeightField terrain + no magenta + diag total:0.
- [ ] CI green on the integration branch.

### Phase 5 — Land
- [ ] Fast-forward `claude/research-ultraplan-fork-DdgI5` to the integration branch (or open a PR integration→research for human review). This becomes the single canonical trunk.
- [ ] `chore/merge-upstream-2026-05-28`: 0 unique value → safe to close its PR (#33 per memory) WITHOUT deleting the branch (user said never auto-delete).
- [ ] `cursor/world-sphere-logic-bugs-fb1c`: its 13 unique are now grafted → its PR can close, branch retained.
- [ ] `fix/shader-standard-fallback`: fully merged → retained as historical, PR closed.
- [ ] Resume ALL further WSM3D work on the one canonical branch. (Root cause of this whole mess: I lost track of our own mainline and built a session on a divergent branch. Single-trunk discipline from here.)

## Alternative considered (rejected)
**`fix/` as base, merge research in:** rejected because research carries the CI workflow + TDD harness as first-class history; rebasing 308 commits of infra on top of fix/ is higher-conflict than merging fix/'s 49 onto research. Same end-state, more pain.

## Non-negotiables (from charter/memory)
- **Never squash** — both histories preserved via `--no-ff`.
- **Never auto-delete a branch** — ask the user first; retain all four.
- **Never force-push** a shared branch.
- Tag before merging; work on a throwaway integration branch; build + vision-verify before landing.
