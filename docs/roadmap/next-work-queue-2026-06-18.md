# WSM Next Work Queue (June 2026)

## Scope
- WSM-only continuation queue for `docs/roadmap/**` and future runtime proof work.
- Tasks are intentionally similar-sized so future agents can pick up one item without owning the whole roadmap.
- Do not mark live tasks complete unless the referenced artifact contains real runtime evidence.

## Current Status Rules
- WSM-R0: `done`.
- WSM-R1: `in_progress`; static checks are partly complete, live gates are blocked.
- WSM-R2: `blocked_live_runtime`; prep artifacts exist, but phase-0 proof has not run.
- WSM-R3 and WSM-R4: `ready` after live blockers are cleared.

## Proven Statically
- Phase-default snapshot captured in `artifacts/2026-06-18/WSM-R1-phase-flags.txt`.
- `MeshWater` constructor default is `true`, but `ApplyPhaseDefaults` resets it to `false`.
- Many later phase flags remain static OFF and must stay OFF until phase smoke proof exists.
- WSM-R2 template, blocker map, and proof directory schema are prepared as static planning artifacts.
- R1 live-proof runbook now captures build/install commands, `/health` retry steps, Player.log freshness checks, screenshot/pixel checks, optional non-live parser/test checks, and R2 unlock conditions.
- Source-shape, Harmony, and coverage evidence are static/source-health gates unless a fresh runtime artifact explicitly links them to current live behavior.

## Blocked Live
- WSM startup smoke execution.
- Player.log exception baseline and exception trend.
- Launch screenshot proof and SHA256.
- WSM-R2 phase-0 proof packet.
- Any phase ladder pass/fail claim that depends on visual runtime behavior.
- Current live attempt receipt: `artifacts/2026-06-21-WSM-R1-live-attempt.md` records a running `worldbox` process, but `/health` on `127.0.0.1:8766` refused the connection and `Player.log` was stale from 2026-06-18. This is not WSM-R1 live proof.
- Historical L1 report blockers remain retry context only: `HEIGHTFIELD_BLOCKING`, `BRIDGE_FLAKY`, `FALLBACK_PATH`, `NO_INSTANCING`, and `UI_SCREENSHOT_FAIL` require fresh classification before R2 can advance.

## Next Execution Order
1. Reattach a live WSM runtime session.
2. Replace WSM-R1 startup placeholder with real launch artifact.
3. Replace WSM-R1 exception placeholder with Player.log baseline.
4. Capture launch screenshot proof/hash.
5. Close WSM-R1 only if DoD evidence is attached.
6. Execute WSM-R2 phase-0 proof packet.
7. Advance one phase at a time, keeping unproven defaults OFF.

## WSM Independent Task List

