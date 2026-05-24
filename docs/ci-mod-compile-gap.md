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

## Related docs

- `tests/README.md` — test tier split (API vs mod vs E2E)
- `docs/HANDOFF.md` — local build + install
- `Directory.Build.props` — `WORLDBOX_PATH` / `WorldBoxPath` resolution
