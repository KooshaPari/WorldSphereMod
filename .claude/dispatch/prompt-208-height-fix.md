You are a Sonnet-level L1 manager agent owning the P0 height-fix task end-to-end on the WorldSphereMod repo. You may also use `codex exec -m gpt-5.3-codex-spark --dangerously-bypass-approvals-and-sandbox` (with prompts via stdin) as a worker channel for compile/build work. Always run long-running builds/installs in background bash.

# Repo + branch
- Working directory: `E:/Dev/WorldSphereMod` (Windows; use PowerShell-friendly commands)
- Branch: `wip/208-height-fix` (already checked out in the main worktree, do NOT switch branches or create a new worktree)
- Current HEAD: `e41ad30d8b7789b8904b0f6a6214dceb200b0c1c`
- Pre-existing uncommitted/untracked files (LEAVE ALONE, do not commit, do not stash):
  - M  `WorldSphereMod/Code/WorldSphereTab.cs`
  - ?? `.claude/dispatch/`, `.claude/worktrees/`, `Tools/coverage.ps1`, `docs/coverage/`, `docs/requirements-traceability.md`, `docs/xdd-status.md`
- Only modify files that are part of the height pipeline for this task.

# HARD RULES (non-negotiable)
1. **DO NOT touch the `External/Compound-Spheres/` submodule on this branch.** The GPU go-live boundary is the Compound-Spheres SUBMODULE. If the height fix requires editing `External/Compound-Spheres/CompoundSpheres/HeightFieldRenderer.cs` (or any other file under `External/Compound-Spheres/`), STOP and report a blocker. Only the consumer side in `WorldSphereMod/Code/` is in scope.
2. **NEVER `git stash`.** If a change conflicts, just keep working dirty or commit to a WIP commit on this branch.
3. **Do not narrate visuals.** Parent will not trust visual claims. Verify ONLY by reading the code and reporting code+commit evidence.
4. **Use background bash / codex for any build >30s.** Do not run `cargo build` / Unity build / install directly inline.
5. **Track size + perf every build.** Note build duration, final binary size, warnings.
6. **STOP if you make >3 iterations with no confirmed win.** Report blocker.
7. **Do NOT push.** Do NOT open a PR. Just commit locally and report.

# Task
The HeightField mesh exists (per #201), but the terrain appears flat / low-relief and shows polygonal faceting (visible triangles). Two symptoms:
- A) Displacement is too shallow — mountains should rise, valleys should dip
- B) Normals are per-triangle (flat-shaded) instead of smooth (gradient from neighboring heights)

# Steps
1. Use Grep (or PowerShell `Select-String` if Grep is unavailable) to locate the height pipeline. Targets:
   - `WorldSphereMod/Code/Core.cs` — look for `ConfigureHeightField`, `sampleHeight`, `_projectPosition`, `ProjectPosition`
   - Any other file under `WorldSphereMod/Code/` that references these
   - The call site that passes height offset into the mesh build
2. Read the consumer-side `sampleHeight` and `_projectPosition`. Note:
   - What does `sampleHeight` return? (likely `World.world.tiles.map[tile].height * some_scale`)
   - What multiplier is applied? (`height * 0.5` vs `height * 2.0`)
   - How is the height offset combined with sphere projection in `_projectPosition`?
3. Read the consumer code path that calls into the Compound-Spheres `HeightFieldRenderer`. Identify:
   - Where is the height value handed off?
   - Does the consumer compute normals, or does the submodule's renderer compute them? (If submodule computes, you can only bump the scale on the consumer side; the smoothing fix may be a blocker.)
4. **Likely fix (try first):** Bump the height scale multiplier in the consumer-side `_projectPosition` / `sampleHeight` path. Try 2x to 3x the current value. If current scale is `* 0.5` and terrain looks flat, try `* 1.5` or `* 2.0`. If it's already `* 1.0` and still flat, look for a clamp/saturate/loss-of-precision step (e.g., integer truncation, divide-by-tile-size).
5. **Normals fix:** Look for where the consumer computes (or could compute) vertex normals. If normals are computed per-triangle in consumer code, switch to a gradient-from-neighbors approach: for each vertex at `(x, y)`, sample height at `(x-1,y)`, `(x+1,y)`, `(x,y-1)`, `(x,y+1)` and compute the normal as the cross product of the tangent vectors. If normal computation is in the submodule, that is a blocker — report and stop.
6. Make the fix in `WorldSphereMod/Code/Core.cs` (or the relevant consumer file under `WorldSphereMod/Code/`).
7. Build: delegate to a background bash subagent (or `codex exec -m gpt-5.3-codex-spark --dangerously-bypass-approvals-and-sandbox` with the prompt via stdin). For WorldSphereMod, the build is via Unity Editor batch mode — use existing build automation (look in `Tools/`, any Justfile, or a `build.ps1`); do NOT invent new build scripts. Check the git log on this branch for prior build/install commands used by previous fixes.
8. Install: copy the built DLL to the canonical install path (likely `CompiledMods/WORLDSPHERE3D_FORK.dll` under the WorldBox StreamingAssets mods dir). Check `Tools/install.ps1` or recent commit messages for the install path.
9. Commit the fix with a clear message referencing #208 and the symptom, ending the body with `Co-Authored-By: Claude Sonnet 4 <noreply@anthropic.com>`.
10. **Do NOT push.** Do NOT open a PR. Just commit locally and report.

# Report back (terse, exact, no filler)
- root cause: file:line + the specific code that was wrong
- the fix: file:line + the new code (snippet)
- commit hash: short SHA
- build outcome: built/not-built, duration, warnings, binary size
- install outcome: copied to where, what got replaced
- blocker: yes/no. If yes, why
- what user needs to confirm visually: be specific (e.g., "launch via Tools/install.ps1, take a screenshot of the southwestern biome, expect visible 50-100m peaks instead of the current rolling-hills profile")
