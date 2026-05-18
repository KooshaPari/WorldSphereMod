# CLAUDE.md — WorldSphereMod3D fork

Read this first when you join the project cold.

## What this is

A hard fork of `MelvinShwuaner/WorldSphereMod`, a NeoModLoader/Harmony mod
for WorldBox. Upstream renders **terrain** as a real 3D mesh but every
visible entity (actors, buildings, drops, items, projectiles, effects,
shadows) is still a 2D `SpriteRenderer` rotated to face the camera. This
fork — `WorldSphereMod3D` — is finishing the 3D conversion in 10 phases:
voxelized actors/items, procedural building meshes, crossed-quad foliage,
mesh water, real sun + cascaded shadows, skeletal animation, worldspace UI,
day/night, post-FX, and an LOD/impostor fallback.

## Start here

1. **`docs/HANDOFF.md`** — current state, what's blocked on a local Unity
   install, prioritized next steps.
2. **`docs/PLAN.md`** — the full 10-phase plan with file paths and
   verification per phase.
3. **PR #1** on GitHub — draft, all CI green at handoff, phase-by-phase commits.

## Conventions

- **Branch:** `claude/research-ultraplan-fork-DdgI5`. Push there, not `main`.
- **One PR per phase.** Don't bundle multiple phases into one PR.
- **`mod.json` GUID** is `worldsphere3d.fork` — co-installable with upstream.
  Don't change it casually.
- **`SavedSettings` flags** gate every phase. New phases ship default-OFF until
  validated in-game.
- **No new comments** explaining what code does. Comments only when they
  capture a non-obvious *why* (invariant, workaround, hidden constraint).
- **External assemblies** are referenced via `$(WorldBoxPath)` from
  `Directory.Build.props`; never hard-code Steam paths in a `.csproj`.

## Build

```bash
# Linux/macOS
export WORLDBOX_PATH="$HOME/.steam/steam/steamapps/common/worldbox"
dotnet build WorldSphereMod.csproj -c Release

# Windows PowerShell
$env:WORLDBOX_PATH = "C:/Program Files (x86)/Steam/steamapps/common/worldbox"
dotnet build WorldSphereMod.csproj -c Release
```

CI in `.github/workflows/build.yml` builds only `WorldSphereAPI.csproj`
(it's Unity-free, targets netstandard2.0). The main mod can't be built in
CI because it needs WorldBox's reference DLLs — that's local-only.

## Where to make changes

| You want to… | Look in |
|---|---|
| Add a new render mode flag | `WorldSphereMod/Code/SavedSettings.cs` |
| Add a public API method | `WorldSphereAPI/WorldSphereAPI.cs` (external) + `WorldSphereMod/Code/WorldSphereAPI.cs` (internal). Update both. |
| Hook a new WorldBox method | Add `[HarmonyPatch]` types to a new file under `WorldSphereMod/Code/` and they'll be picked up by `Patcher.PatchAll()` in `Core.Patch`. |
| Add a per-frame driver | Drop a `MonoBehaviour` and `AddComponent` it to `Mod.Object` in `Mod.Init`. See `Voxel/VoxelFrameDriver` for the pattern. |
| Convert 2D coords to 3D | `Tools.To3D`, `Tools.To3DTileHeight`, `Tools.To2D`. Don't re-derive. |
| Get a tile's terrain height | `Tools.GetTileHeightSmooth`. |
| Add a 3D mesh draw | `Voxel/MeshInstanceBatcher.Submit(mesh, material, matrix, color)` then call `Flush()` once per frame. |
| Voxelize a sprite | `Voxel/VoxelMeshCache.Get(sprite)` — cached. |

## What's a fork-specific concern vs. upstream

If you're touching anything in `WorldSphereMod/Code/Voxel/`, the
`SavedSettings` v2 fields, the new `WorldSphereAPI` v2 methods
(`IsModel3D`, `RegisterCustomMesh`, `OnTimeOfDayChanged`), or the
`Directory.Build.props` portability layer — that's fork-specific, ship it.

If you're touching `Core.cs`, `QuantumSprites.cs`, `3DCamera.cs`,
`Effects.cs`, `Tools.cs`, `DimensionConverter.cs`, `General.cs`,
`TileMapToSphere.cs`, `CompoundSphereScripts.cs` — those are inherited
from upstream. Tread carefully. The mod has ~80 Harmony patches across
those files; changes can cascade.

## Pitfalls and surprises

- **Z-displacement sentinel.** `Constants.ZDisplacement = 100` is used as a
  magic value to detect "this Vector3 was already converted to 3D space."
  Don't naively add height to a position without checking.
- **Cylindrical X-wrapping.** When `CurrentShape == 0` (the default), X
  coordinates wrap around the world. Use `Tools.Dist`/`Tools.WrappedDist`
  for any distance math, never raw `Vector3.Distance`.
- **Parallel render passes.** `ActorManager.precalculateRenderDataParallel`
  and `BuildingManager.precalculateRenderDataParallel` run on a worker
  pool. Anything you do in a Postfix on those needs to be thread-safe or
  to run synchronously after `Parallel.For` exits. Most Postfix code does
  run after, but be explicit.
- **Compute-shader gate.** `Mod.OnLoad` throws `IncompatibleHardwareException`
  if the GPU doesn't support instancing/compute/indirect-args. The mod
  icon turns red. Don't relax this gate; build the impostor fallback path
  in Phase 10 instead.
- **AssetBundle paths.** Bundles live in `WorldSphereMod/AssetBundles/{win,
  linux,osx}/worldsphere`. The platform-specific files are binary blobs
  rebuilt from Unity 2022.3 — not editable by hand.

## When you're done with a phase

1. Toggle the corresponding `SavedSettings` flag to `true` by default.
2. Update `README.md`'s phase table from `planned` / `scaffolding` to
   `landed`.
3. Update `docs/HANDOFF.md`'s "Recommended next steps" list.
4. Commit, push, mark the PR ready for review (still draft until then),
   and let CodeRabbit do a pass.
