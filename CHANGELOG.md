# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.0.0-beta.0] - 2026-05-23

First beta release of **WorldSphereMod3D** — a hard fork that completes the 3D conversion of WorldBox on top of upstream WorldSphereMod terrain. All ten rendering phases are code-complete with default-on settings; visible 3D rendering is fully in place.

### Rendering phases (0–10)

| Phase | Summary |
|-------|---------|
| **0** | Fork metadata, `WORLDBOX_PATH` build portability, CI, settings/API v2 |
| **1** | Voxelized actors and buildings (`VoxelEntities`, default ON) |
| **2** | Procedural building meshes (`ProceduralBuildings`, default ON) |
| **3** | Crossed-quad foliage (trees/bushes/rocks), surface overlays, 3D wall prisms (`CrossedQuadFoliage`, default ON) |
| **4** | Mesh water with WaterGerstner shader (`MeshWater`, default ON); AssetBundle bake deferred to Phase 5b |
| **5** | Sun driver, shadow cascade config, procedural sky (`HighShadows`, `HdrSkybox`, `ColorGradingLut`, default ON) |
| **6** | Skeletal animation pipeline (`SkeletalAnimation`, default ON) |
| **7** | Worldspace UI — nameplates, HP bars, damage popups, selection ring (`WorldspaceUI`, `WorldspaceLabel3D`, default ON) |
| **8** | Autonomous time-of-day driver, SunRig color gradient, procedural sky (`DayNightCycle`, default ON) |
| **9** | Particle bursts on five effect IDs; URP PostFX volume (`ParticleEffects`, `PostFX`, `SSAOEnabled`, default ON) |
| **10** | FrustumCuller, LodSelector, ImpostorBillboard, softened hardware gate; proxy tier still routes to Voxel |

### API & compatibility

- **WorldSphereAPI v2** — new calls (`IsModel3D`, `RegisterCustomMesh`, …) safely no-op when connected to upstream WorldSphereMod.
- **Co-installable fork** — separate mod GUID (`worldsphere3d.fork`) from upstream; install via `Tools/install.ps1` or `wsm3d.ps1 install`.
- **Hardware gate** preserved from upstream; Phase 10 impostor path for GPUs that fail the compute-shader gate.

### Testing harness

- **Three-tier `dotnet test` suite** — `WorldSphereMod.Tests.Unit` (Unity-free API surface), `WorldSphereMod.Tests.Integration` (manifests, install contracts, journey/bake invariants), `WorldSphereMod.Tests.E2E` (repo shape, CI workflow, harness preflight).
- **`Tools/wsm-live-verify.ps1`** — orchestrates offline CI-equivalent stages (full `dotnet test` + phenotype journey mock verify) and optional `-Live` stages (BridgeRPC on `:8766`, PlayCUA scenarios, SSIM vs `docs/journeys/phase-previews/`); writes `Tools/.reports/live-verify-latest.json`.
- **CI gates** — `build.yml`, `test-gate.yml`, `live-verify-gate.yml` (offline stages 1–2), `lint-gate.yml`, `journeys-gate.yml`, `docs-build-gate.yml`, `nightly.yml` (full matrix including E2E).
- **Dev CLI** — `Tools/wsm3d.ps1` with build, install, reload, phase toggle, screenshot, profile, and `test` subcommand covering all three test projects.

### PlayCUA (agentic live verification)

- **`Tools/wsm3d-playcua/`** — Python agentic gate against a running WorldBox instance with mod installed and BridgeRPC on `127.0.0.1:8766`.
- **Sample scenarios** — YAML manifests per phase (1–10) plus bridge health, save/load smoke, and vision checks under `Tools/wsm3d-playcua/sample-scenarios/`.
- **`wsm3d playcua run-all`** — runs every sample scenario; optional `-VisionBackend omniroute` for OmniRoute vision on screenshot steps; artifacts in `Tools/wsm3d-playcua/.reports/run-all-artifacts/`.
- **MCP integration** — `wsm3d-mcp` exposes `list_playcua_scenarios()` and `run_live_verify_offline()` for agent workflows.

### Dev tooling & docs

- Phase journeys (`docs/journeys/manifests/us-wsm-phase-*/`), live verification guide (`docs/live-verification.md`), full plan (`docs/PLAN.md`), docs site at <https://kooshapari.github.io/WorldSphereMod/>.
- MCP server, PowerShell tab-completion, `/wsm-*` slash commands, and `wsm3d-stats.ps1` dashboard generation.

[2.0.0-beta.0]: https://github.com/kooshapari/WorldSphereMod/compare/v1.0.0...v2.0.0-beta.0
