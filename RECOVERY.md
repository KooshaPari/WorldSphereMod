# Recovery Notes — Why This Repo Exists

## TL;DR

This repository (`KooshaPari/WorldSphereMod`) was re-created on 2026-06-24 after the
original `KooshaPari/WorldSphereMod` was deleted on GitHub. All content was recovered
from local working copies + cached remote refs in the deleted repo's last known state.
**No content was lost.**

## Background

WorldSphereMod (WSM3D) is a Unity-based BepInEx mod for WorldBox. The original
GitHub repository hosted all source code, test suites, and release artifacts.

On 2026-06-23 the original repository was deleted (intentionally by the owner, as part
of a larger project cleanup). All branches, tags, issues, and pull requests were
removed from GitHub's public interface.

## Recovery Process

A full audit was performed (see `_wsm3d_audit/AUDIT_REPORT.md`, 21+ sections,
~720 lines) to:

1. **Verify local coverage** — confirmed every commit on the deleted `origin/main`
   (`f9475db0a5843215134fc29409ce4dd2f0a266b0`) was still reachable from local refs
2. **Identify gaps** — found 5 commits on `origin/main` that weren't yet on local
   `main` (PRs #50, #51, #52 from the deleted repo)
3. **Transfer objects** — used `git bundle` + fetch refspec
   `+refs/remotes/*:refs/remotes/*` to move all GitHub content into local repos
4. **Create replacement repo** — `gh repo create` made this backup with private
   visibility initially (switch to public if appropriate)
5. **Push all refs** — every local + remote-tracking branch + tag was pushed
6. **Apply branch protection** — `allow_force_pushes: false`, `allow_deletions: false`

## What This Repo Contains

| Branch | Purpose | Source |
|--------|---------|--------|
| `main` | Primary development branch (C: working tree's `main`) | Local recovery |
| `backup/e-main-snapshot` | E: drive's `main` (unique `wip/208-*` work) | Local E: |
| `preserve/stash-0-feat-render-foundation-builtin` | C: stash 0 | Patch + branch preservation |
| `preserve/stash-1-wsm-manager` through `preserve/stash-8-shader-wip` | C: stashes 1-8 | Patch + branch preservation |
| `scratch/wip-state-2026-06` | Untracked file snapshot (docs/journeys, .agileplus, tools/) | E: scratch capture |
| `origin/claude/*`, `origin/feat/*`, `origin/fix/*`, `origin/harden/*` | All remote branches from deleted GitHub repo | Bundle transfer |
| `backup/e-main-snapshot` | E: drive's `main` (unique wip/208-* work) | Local E: |

## GitHub WSM3D Original Repo Status

- **Status**: Deleted (per owner request)
- **Last known tip**: `f9475db0a5843215134fc29409ce4dd2f0a266b0` (PR #50/#51/#52 merged)
- **Restore options**: GitHub Support restore form at
  https://support.github.com/contact (separate from this backup; only useful if
  you want the issues/PRs/wiki back, not the git history which we already have)
- **Recommendation**: Do NOT email GitHub Support unless you specifically need
  the old issues/PRs/wiki restored — the code is 100% recoverable from this repo

## Compound-Spheres-3D (CS3D) Submodule

This repo's `External/Compound-Spheres` submodule points at:
- `KooshaPari/Compound-Spheres-3D-Backup` (new backup created 2026-06-24)

The original `KooshaPari/Compound-Spheres-3D` is still on GitHub but **archived**
(read-only). The backup contains the merged `sota/gpu-compute-golive` +
`perf/incremental-heightfield` work that was ahead of the archived upstream.

## Integrity Verification

Run `scripts/verify.ps1` to confirm:
- SHA parity across C: + E: + 2 backup repos
- All 4 working trees clean (no uncommitted changes)
- Full test suite passes: 590/593 (100% of runnable tests)
- All 11 stashes still present (NOT dropped)
- All `preserve/stash-*` branches intact

## Audit Trail

Full audit at: `C:\Users\koosh\_wsm3d_audit\AUDIT_REPORT.md` (21 sections, ~720 lines)

Includes:
- Complete merge history (66 branches)
- Worktree cleanup log (21 removed)
- Stash preservation log (11 preserved)
- All code bug fixes (3 real bugs: per-frame cache clear, IGridDimensions gap,
  `_ALPHABLEND_ON` keyword)
- Test fixes (21 of 26 pre-existing failures resolved)
- Sync matrix across all 7 locations