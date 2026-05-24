# CI mod compile gap

WorldSphereMod is a Harmony + NeoModLoader mod that compiles against WorldBox’s
Unity `Managed/` assemblies, NML’s publicized `Assembly-CSharp`, and NeoModLoader
itself. Those binaries are **not redistributable** and are **not present** on
GitHub-hosted runners.

## What CI does today

| Workflow | Blocking gate | Mod (`WorldSphereMod.csproj`) |
|---|---|---|
| `build.yml` | `WorldSphereAPI` (`netstandard2.0`) | Best-effort `dotnet build`, `continue-on-error: true` |
| `test-gate.yml` | Unit tests (API-only) | Stubs only if a test project references the mod |
| `lint-gate.yml` | `dotnet format` on API | Mod format check is best-effort |
| `release.yml` | API + changelog artifact | Mod build best-effort; release ships API/docs, not a game DLL |

Before each job that needs HintPaths, CI runs `Tools/ci-stub-worldbox-refs.sh`,
which creates **zero-byte placeholder files** at the paths listed in
`Tools/ci-worldbox-ref-dlls.manifest` and sets `WORLDBOX_PATH` to that tree
(via `Directory.Build.props`).

## Why placeholders are not enough to compile the mod

MSBuild can resolve a `HintPath` to an empty file, but the C# compiler must
**read metadata** from each reference assembly. Zero-byte files are invalid PE
images, so type resolution fails (thousands of `CS0246` errors for `UnityEngine`,
`HarmonyLib`, `ActorManager`, etc.).

A loadable mod binary also needs real IL for every patched type and every
`UnityEngine.*` member the code touches. Synthesizing that surface without the
game install means either:

1. **Checked-in or generated stub assemblies** with the full public API the mod
   uses (large, high maintenance), or
2. **AsmResolver / similar** tooling to emit minimal PE files plus hand-maintained
   type stubs (medium effort, still incomplete vs. game updates), or
3. **Self-hosted CI** with a licensed WorldBox install (operational cost, best
   fidelity).

Until one of those exists, **the real compile gate is local**: set
`WORLDBOX_PATH` to your Steam install and run `dotnet build WorldSphereMod.csproj`
or `task build`.

## What *is* gated in CI

- **`WorldSphereAPI`** builds on every push/PR (no Unity, no WorldBox refs).
- **E2E repo tests** (`CiWorkflowInvariantsTests`, install/mod.json checks, etc.)
  keep workflow YAML, stub manifest, and `WorldSphereMod.csproj` HintPaths aligned.
- **Unit tests** under `tests/WorldSphereMod.Tests.Unit` exercise the public API
  without a host process.

## Improving the gate later

Priority order from `docs/phenotype-baseline.md` item 21:

1. Self-hosted runner + `WORLDBOX_PATH` → flip mod build to a hard failure.
2. Generated compilable stubs → mod build becomes a hard failure on hosted runners.
3. Keep best-effort mod build but rename the job step so it is not mistaken for a gate.

## `External/Compound-Spheres` submodule (upstream pin)

The terrain backend lives at
[`MelvinShwuaner/Compound-Spheres`](https://github.com/MelvinShwuaner/Compound-Spheres).
We keep the git submodule pointer on **upstream `main`** (currently
`73a7b77`), not on fork-only commits.

| Topic | Detail |
|---|---|
| **Why not push our fixes upstream?** | We do not have write access to `MelvinShwuaner/Compound-Spheres`. `git push` to `origin` is rejected unless Melvin grants collaborator access or merges a PR. |
| **What ships in the mod today** | `WorldSphereMod/Assemblies/CompoundSpheres.dll` (vendored binary). `WorldSphereMod.csproj` references that DLL, not a `<ProjectReference>` to the submodule (see comment at the CompoundSpheres `HintPath`). |
| **Why the submodule exists** | Source reference, diff review, and future `Compound-Spheres-3D` fork (`docs/phase5-prep.md`). It is not the active MSBuild compile input. |
| **Building submodule source in-tree** | Compiling `External/Compound-Spheres/CompoundSpheres.csproj` alongside the mod currently hits duplicate `AssemblyInfo` / project-output conflicts; that path is deferred. |

### Optional local patch: `dd78b11` (`SetTexture` guard)

Commit `dd78b11` on branch `main` in a **local-only** submodule checkout adds a null
guard before `Material.SetTexture("TextureArray", …)` in
`CompoundSpheres/SphereManager.cs` (init path when material or texture array is
missing). It is **not** on upstream `main` and **cannot** be pushed to
`MelvinShwuaner/Compound-Spheres` without upstream accepting it.

To apply locally (does not change the pinned submodule SHA in the parent repo):

```powershell
cd External/Compound-Spheres
git fetch origin
git cherry-pick dd78b11
# rebuild CompoundSpheres.dll in Unity 2022.3 and copy into WorldSphereMod/Assemblies/
```

To match the repo pin (upstream `main` only):

```powershell
cd External/Compound-Spheres
git checkout 73a7b77
cd ../..
git add External/Compound-Spheres
```

**Runtime alternative (no DLL rebuild):** Harmony patch on
`CompoundSpheres.SphereManager` init — same guard intent as `dd78b11`; see
`WorldSphereMod.csproj` comment (2026-05-23).

Parent-repo submodule SHA should stay at upstream `main` until we own a fork
(e.g. `KooshaPari/Compound-Spheres-3D`) or upstream merges the fix.

## Related docs

- `tests/README.md` — test tier split (API vs mod vs E2E)
- `docs/HANDOFF.md` — local build + install
- `docs/phase5-prep.md` — planned `Compound-Spheres-3D` fork + DLL swap
- `Directory.Build.props` — `WORLDBOX_PATH` / `WorldBoxPath` resolution
