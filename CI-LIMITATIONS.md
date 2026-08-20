# CI Known Limitations

This document records known CI limitations for WorldSphereMod and Compound-Spheres-3D,
so contributors understand what the gates do and don't cover.

## WorldSphereMod

### Required checks (block merge)

| Check | Source | What it tests |
|---|---|---|
| `ci / lint` | `ci.yml` | `trunk check` (actionlint, yamllint, taplo, shellcheck, prettier) + C# `dotnet format --verify-no-changes` |
| `ci / test` | `ci.yml` | `dotnet test` on `tests/WorldSphereMod.Tests.*` (standalone net8.0 xunit projects, no Unity dependency) |

### Advisory checks (don't block merge)

| Check | Source | Known limitation |
|---|---|---|
| `test-gate` | `test-gate.yml` → `live-verify-gate.yml` | Runs `dotnet test` on ALL test projects including E2E. E2E tests depend on Unity assemblies (`UnityEngine`, `WorldBoxConsole`, `SleekRender`, `HarmonyLib`) not available on GitHub-hosted runners. These tests require a self-hosted runner with WorldBox installed. |
| `build` | `build.yml` | Tries to compile `WorldSphereMod.csproj` which requires `WORLDBOX_PATH` environment variable pointing to a WorldBox installation. Advisory only (`continue-on-error: true`). |
| `dotnet-build` | (part of build.yml) | Same Unity assembly dependency. Fails on GitHub-hosted runners. |
| `scorecard` | `scorecard.yml` | OpenSSF Scorecard SARIF upload requires Code Scanning enabled on the repo. Fork repos don't inherit this from the parent. |
| `SonarCloud` | SonarCloud GitHub App | Requires SonarCloud project configuration. Not configured for fork repos. |
| `Capture Vercel Screenshots` | `screenshot-vercel.yml` | Requires Vercel integration. Not configured for fork repos. |

### How to run the full test suite locally

```bash
# Requires WORLDBOX_PATH to be set
export WORLDBOX_PATH="/path/to/WorldBox"

# Build the mod
dotnet build WorldSphereMod.csproj -c Release

# Run unit tests (standalone, no Unity needed)
dotnet test tests/WorldSphereMod.Tests.Unit

# Run integration tests (needs Unity assemblies)
dotnet test tests/WorldSphereMod.Tests.Integration

# Run E2E tests (needs Unity assemblies + bridge)
dotnet test tests/WorldSphereMod.Tests.E2E

# Full offline live-verify
pwsh Tools/do-all.ps1
```

### Self-hosted runner setup

To run the full CI suite (including Unity-dependent tests), configure a self-hosted runner:

1. Install WorldBox and set `WORLDBOX_PATH`
2. Register a GitHub Actions self-hosted runner on the machine
3. Add the `self-hosted` label to the runner
4. Update `.github/workflows/live-verify-gate.yml` to use `runs-on: self-hosted` instead of `ubuntu-latest`

See `.claude/commands/wsm-build.md` for detailed build instructions.

## Compound-Spheres-3D

### Required checks (block merge)

| Check | Source | What it tests |
|---|---|---|
| `ci / lint` | `ci.yml` | `trunk check` (actionlint, yamllint, taplo, shellcheck, prettier) + C# `dotnet format --verify-no-changes` |
| `ci / test` | `ci.yml` | `dotnet test` on `Tests/CompoundMeshes.Tests/` (standalone net8.0 xunit, no Unity dependency) |

### Advisory checks (don't block merge)

| Check | Source | Known limitation |
|---|---|---|
| `scorecard` | `scorecard.yml` | OpenSSF Scorecard SARIF upload requires Code Scanning enabled. |
