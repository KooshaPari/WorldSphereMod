<!--
Mirrors docs/PR_CHECKLIST.md. Keep the two in sync — when you tick a box
here, you should be able to tick it there too.
-->

## Summary

<!-- 1–3 sentences. What does this PR change and why? Include the phase number if applicable. -->

## Phase

<!-- Phase N: <short description> — or "infra / docs / chore" if not phase-aligned. -->

## Branch and scope

- [ ] Branch is `claude/research-ultraplan-fork-DdgI5` (or a sub-branch off it). Not `main`.
- [ ] One phase per PR. PR title includes the phase number (e.g. `Phase 3: crossed-quad foliage`).
- [ ] No file outside the phase's own module is touched, unless this PR body explicitly describes and justifies the cross-module change.
- [ ] Inherited-upstream files touched (`Core.cs`, `QuantumSprites.cs`, `3DCamera.cs`, `Effects.cs`, `Tools.cs`, `DimensionConverter.cs`, `General.cs`, `TileMapToSphere.cs`, `CompoundSphereScripts.cs`)? List which and why below.

## Build and CI

- [ ] `dotnet build WorldSphereMod.csproj -c Release` produces 0 errors locally. (~47 pre-existing warnings are acceptable.)
- [ ] CI is green: `build`, `test-gate`, `lint-gate`, and `docs-build-gate` (if docs touched) all green.
- [ ] Socket Security checks and semgrep are green.
- [ ] CodeRabbit pass completed after marking the PR ready-for-review (it does not run on drafts).

## Feature flag and smoke test

- [ ] New `SavedSettings` field defaults to `false`.
- [ ] If this phase's flag was flipped to default-`true`, the in-game smoke test was captured into `docs/screenshots/phase-N-*.png` and linked from this PR body. 360° camera sweep at minimum; per-phase additions per `docs/smoke-test-phase1.md` style.

## Public API

- [ ] Public API additions update **both** `WorldSphereMod/Code/WorldSphereAPI.cs` (internal `WorldSphereModAPI`) **and** `WorldSphereAPI/WorldSphereAPI.cs` (external `WorldSphereAPI`).
- [ ] The boxing-as-`object` pattern is preserved across the delegate boundary (external API stays Unity-free, targets netstandard2.0).
- [ ] Existing v1 signatures (`IsWorld3D`, `MakeActorNonUpright`, `MakeBuildingNonUpright`, `MakeProjectileNonUpright`, `EditEffect`, `GetSetting<T>`) are byte-compatible.

## Comments and docs

- [ ] No new comments describe *what* code does. Any new comment captures a *why* (invariant, workaround, hidden constraint) in one line.
- [ ] `README.md` phase table updated to reflect the new state (`planned` → `designed` → `code-complete` → `ready-to-test` → `landed`).
- [ ] `docs/HANDOFF.md` updated: "What has landed", "Recommended next steps", and any new open design questions.

## Identity and tooling

- [ ] `mod.json` `GUID` (`worldsphere3d.fork`) is unchanged.
- [ ] `mod.json` `name` is unchanged unless this PR is explicitly a rename PR.
- [ ] `Tools/install.ps1` still works end-to-end. Sanity check: `./Tools/uninstall.ps1` then `./Tools/install.ps1` from a clean install folder, launch WorldBox, mod loads green.

## Cross-phase coupling

- [ ] If this PR touches code a later phase depends on (per `docs/PLAN.md`), it is noted explicitly above so the next phase author is not surprised.
- [ ] If new decompile findings were needed, they are saved under `docs/` and linked from this PR body.

## Notes

<!-- Anything reviewers should know that doesn't fit above: screenshots, perf measurements, risks, follow-ups. -->
