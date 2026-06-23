# WSM3D Non-Functional Requirements (NFR)

> **Status:** draft bootstrapped from `PLAN.md` (`/PLAN.md` at repo root;
> `docs/PLAN.md` is a redirect). Headline targets below; full prose
> lives in `PLAN.md` §"Phased Plan" and §"Verification plan".
> Cross-referenced by `docs/requirements-traceability.md`.

## NFR-WSM-001 — Frame budget (60 fps mid-range hardware)
Vanilla map, age 100, 5 000 actors must sustain 60 fps on RTX 3060 / 5600X.
`SavedSettings.BuildingRenderBudget` caps per-frame building work
(default 50, was 200). Telemetry: `FrameProfiler.cs` (CPU ms per system),
in-game FPS HUD toggled via F8.

## NFR-WSM-002 — Voxel mesh cache hit rate
`VoxelMeshCache` LRU dedup reduces per-sprite voxelization cost. On-disk
SQLite cache (`SavedSettings.VoxelDiskCache`, default on,
`VoxelDiskCacheMaxSizeMB = 50`) skips async queue on warm launch.

## NFR-WSM-003 — Mod init time
`Mod.OnLoad` should complete in < 500 ms on a typical WorldBox world.
Tracked via `InitProfiler.cs` and emitted to the bridge; no
hard CI gate today.

## NFR-WSM-004 — Memory footprint
Total managed+GPU memory under `/memory` bridge endpoint; budget
enforced by `MaxTilesFor3D = 100 000` (316×316 map cap above which
`Become3D` is skipped to prevent GPU hangs).

## NFR-WSM-005 — Hardware fallback
On hardware failing the compute-shader gate (`Mod.cs:21`), the impostor
LOD path must still render at 60 fps on Intel UHD 620 (sprite-billboard
mode). Surface: `WorldSphereMod/Code/Perf/ImpostorFallback.cs`.

## NFR-WSM-006 — Backwards-compatible public API
v1 callers (`IsWorld3D`, `MakeActorPerp`, `MakeBuildingPerp`,
`MakeProjectilePerp`, `EditEffect`, `GetSetting<T>`) keep identical
signatures across major versions. v2 additions are additive
(`IsModel3D`, `RegisterCustomMesh`, `RegisterBuildingRules`,
`OnTimeOfDayChanged`).

## NFR-WSM-007 — Mod coexistence (different GUID)
`mod.json` `GUID = worldsphere3d.melvinshwuaner.fork` so WSM3D installs
side-by-side with upstream `WorldSphereMod`. Verify by parallel install
in `Testing/Scenes/`.

## NFR-WSM-008 — Reference-rig performance baselines
Documented in `Testing/PERFORMANCE.md` for:
- `small_kingdom_500` (actor perf)
- `forest_5k` (foliage perf)
- `coastal_water` (water/shore)
- `crabzilla_boss` (skeletal special-case)

## NFR-WSM-009 — Visual regression coverage
For any rendering-affecting PR, before/after screenshots from 6 camera
angles diffed against the previous phase. `Tools/verify-visual.py`
provides SSIM compare; harness lives in
`tests/WorldSphereMod.Tests.Integration/VisualRegressionHarnessTests.cs`.

## NFR-WSM-010 — Reproducible build
`dotnet build WorldSphereMod.csproj -c Release` produces
`WorldSphereMod3D.dll`. AssetBundle workflow produces `worldsphere`
bundles for `win` / `linux` / `osx`. CI in
`.github/workflows/{build.yml,bundles.yml}` is the authoritative gate.

## NFR-WSM-011 — Determinism-on-launch, variety-in-world
NFR-WSM-011 is **NOT** a fixed-bit/seeded-only requirement (per project
convention: real randomness welcome for emergent variety). Launch-time
state must be reproducible for a given `SavedSettings` JSON; in-world
behavior is intentionally non-deterministic.

## NFR-WSM-012 — Coverage gate (xDD)
Line coverage on the Unity-free surface (currently `WorldSphereAPI`)
must trend; failure to publish the HTML report is a build hygiene
issue. Coverlet + reportgenerator wired in `Tools/coverage.ps1`;
report at `docs/coverage/index.html`. CI gate is a `TODO(xdd-coverage)`
placeholder in `build.yml` (not enabled until 3 consecutive green runs).
