# Contributing to WorldSphereMod3D

Welcome! This guide is for downstream modders fixing bugs, adding features, or experimenting with the 3D conversion stack. We ship draft PRs and iterate with CI as the feedback loop, so don't worry about polish—worry about clarity.

## Quickstart (5 steps to your first PR)

1. **Clone** the repo and set `WORLDBOX_PATH`:
   ```powershell
   git clone https://github.com/KooshaPari/WorldSphereMod.git
   cd WorldSphereMod
   $env:WORLDBOX_PATH = "C:/Program Files (x86)/Steam/steamapps/common/worldbox"
   ```
   (Bash: `export WORLDBOX_PATH="$HOME/.steam/steam/steamapps/common/worldbox"`)

2. **Build** the mod:
   ```powershell
   pwsh Tools/wsm3d.ps1 build
   ```
   (Bash: `./Tools/wsm3d.ps1 build` or delegate to the CLI tool directly)

3. **Make your change** — edit source files under `WorldSphereMod/Code/` or `WorldSphereAPI/`.

4. **Install and test**:
   ```powershell
   pwsh Tools/wsm3d.ps1 install
   pwsh Tools/wsm3d.ps1 launch
   ```
   Reload in NML or restart the game. Watch `Player.log` (see paths in `CLAUDE.md`).

5. **Commit and push** (see [Conventions](#conventions) below) then open a PR.

## Conventions

### Branch naming
Use one of: `claude/<topic>`, `feat/<topic>`, or `fix/<topic>`.

### Commit style
Follow [Conventional Commits](https://www.conventionalcommits.org/):
- `feat: ...` — new feature
- `fix: ...` — bug fix
- `chore: ...` — build, tooling, deps
- `docs: ...` — docs-only
- `refactor: ...` — code cleanup
- `test: ...` — test additions
- `perf: ...` — performance

### One PR per phase
Don't bundle phases 1 and 2 into a single PR. One phase = one PR. This keeps the review surface small and CI feedback focused.

### Protect the GUID
`mod.json` GUID is `worldsphere3d.fork` — it's intentionally different from upstream so the mod is co-installable. Don't change it casually.

### SavedSettings flags ship default-OFF
Every phase is gated by a `SavedSettings` flag (e.g., `phase_1_voxel_actors`). New phases must default to `false` until validated in-game. See `WorldSphereMod/Code/SavedSettings.cs`.

### No comment spam
Don't comment on obvious code ("this variable stores the count"). Comments are for *why* — invariants, workarounds, hidden constraints. See `CLAUDE.md` "Pitfalls" section for examples.

### For help
Refer to `CLAUDE.md` "Where to make changes" table for file paths and patterns. The tooling section explains the CLI, MCP server, and slash commands.

## Dev loop

The CLI at `Tools/wsm3d.ps1` is your friend. Use it instead of raw `dotnet` commands.

### Iterative (hot-reload)
```powershell
pwsh Tools/wsm3d.ps1 watch
```
This rebuilds and reinstalls on source change. Great for rapid iteration.

### One-shot
```powershell
pwsh Tools/wsm3d.ps1 relaunch
```
Build, install, kill the game, relaunch. Waits for you to close the editor before building.

### Validation
Run journeys to verify your phase works end-to-end:
```powershell
/wsm-journey-run us-wsm-phase-1-voxel-actors
```
or
```powershell
pwsh Tools/wsm3d.ps1 journey run -Id us-wsm-phase-1-voxel-actors
```

See `docs/journeys/CONTRIBUTING.md` for authoring your own journey.

## Testing

The repo has **42 unit tests** and **15 e2e tests** (journeys). Run them with:
```powershell
dotnet test
# or
task test-all
```

### Add tests for public changes
If you add a method to `WorldSphereAPI.cs` or a public flag in `SavedSettings.cs`, add a corresponding test. See `tests/WorldSphereAPI.Tests/` for the pattern. Phase 6 tests show how to test non-obvious codepaths.

### Static-text tests
We have tests that verify error messages, log output, and UI strings without loading Unity. These cover code we can't run in CI (like `VoxelRender.cs` which needs `Material`). If you touch those files, check that tests still pass.

## PR review

Your PR will automatically receive comments from **CodeRabbit** (code review bot) and **Gemini** (validation). Address those before requesting human review. We do a 24-hour round, so quick turnaround is appreciated.

## Code of conduct

See `CODE_OF_CONDUCT.md`. TL;DR: be respectful, assume good intent, and escalate to kooshapari@gmail.com if there are problems.

## License

This project is MIT. See `LICENSE` for details.

---

**Questions?** Open an issue or check the Discussions tab. We're here to help.
