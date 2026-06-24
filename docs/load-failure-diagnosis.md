# WSM3D Load Failure Diagnosis

Date: 2026-05-29

## Summary

I reproduced the current WorldBox startup from a fresh process, read the fresh `Player.log`, compared the deployed `Mods/WorldSphereMod3D/Code` tree against repo HEAD, and then reinstalled the mod from the repo.

Result:

- The fresh log does **not** show a WSM3D compile failure, runtime init exception, or feature-specific exception in the current run.
- The deployed mod source tree **was stale / mismatched** before reinstall.
- Running `pwsh Tools/wsm3d.ps1 install` refreshed the install and the next launch reached both `Init Mod WorldSphereMod3D` and `Post-Init Mod WorldSphereMod3D` with no WSM3D error.

## Failure class

This was **not** a current compile/runtime exception in the fresh log. It was an **install drift** problem: the installed `Mods/WorldSphereMod3D/Code` snapshot did not match repo HEAD, so the deployed mod was not guaranteed to reflect the current source tree.

That maps to the user’s decision tree as:

- Not A: no `Failed to compile mod WorldSphereMod3D` + `error CS...` chain in the fresh log.
- Not B: no WSM3D init exception/stack trace during `[WSM3D]...` startup in the fresh log.
- Not C: no feature-level exception after mod load in the fresh log.
- Root cause: stale install / mismatched deployed source snapshot.

## Exact log evidence

Fresh launch sequence in `Player.log`:

- `Compile Mod WorldSphereMod3D`
- `Init Mod WorldSphereMod3D`
- `Post-Init Mod WorldSphereMod3D`

Relevant lines from the fresh log:

- `Compile Mod WorldSphereMod3D        = 9.6639`
- `Init Mod WorldSphereMod3D           = 3.4158`
- `Post-Init Mod WorldSphereMod3D      = 0.1031`

There is **no** `Failed to compile mod WorldSphereMod3D` line and **no** `error CS...` block in the fresh log capture used for diagnosis.

## Install drift evidence

Before reinstall, the installed `Mods/WorldSphereMod3D/Code` tree did not match repo HEAD. The mismatch was not a single file typo; it included source differences across several files, including:

- `WorldSphereMod/Code/LOD/LodSelector.cs`
- `WorldSphereMod/Code/SavedSettings.cs`
- `WorldSphereMod/Code/Voxel/VoxelRender.cs`
- `WorldSphereMod/Code/Worldspace/HealthBar.cs`
- `WorldSphereMod/Code/Worldspace/NameplateWorld.cs`
- `WorldSphereMod/Code/Worldspace/DamagePopup.cs`
- `WorldSphereMod/Code/Worldspace/FactionBadge.cs` and `WorldSphereMod/Code/Worldspace/FactionBadgeAtlasBuilder.cs` were also present in repo state while the install snapshot differed

The install was refreshed with:

```powershell
pwsh Tools/wsm3d.ps1 install
```

That install completed successfully and reported:

- `verified 108 .cs files`
- `installed to C:\Program Files (x86)\Steam\steamapps\common\Worldbox\Mods\WorldSphereMod3D`

## Post-fix confirmation

After reinstall and relaunch, the fresh `Player.log` shows:

- `Compile Mod WorldSphereMod3D`
- `Init Mod WorldSphereMod3D`
- `Post-Init Mod WorldSphereMod3D`

with no WSM3D compile/init exception.

## Fix applied

Applied fix:

1. Stopped WorldBox.
2. Reinstalled WSM3D from the repo with `pwsh Tools/wsm3d.ps1 install`.
3. Relaunched WorldBox.
4. Confirmed the mod reaches `Init` and `Post-Init` on the fresh log.

No source code change was required to remove a compile/runtime exception, because the current failure was an out-of-date deployed install snapshot rather than a live source defect in the fresh boot.
