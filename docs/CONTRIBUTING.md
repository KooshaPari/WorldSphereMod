# Contributing to WorldSphereMod3D

This is the governance companion to `CLAUDE.md`. Read that first for the
project overview and conventions. This doc codifies the rules a PR author
is expected to follow.

## 1. Branch convention

`claude/research-ultraplan-fork-DdgI5` is the active development branch.
Push there, **never to `main`**. Sub-branches off the dev branch are fine
for stacked work; merge them back into the dev branch via PR, not into
`main`.

## 2. One PR per phase

Phases 0 through 10 each ship as their own PR. Do not bundle two phases
into a single PR even if they touch adjacent modules. Within a single
phase, commits can be incremental (step 1, step 2, …) — that's the
preferred shape for review.

## 3. Feature flag rule

Every new render mode is gated behind a field in
`WorldSphereMod/Code/SavedSettings.cs`. The flag ships **default-OFF**.
A phase only flips its own flag to `true` after in-game smoke testing
captures expected behavior at 360° camera sweep (see
`docs/smoke-test-phase1.md` for the template). Never flip another
phase's flag from inside your PR.

## 4. Co-installability

The `mod.json` `GUID` is `worldsphere3d.fork`. **Do not change it.** It
is the identity that lets this fork sit beside upstream `WorldSphereMod`
in NeoModLoader. Before opening a PR, install both mods side-by-side
and confirm upstream `THE_3D_WORLDBOX_MOD` still loads with this fork
present (only one enabled at a time to avoid double-patching, but both
must remain installable).

## 5. Build and install loop

The canonical local loop is:

```powershell
./Tools/install.ps1
```

NeoModLoader compiles `Code/*.cs` at runtime, so `install.ps1` copies
**source files** plus `Assemblies/`, `AssetBundles/`, `GameResources/`,
`Locales/`, and `mod.json` into `<WorldBox>/Mods/WorldSphereMod3D/`. The
script runs `dotnet build` first as a sanity gate; pass `-SkipBuild` only
when iterating on non-code resources. Use `Tools/uninstall.ps1` to clean
up between test runs.

## 6. Comment policy

Do not add comments that describe what code does — the code already says
that. The only acceptable new comments are one-liners that capture a
non-obvious *why*: a hidden invariant, a workaround for a WorldBox
behavior, a constraint that isn't visible from the call site (e.g. the
`Constants.ZDisplacement = 100` sentinel, thread-safety of a parallel
postfix, the cylindrical X-wrap). If the *why* takes more than a line,
put it in `docs/` and link to it.

## 7. Cross-phase coupling

When a change in Phase N touches code that a later Phase M depends on,
call it out in the PR description so the M author isn't surprised. The
inherited-upstream files — `Core.cs`, `QuantumSprites.cs`, `3DCamera.cs`,
`Effects.cs`, `Tools.cs`, `DimensionConverter.cs`, `General.cs`,
`TileMapToSphere.cs`, `CompoundSphereScripts.cs` — are tread-carefully:
~80 Harmony patches live across them and unrelated breakage cascades
fast. If you must edit one, keep the change minimal and isolated, and
explain the *why* in the PR body.

## 8. Decompile path

For undocumented WorldBox API surface, decompile
`Assembly-CSharp-Publicized.dll` with `ilspycmd`. Save the findings to a
new file under `docs/` (e.g. `docs/phase3-decompile-findings.md`) so the
next contributor doesn't re-do the investigation. Reference the findings
doc from the PR body.
