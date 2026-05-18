# Handoff — pick up locally

State of the world for the next Claude Code instance (or human) resuming work.

## Where things are

| Thing | Location |
|---|---|
| Active branch | `claude/research-ultraplan-fork-DdgI5` |
| Open draft PR | https://github.com/KooshaPari/WorldSphereMod/pull/1 |
| Full plan | `docs/PLAN.md` |
| Phase 1 review | `docs/phase1-review.md` |
| Phase 1 smoke test | `docs/smoke-test-phase1.md` |
| Phase 2 architecture | `docs/phase2-architecture.md` |
| Phase 3 architecture | `docs/phase3-architecture.md` |
| Phase 4 architecture | `docs/phase4-architecture.md` |
| Phase 5 prep notes | `docs/phase5-prep.md` |
| `render_data` field map | `docs/render-data-fields.md` |
| Voxel module | `WorldSphereMod/Code/Voxel/` |
| ProcGen module (Phase 2) | `WorldSphereMod/Code/ProcGen/` |
| Install / Uninstall scripts | `Tools/install.ps1`, `Tools/uninstall.ps1` |
| Build portability | `Directory.Build.props` (`WORLDBOX_PATH` env) |
| CI | `.github/workflows/build.yml` (API build only — mod build local) |

## What has landed

**Phase 0** — fork plumbing, Phase flag defaults corrected to OFF.

**Phase 1** — voxel pipeline complete:
- `Voxel/SpriteVoxelizer.cs` with greedy meshing (~5-10× vertex reduction).
- `Voxel/VoxelMeshCache.cs` (lock + deferred destroy).
- `Voxel/MeshInstanceBatcher.cs` (per-batch sized color array).
- `Voxel/VoxelRender.cs` — Postfixes on both `ActorManager.precalculateRenderDataParallel`
  and `BuildingManager.precalculateRenderDataParallel`. Yaw-only rotation. `VoxelFrameDriver`
  flushes batcher + drains both caches in `LateUpdate`.
- All 5 Phase 1 review issues addressed (#5 deferred — read-back not needed yet).

**Phase 2** — procgen buildings, 6 of 8 commits landed in this PR:
- Step 1: `ProcGen/BuildingRules.cs` data types.
- Step 2: `ProcGen/ProcGenCache.cs` LRU + deferred destroy.
- Step 3: real heuristic pipeline (footprint, stories, openings, walls) in
  `BuildingMeshGen.cs`.
- Step 4: gable/hipped/flat roof inference + outward-normal winding fix.
- Step 5: `ProcGen/BuildingProcRender.cs` Postfix wired; `BuildingRulesRegistry`.
- Step 6: `WorldSphereModAPI.RegisterBuildingRules` (internal) and external delegate
  binding (Unity-free).
- **Pending**: Step 7 (flip `ProceduralBuildings=true` default) blocks on smoke test.
  Step 8 (PR + screenshots) blocks on Phase 1 in-game.

**Phase 3 + Phase 4** — pre-implementation design docs only. No code yet.

**Tooling**:
- `./Tools/install.ps1` — one-shot build + copy into `<WorldBox>/Mods/WorldSphereMod3D/`.
- `./Tools/uninstall.ps1` — remove the installed copy.

## Local build + install

WorldBox is at the default Steam path. Full mod build is `dotnet build WorldSphereMod.csproj -c Release` (~5s, 0 errors, ~47 pre-existing warnings).

```powershell
./Tools/install.ps1
```

NML compiles `Code/*.cs` at runtime, so install copies source + assets + `Assemblies/CompoundSpheres.dll`.

## In-game smoke test (Phase 1)

See `docs/smoke-test-phase1.md` for the full checklist. Quick version:

1. `./Tools/install.ps1` → launch WorldBox.
2. Confirm terrain regression-clean with `VoxelEntities = false` (default).
3. Toggle on, generate 500-unit kingdom, sweep camera 360°.
4. Verify: voxel actors render, no body topple while walking, all batches tinted correctly, no flicker on 1023-unit boundaries.
5. Optional: enable `VoxelEntities && !ProceduralBuildings` to see voxel buildings.
6. Capture before/after into `docs/screenshots/phase-1-*.png`.

## Recommended next steps

1. **Smoke test Phase 1** (user-driven). If clean, flip `SavedSettings.VoxelEntities = true` and call Phase 1 done.
2. **Smoke test Phase 2** (user-driven). Toggle `ProceduralBuildings = true` and verify the heuristic produces reasonable building meshes for vanilla assets. Tweak `BuildingMeshGen` heuristic thresholds if many false-positive gables/hipped roofs.
3. **Phase 3 implementation** — blocked on confirming the WorldBox top-tile draw entry point. Decompile investigation in flight.
4. **Phase 4 implementation (Phase 4-lite)** — design ships independently of Phase 5 backend work. CPU height-threshold mask is sufficient.
5. **Phase 5** — Unity 2022.3 install + `Compound-Spheres-3D` submodule. Adds per-vertex normals + water-mask SSBO. See `docs/phase5-prep.md`.

## Open design questions

- Per-instance color via `_InstanceColor`: declared in `MeshInstanceBatcher` but the placeholder shader doesn't read it. Phase 5 lit shader must honor it.
- Skeletal animation (Phase 6) cache key: currently `Sprite.GetInstanceID`. Switch to `(SpriteId, rigId)` when rigging arrives.
- External `BuildingRules` ergonomics: API takes `object` (delegate boundary). Consider a `RegisterBuildingRules(string assetId, string rulesJson)` overload so external mod authors don't need to copy the struct.
- `VoxelRender.Reset()` exists but isn't wired to a world-reload hook (no such hook exists in `Core` yet). Multi-world sessions may stop rendering voxels after the second `World.NewWorldStart` until restart.

## Don't forget

- `claude/research-ultraplan-fork-DdgI5` is the dev branch. Push there, not `main`.
- One PR per phase. Commits within a phase can be incremental.
- Don't bump the GUID in `mod.json` casually — it's the co-installable identity.
- Phase 2-8 flags all default OFF per project rule. Each phase flips its own when validated.
