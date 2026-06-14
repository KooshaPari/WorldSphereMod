# Canonical development checkout

**Use `E:\Dev\WorldSphereMod` as the single source of truth for WorldSphereMod3D.**

| Role | Path |
|------|------|
| Canonical repo | `E:\Dev\WorldSphereMod` |
| Active dev branch | `wip/208-height-fix` |
| WorldBox Steam install | `C:\Program Files (x86)\Steam\steamapps\common\worldbox` |
| NML mod load path | `worldbox_Data\StreamingAssets\Mods\WorldSphereMod` |

## Install

Always install from the E: checkout:

```powershell
cd E:\Dev\WorldSphereMod
pwsh Tools/wsm3d.ps1 install
```

The install script copies sources into `StreamingAssets/Mods/WorldSphereMod` (not `Mods/WorldSphereMod3D`).

## Do not install from C:

`C:\Users\koosh\Dev\WorldSphereMod` is a **mirror/resync target only**. Its `Tools/install.ps1` redirects to E:. That clone was stale as of 2026-06-02 (5/29 shader branch) and must not be used for installs.

## Parallel worktrees (E:)

| Path | Branch | Purpose |
|------|--------|---------|
| `E:\wsm3d-wt-quality` | `wip/quality-tests` | Regression tests + ADR |
| `E:\wsm3d-wt-robustness` | `wip/robustness` | Robustness docs + hardening |

Lane branches are merged into `wip/208-height-fix` when ready; they are not installed directly.

**Merged 2026-06-02:**
- `wip/quality-tests` → regression tests (`SunRegistrationInvariantsTests`, `VoxelPipelineRegressionTests`) + ADR-0021
- `wip/robustness` → load-path hardening, doc hygiene, bloat strip (scratch PNGs removed from git; local copies remain gitignored)
