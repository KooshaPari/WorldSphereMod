---
description: Diagnostic check of WorldSphereMod3D environment
---

# wsm-doctor

Verify dependencies and game setup via the unified CLI.

## Command

```pwsh
pwsh Tools/wsm3d.ps1 doctor
pwsh Tools/wsm3d.ps1 doctor -Json
```

## Checks

| Check | Required | Notes |
|-------|----------|-------|
| `worldbox_path` | yes | `WORLDBOX_PATH` or default Steam path; needs `worldbox_Data` |
| `dotnet_sdk` | yes | `dotnet --version` on PATH |
| `python` | yes | `python` / `python3` / `py` for playcua |
| `git_submodules` | yes | `External/Compound-Spheres` initialized at pinned commit |
| `phenotype_journey` | recommended | PATH or local `target/release/phenotype-journey` |
| `bridge_rpc` | recommended | `http://127.0.0.1:8766/health` (game or wsm3d-mcp HTTP) |
| `omniroute` | optional | `OMNROUTE_BASE_URL` default `http://127.0.0.1:20128/v1` |

Exit code `1` when any **required** check fails or warns. Optional services report `[SKIP]` / `[WARN]` without failing the run.

## Example (healthy desktop)

```
WSM3D Doctor
============
  [OK] worldbox_path — WorldBox install found
  [OK] dotnet_sdk — dotnet SDK 8.0.100
  [OK] python — Python available
  [OK] git_submodules — git submodules initialized
  [WARN] phenotype_journey (recommended) — phenotype-journey not found ...
  [WARN] bridge_rpc (recommended) — BridgeRPC not reachable on port 8766
  [SKIP] omniroute (optional) — OmniRoute not reachable (optional for vision)

[WARN] Required checks passed with 2 optional warning(s).
```

Run again after fixing paths, `wsm3d submodule init` (or `git submodule update --init --recursive`), or launching WorldBox with the mod.

When `git_submodules` fails, run:

```pwsh
pwsh Tools/wsm3d.ps1 submodule init
```
