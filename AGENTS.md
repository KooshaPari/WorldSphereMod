# AGENTS.md — agent collaboration guide

This repo runs under Phenotype-org agent conventions, adapted for a Unity-mod
codebase. The reference set lives in `C:/Users/koosh/Dino/` (DINOForge).

## Stack quick reference

- **Game**: WorldBox (Unity, NeoModLoader-compatible)
- **Language**: C# (.NET 5.0 main mod, netstandard2.0 API, net8.0 tests)
- **Build**: `dotnet build WorldSphereMod.csproj -c Release` (~5s, 0 errors)
- **Install**: `./Tools/install.ps1` (copies to `<WorldBox>/Mods/WorldSphereMod3D/`)
- **Tests**: `task test` (or `dotnet test tests/...`)

## Branching + commits

- **Dev branch**: `claude/research-ultraplan-fork-DdgI5`. Push there, never to `main`.
- **One PR per phase**. Inside a PR, commits are atomic and labeled `phase N step M: ...`.
- **Conventional Commits** for message format: `fix:`, `feat:`, `docs:`, `chore:`, `perf:`, `test:`, `refactor:`. Co-Authored-By footer for any Claude-driven commit.

## Agent operational rules

These are the rules this session ran under, lifted from DINOForge with Unity-mod adaptations.

### Manager-style orchestration

The user runs the top-level Claude as a **manager**, not a coder. Default behavior:

- Delegate implementation, builds, log analysis, decompile passes to subagents (`general-purpose` for code, `feature-dev:code-architect` for design, `feature-dev:code-reviewer` for review).
- Reserve the orchestrator for: dispatching, integrating returns, committing, and pushing.
- Multiple agents in a single message run concurrently. Maximize parallelism on non-overlapping files.

### Never idle

When agents are in flight, find non-conflicting work — don't end a turn just because work is running. "Idle" means producing no progress. Acceptable forms of non-idle work while waiting:

- Verify the build is currently green
- Update tasks (`TaskUpdate`)
- Refresh docs that don't conflict with in-flight scopes
- Investigate the next phase

### Tooling evolution rule

When a multi-step workflow repeats, collapse it to a single Taskfile / Justfile target. Update the runner instead of accumulating ad-hoc commands.

### Feature flag rule

Every new phase ships behind a `SavedSettings` flag defaulting OFF until the phase has passed in-game smoke test. Each phase flips its own flag to `true` in its ship-gate commit. Phase 0 sets the default; subsequent phases inherit OFF until explicitly opted in.

### One PR per phase

Don't bundle multiple phases. Commits inside one PR can be small and incremental, but the PR scope is exactly one phase number.

### Decompile workflow

For any unknown WorldBox API, run `ilspycmd` against
`worldbox_Data/StreamingAssets/mods/NML/Assembly-CSharp-Publicized.dll`. Save the
relevant decompiled `.cs` files to `C:/Users/koosh/AppData/Local/Temp/wsm_decomp*/`
and reference them from `docs/*-findings.md`. Do not commit the decompiled output.

## Common pitfalls

- The `Mods/` install layout is `<WorldBox>/Mods/<GUID-folder>/` containing the source `Code/*.cs` for NML's Roslyn compile, plus `Assemblies/CompoundSpheres.dll` and `AssetBundles/{platform}/`. NML compiles source at game launch — the built `WorldSphereMod3D.dll` in `bin/` is for sanity check only.
- WorldBox's `Assembly-CSharp.dll` is opaque; the *publicized* variant from NML is what `WorldSphereMod.csproj` references. If a member access fails at compile time, decompile and verify; don't guess.
- The `Mod.OnLoad` hardware gate throws on missing instancing; it logs-and-continues on missing compute/indirect-args (sets `LodSelector.ImpostorOnlyMode = true`). Don't tighten the gate.
- Cylindrical world (`CurrentShape == 0` default) wraps X. Use `Tools.WrappedDist` for distances. Frustum cull near the seam tests both `pos` and `pos + (Width, 0, 0)`.
- Reflective URP code (`ShadowCascadeConfig`, `PostFxController`) is intentional: WorldBox's `Managed/` ships no URP runtime DLLs. Don't refactor to direct references.

## What's where

| Need | Folder |
|---|---|
| Voxel pipeline (Phase 1) | `WorldSphereMod/Code/Voxel/` |
| Procedural buildings (Phase 2) | `WorldSphereMod/Code/ProcGen/` |
| Foliage + walls (Phase 3) | `WorldSphereMod/Code/Foliage/` |
| Mesh water (Phase 4) | `WorldSphereMod/Code/Water/` |
| Sun + sky + day/night (Phase 5 + 8) | `WorldSphereMod/Code/Lighting/` |
| Skeletal rigs (Phase 6) | `WorldSphereMod/Code/Rig/` |
| Nameplate / HP / selection / popups (Phase 7) | `WorldSphereMod/Code/Worldspace/` |
| Particles + decals + post-FX (Phase 9) | `WorldSphereMod/Code/Fx/` |
| LOD + impostor (Phase 10) | `WorldSphereMod/Code/LOD/` |
| Profiler (Phase 10) | `WorldSphereMod/Code/Perf/` |
| Shared shader source | `WorldSphereMod/Resources/Shaders/` |
| World-unload sink | `WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs` |
| Settings | `WorldSphereMod/Code/SavedSettings.cs` |
| Internal API | `WorldSphereMod/Code/WorldSphereAPI.cs` |
| External API (Unity-free) | `WorldSphereAPI/WorldSphereAPI.cs` |
| Test scaffold | `tests/WorldSphereMod.Tests.{Unit,Integration,E2E}/` |
| Docs site | `docs/` (VitePress) |
| ADRs | `docs/adr/` |
| User journeys | `docs/journeys/` |

## When in doubt

1. Check `docs/HANDOFF.md` for the canonical state.
2. Check `docs/phenotype-baseline.md` for which org conventions are met.
3. Check `docs/phase[N]-architecture.md` for what each phase is supposed to do.
4. If still uncertain, ask a clarifying question rather than guessing.
