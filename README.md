# WorldSphereMod (WSM3D) â€” Backup Repository

**This is a backup of the deleted `KooshaPari/WorldSphereMod` repository.**

The original WSM3D repo (a Unity/BepInEx mod for WorldBox) was deleted from
GitHub prior to 2026-06-23. This backup was created from a complete local
clone on the same day.

## What's here

| Branch | Description |
|--------|-------------|
| `main` | Primary main from `C:\Users\koosh\Dev\WorldSphereMod`, including the IGridDimensions Compound-Spheres submodule fix |
| `backup/e-main-snapshot` | Secondary main from `E:\dev\WorldSphereMod` (E: drive), preserving the `wip/208-height-fix` and other WIP merges that diverged from C: main |
| `preserve/stash-0-feat-render-foundation-builtin` through `preserve/stash-8-shader-wip` | 9 git stashes preserved as branches (the original `git stash list` is also still intact in the local repo) |
| `scratch/wip-state-2026-06` | WIP scratch state (docs/journeys, docs/progress, .claude, wsm3d_L1_progress) preserved as a branch |

All 38 GitHub `origin/*` remote branches are also cached locally in the
recovered `.git` object store. To re-fetch them, use the bundle at
`C:\Users\koosh\_wsm3d_audit\wsm3d-full-backup.bundle` (173 MB).

## Submodule: Compound-Spheres-3D

`External/Compound-Spheres` is a git submodule pointing to
[`KooshaPari/Compound-Spheres-3D-Backup`](https://github.com/KooshaPari/Compound-Spheres-3D-Backup),
which contains the merged `sota/gpu-compute-golive` + `perf/incremental-heightfield`
work that was integrated as part of the WSM3D finish.

## Build status

- **Compound-Spheres** (submodule): builds clean, 19/19 tests pass
- **WorldSphereMod.Tests.Unit**: 157/161 pass (1 pre-existing test failure, 3 skipped)
- **WorldSphereMod.Tests.E2E**: 387/399 pass (12 pre-existing test failures â€” all caused by the intentional Phase 6 â†’ CS3D water-surface migration, not by the merge work)
- **WorldSphereMod main project**: requires Unity / WorldBox DLLs; cannot build standalone

The `main` branch in this repo includes a fix for a merge-induced interface
gap: `IGridDimensions` was missing 3 members (`HasDirtyHeights`,
`SnapshotDirtyHeights()`, `TotalTiles`) that `HeightFieldRenderer.cs` called
after the sota+perf merges. See Compound-Spheres commit `75c7f96`.

## Recovery context

- Original repo: `KooshaPari/WorldSphereMod` (deleted, not restorable via API)
- GitHub Support restore: only via https://support.github.com/contact form
- Local backup: `C:\Users\koosh\_wsm3d_audit\AUDIT_REPORT.md` (16 sections)
- Bundle: `C:\Users\koosh\_wsm3d_audit\wsm3d-full-backup.bundle` (173 MB, 77 refs)

## License

See individual source files. The original repo did not have a top-level
LICENSE file.
