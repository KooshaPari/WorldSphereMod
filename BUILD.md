# Building WorldSphereMod3D

The mod is a `net48` C# library that depends on WorldBox's publicized
assemblies. The build is **hermetic from the repo root**: the `dotnet build`
command resolves all required WorldBox references through MSBuild properties.

## TL;DR

```pwsh
# 1. Set the WorldBox install location (one-time, or per shell)
$env:WORLDBOX_PATH = "C:/Program Files (x86)/Steam/steamapps/common/worldbox"

# 2. Build
dotnet build WorldSphereMod.csproj -c Release
```

The resulting DLL is at `WorldSphereMod/bin/Release/net48/WorldSphereMod3D.dll`
and is installed by `Tools/install.ps1`.

## Prerequisites

- Windows 10/11 (the mod is a WorldBox plugin; the runtime is Win32).
- .NET SDK 6.0 or newer (provides the `dotnet` CLI and the `net48` targeting pack).
- A local install of WorldBox (the `WORLDBOX_PATH` env var must point at it).
- For CI, the `WorldBoxNML` / `WorldBoxNMLAssemblies` / `WorldBoxManaged` /
  `WorldBoxPublicized` directories are populated by `.github/workflows/build.yml`
  from the `WorldBox-Publicized-DLLs` reference set.

## Build variants

| Command | Purpose |
|---|---|
| `dotnet build WorldSphereMod.csproj -c Release` | Production DLL |
| `dotnet build WorldSphereMod.csproj -c Debug` | Debug symbols, no optimizations |
| `dotnet build WorldSphereAPI.csproj -c Release` | Public API surface DLL (separately published) |
| `pwsh Tools/wsm3d.ps1 build -Configuration Release` | High-level wrapper used by `.claude/commands/wsm-build.md` |
| `task build` (via [Taskfile.yaml](Taskfile.yaml)) | Cross-platform task runner equivalent |
| `just build` (via [Justfile](Justfile)) | Cross-platform task runner equivalent |

## Test projects (separate solutions)

The four test suites live under `tests/`:

| Suite | Project | Purpose |
|---|---|---|
| Unit | `tests/WorldSphereMod.Tests.Unit/` | Fast invariants, < 30 s |
| Integration | `tests/WorldSphereMod.Tests.Integration/` | Cross-component + journey traces |
| E2E | `tests/WorldSphereMod.Tests.E2E/` | Full mod + WorldBox harness |
| Bench | `tests/WorldSphereMod.Tests.Bench/` | Frame/heap/GPU micro-benchmarks |

Each is invoked via `dotnet test <csproj> -c Release`. The CI equivalents are
`.github/workflows/{lint-gate,test-gate,nightly}.yml`.

## Unity Bake project

`Tools/Unity-Bake-Project/` is a separate Unity project used **only** to
author AssetBundles (`WorldSphereMod/AssetBundles/`) and shaders. It is **not**
required to build the mod DLL. If you change a shader or asset, run the
Unity editor against this project to rebake the bundles; the produced
artifacts are committed and consumed by `WorldSphereMod.csproj` at build time
via `<None Include="WorldSphereMod/AssetBundles/**/*" />` (see
`Directory.Build.props`).

## Continuous integration

The protected `main` branch requires:

- `ci / lint` — aggregated lint gate (rust/cargo-deny/python/go/ts/security/dep-review)
- `ci / test` — aggregated test gate
- 1 approving PR review, linear history, conversation resolution, strict up-to-date

These checks are wired in `.github/workflows/ci.yml` and enforce that any
`WorldSphereMod.csproj` build landing on `main` is the result of a green
local verification set.