1. [ ] WSM-R1: confirm the active branch and commit hash before live runtime work, then append the exact values to the R1 evidence closure note.
2. [ ] WSM-R1: re-run the build command from a clean shell and attach the build log path to the execution matrix.
3. [ ] WSM-R1: re-run the install command and attach the install log path to the execution matrix.
4. [ ] WSM-R1: start a live WSM runtime session and replace `WSM-R1-smoke-startup.txt` with real launch command, process, timestamp, and artifact hash.
5. [ ] WSM-R1: capture the first Player.log baseline and replace `WSM-R1-exception-baseline.txt` with exception counts and relevant snippets.
6. [ ] WSM-R1: capture one deterministic launch screenshot and record its file name plus SHA256.
7. [ ] WSM-R1: update `WSM-R1-batch-1-notes.md` so blocked placeholders are either replaced by evidence or still explicitly blocked.
8. [ ] WSM-R1: update `WSM-R1-batch-2-notes.md` so task statuses match the new runtime artifacts.
9. [ ] WSM-R1: verify the static phase-default snapshot still matches `SavedSettings.cs`; if it changed, create a new dated snapshot instead of editing history silently.
10. [ ] WSM-R1: decide whether source-shape failures are intentional or blockers, then attach the final classification artifact.
11. [ ] WSM-R1: decide whether the Harmony contradiction is resolved or still blocked, then attach the final rationale artifact.
12. [ ] WSM-R1: close WSM-R1 only after startup smoke, Player.log baseline, screenshot proof, coverage decision, source-fail map, and Harmony rationale all have evidence links.
13. [ ] WSM-R2: create the phase-0 proof packet from `WSM-R2-evidence-template.md` after R1 live blockers are cleared.
14. [ ] WSM-R2: run phase-0 baseline smoke and populate live fields for branch, commit, build, install, phase flags, Player.log, screenshot, and acceptance result.
15. [ ] WSM-R2: define the exact phase-01 flag delta before enabling anything beyond the proven baseline.
16. [ ] WSM-R2: run phase-01 voxel entities smoke and attach live proof or a concrete blocker entry.
17. [ ] WSM-R2: define the exact phase-02 flag delta and expected visual proof before any building/LOD smoke.
18. [ ] WSM-R2: run phase-02 building/LOD smoke and attach live proof or a concrete blocker entry.
19. [ ] WSM-R2: define the exact phase-03 water/terrain flag delta, including the `MeshWater` conservative default, before any water smoke.
20. [ ] WSM-R2: run phase-03 water/terrain smoke and attach live proof or a concrete blocker entry.
21. [ ] WSM-R3: after the first phase blocker is observed live, update the blocker map with owner, source path, and required closure evidence.
22. [ ] WSM-R3: document shader fallback/load-order policy only after confirming whether the live phase ladder hits a shader/substrate blocker.
23. [ ] WSM-R4: refresh release-readiness notes only after WSM-R1 and at least phase-0 WSM-R2 proof have real artifacts.
24. [ ] WSM-R4: update the execution matrix evidence links after each completed task, preserving blocked statuses where runtime evidence is missing.
25. [ ] WSM-R1: write a current branch/commit/dirty-state note before the next live attempt.
26. [ ] WSM-R1: attach the exact build log path and exit code from a clean shell run.
27. [ ] WSM-R1: attach the exact install log path and resolved WorldBox mod folder.
28. [ ] WSM-R1: verify installed `Code` and `mod.json` paths before launching WorldBox.
29. [ ] WSM-R1: relaunch WorldBox manually and record process identity or startup failure.
30. [ ] WSM-R1: rerun `/health` after NeoModLoader compile/init settles and record JSON or refused/timeout details.
31. [ ] WSM-R1: copy active `Player.log` immediately after the launch attempt and record mtime plus hash.
32. [ ] WSM-R1: grep `Player.log` for WSM/NML/Roslyn/Bridge/exception markers and attach counts.
33. [ ] WSM-R1: regenerate `report.json` only from the current live run, or record why it could not be produced.
34. [ ] WSM-R1: capture the required launch screenshots in the same output directory as `report.json`.
35. [ ] WSM-R1: run pixel verification against current screenshots or attach explicit failure output.
36. [ ] WSM-R1: classify `HEIGHTFIELD_BLOCKING` as resolved, current blocker, or accepted carry-forward risk.
37. [ ] WSM-R1: classify `BRIDGE_FLAKY` as resolved, current blocker, or accepted carry-forward risk.
38. [ ] WSM-R1: classify `FALLBACK_PATH` as resolved, current blocker, or accepted carry-forward risk.
39. [ ] WSM-R1: classify `NO_INSTANCING` as resolved, current blocker, or accepted carry-forward risk.
40. [ ] WSM-R1: classify `UI_SCREENSHOT_FAIL` as resolved, current blocker, or accepted carry-forward risk.
41. [ ] WSM-R1: reconcile source-shape failures against the current live result before closing R1.
42. [ ] WSM-R1: reconcile Harmony contradiction status against the current live result before closing R1.
43. [ ] WSM-R1: reconcile coverage decision against the current live result before closing R1.
44. [ ] WSM-R1: write final R1 classification as PASS, FAIL with blocker, or still blocked with missing artifact.
45. [ ] WSM-R2: create phase-0 proof packet only after R1 has a PASS or reviewed carry-forward classification.
46. [ ] WSM-R2: run phase-0 baseline smoke with current `Player.log`, screenshots, and report paths.
47. [ ] WSM-R2: define phase-01 voxel flag delta and expected visual proof.
48. [ ] WSM-R2: run phase-01 voxel smoke and attach live proof or blocker.
49. [ ] WSM-R2: define phase-02 building/LOD flag delta and expected visual proof.
50. [ ] WSM-R2: run phase-02 building/LOD smoke and attach live proof or blocker.
51. [ ] WSM-R2: define phase-03 water/terrain flag delta and expected visual proof, including conservative `MeshWater` handling.
52. [ ] WSM-R2: run phase-03 water/terrain smoke and attach live proof or blocker.
53. [ ] WSM-R2: define phase-04 lighting/day-night flag delta and expected visual proof.
54. [ ] WSM-R2: define phase-05 worldspace UI flag delta and expected visual proof.
55. [ ] WSM-R2: define phase-06 PostFX/particles flag delta and expected visual proof.
56. [ ] WSM-R3: summarize SafeShaders policy before any shader list expansion.
57. [ ] WSM-R3: summarize CompoundSpheres/substrate ownership before renderer hardening work.
58. [ ] WSM-R3: define exception-storm threshold for phase proof packets.
59. [ ] WSM-R3: define bridge-flakiness threshold for phase proof packets.
60. [ ] WSM-R4: update matrix and release-readiness notes only after proof artifacts exist.
