# Handoff — pick up locally

State of the world for the next Claude Code instance (or human) resuming
work on this fork.

## Where things are

| Thing | Location |
|---|---|
| Active branch | `claude/research-ultraplan-fork-DdgI5` |
| Open draft PR | https://github.com/KooshaPari/WorldSphereMod/pull/1 |
| Full plan | `docs/PLAN.md` |
| Phase 1 review | `docs/phase1-review.md` |
| Phase 2 architecture | `docs/phase2-architecture.md` |
| `render_data` field map | `docs/render-data-fields.md` |
| Voxel module | `WorldSphereMod/Code/Voxel/` |
| ProcGen module (Phase 2) | `WorldSphereMod/Code/ProcGen/` (scaffolding) |
| Install script | `Tools/install.ps1` |
| Build portability | `Directory.Build.props` (uses `WORLDBOX_PATH` env) |
| CI | `.github/workflows/build.yml` (API build only — mod build is local) |

## What has landed since the original handoff

- Phase 1 voxel pipeline scaffolding (actor render Postfix + driver + cache).
- `chore: default unimplemented phase flags to false` — Phases 2-8 default OFF
  per the project's "default-OFF until validated" rule.
- `perf(voxel): greedy meshing in SpriteVoxelizer` — ~5-10x vertex reduction.
- `tooling: add Tools/install.ps1 for fast iteration`.
- Decompiled `render_data` field map for both `ActorManager` and
  `BuildingManager` saved to `docs/render-data-fields.md`. `BuildingRenderData`
  has **no `has_normal_render`** field — voxel-building sprite suppression
  must use `main_sprites[i] = null` (verify) or `scales[i] = Vector3.zero`.
- Phase 2 procgen architecture finalised; 8-commit implementation plan
  in `docs/phase2-architecture.md`.
- Phase 1 code review identified 5 issues; fixes #1-4 in progress in a
  bundled "Phase 1 hardening" commit.

## Local build + install

This Windows machine has WorldBox at the default Steam path. The full
mod build is `dotnet build WorldSphereMod.csproj -c Release` (~5s, 0
errors, 46 pre-existing warnings). The install procedure is automated:

```powershell
./Tools/install.ps1
```

NML compiles `Code/*.cs` at runtime, so install copies source + AssetBundles
+ Assemblies + GameResources + Locales + mod.json into
`<WorldBox>/Mods/WorldSphereMod3D/`. Override the WorldBox path with
`-WorldBoxPath` or `$env:WORLDBOX_PATH`.

## In-game smoke test (Phase 1)

After install, with the active "Phase 1 hardening" commit applied:

1. Launch WorldBox; confirm terrain renders identically to upstream (regression
   check on Phase 0 plumbing).
2. Open Settings → WorldSphere tab → enable "Voxel Entities".
3. Generate a small kingdom (~500 units). Sweep camera 360°.
4. Verify:
   - Actors render as voxel meshes from any angle (unlit; that's expected
     until Phase 5).
   - Actors do **not** lean / topple while walking (regression on review #4).
   - Per-actor tint colors are correct on every actor in the final batch
     (regression on review #1; happens when actor count is not a multiple of
     1023).
5. Capture before/after screenshots into `docs/screenshots/phase-1-*.png`
   and link from PR #1.

## Recommended next steps

1. **Land Phase 1 hardening** (in flight). Fixes #1-#4 from `docs/phase1-review.md`.
2. **Smoke test Phase 1** per the steps above. If clean, flip
   `SavedSettings.VoxelEntities = true` and call Phase 1 done.
3. **Voxel buildings re-enable.** Field map is at `docs/render-data-fields.md`.
   The actor Postfix is the template; building variant must drop the
   `has_normal_render`, `has_item`, `item_*`, `shadow_position`, and
   `shadow_scales` branches. Gate behind `VoxelEntities && !ProceduralBuildings`.
4. **Ship the real `VoxelLit.shader`** (half a day with Unity).
   Unity Hub + 2021.3.45f1 + 6000.3.11f1 are installed on this machine. The
   PLAN spec'd 2022.3 specifically — check AssetBundle compatibility before
   committing to 2021.3.
5. **Phase 2 procedural buildings.** Architecture is in
   `docs/phase2-architecture.md`. Implement in the 8 atomic commits the
   doc lists. Ship as its own PR.

## Open design questions

- Per-instance color via `_InstanceColor`: declared in `MeshInstanceBatcher`
  but the placeholder shader doesn't read it. The Phase 5 lit shader needs
  to honor it.
- Skeletal animation (Phase 6) will replace static voxel meshes with
  skinned variants. Plan to change the cache key from
  `(Sprite.GetInstanceID, depth)` to `(SpriteId, rigId)` when rigging arrives.
- External `BuildingRules` ergonomics: API takes `object` (delegate-type
  boundary). Consider adding a `RegisterBuildingRules(string assetId,
  string rulesJson)` overload so external mod authors don't need to copy
  the struct. See Phase 2 architecture risk #4.

## Don't forget

- `claude/research-ultraplan-fork-DdgI5` is the dev branch. Push there,
  not to `main`.
- One PR per phase (Phase 1 fixes amend Phase 1's PR; Phase 2 ships its
  own PR). Commits within a phase can be incremental.
- Don't bump the GUID in `mod.json` casually — it's how this fork stays
  co-installable with upstream.
- Hooks: SessionStart will load skills + memory automatically; the
  /loop 5m cadence drives recurring re-entry.
