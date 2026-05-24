# Releasing

This fork releases via GitHub Releases keyed to git tags. There is no Steam
Workshop submission planned (upstream's Workshop page was flagged for guideline
violation; ship on GameBanana + GitHub only).

## Pre-release checklist

- [ ] `git status` clean on `claude/research-ultraplan-fork-DdgI5`
- [ ] `task release:check` (or `dotnet build && dotnet test && dotnet format --verify-no-changes`) passes
- [ ] `CHANGELOG.md` `[Unreleased]` section is populated and dated
- [ ] `VERSION` matches the tag you're about to cut
- [ ] `mod.json` `version` field matches `VERSION`
- [ ] In-game smoke test from `docs/smoke-test-phase1.md` passes — at minimum:
  - Terrain regression-clean
  - Voxel actors don't topple while walking
  - Each enabled-by-default flag toggles cleanly
- [ ] CodeRabbit + Socket Security + semgrep all green on the latest commit

## Cutting a release

```powershell
# 1. Bump version
$ver = "2.0.0-alpha.N"  # pick the next pre-release identifier
Set-Content -Path VERSION -Value $ver
# update mod.json `version` field by hand

# 2. Move CHANGELOG [Unreleased] section to a dated heading
# (one-line edit; commit message: "release: $ver")

git add VERSION mod.json CHANGELOG.md
git commit -m "release: $ver"
git tag -a "v$ver" -m "WorldSphereMod3D $ver"
git push origin claude/research-ultraplan-fork-DdgI5 --tags

# 3. Build the release artifacts
dotnet build WorldSphereMod.csproj -c Release
# Copy the mod folder layout that ./Tools/install.ps1 produces into a zip:
#   WorldSphereMod3D-$ver/
#     Code/*.cs
#     Assemblies/CompoundSpheres.dll (+ .pdb)
#     AssetBundles/{win,linux,osx}/worldsphere{,.manifest}
#     GameResources/, Locales/, mod.json
# Zip it as WorldSphereMod3D-$ver.zip

# 4. Create GitHub Release
gh release create "v$ver" `
    --title "WorldSphereMod3D $ver" `
    --notes-file "docs/release-notes/v$ver.md" `
    "WorldSphereMod3D-$ver.zip"
```

## After release

- Update `docs/HANDOFF.md` "What's shipped" if a new phase flipped its default.
- Bump `VERSION` to the next `-alpha.N+1` and commit `chore: bump to v$next`.
- Notify GameBanana page if one exists.

## Yanking a release

Delete the tag + release on GitHub. Add a `[Yanked]` line in `CHANGELOG.md`
explaining why. The git history is not rewritten — yanking only revokes the
release artifact, not the commits.
