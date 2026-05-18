# WorldSphereMod3D (hard fork)

A hard fork of [MelvinShwuaner/WorldSphereMod](https://github.com/MelvinShwuaner/WorldSphereMod)
that finishes the 3D conversion of WorldBox.

Upstream `WorldSphereMod` puts the **terrain** in 3D but leaves every visible
entity — actors, buildings, drops, items, projectiles, effects, talk bubbles,
shadows, even Crabzilla and dragons — as a 2D `SpriteRenderer` that's been
rotated to face the camera. UI is flat Canvas, water is tile colour, foliage
is sprite cards, lighting is a skybox + per-tile baked colour, animation is
frame-swap sprite arrays.

This fork lands a real 3D pipeline on top of that foundation:

| Phase | Status | What changes |
|---|---|---|
| 0  | landed       | Fork metadata, build portability (`WORLDBOX_PATH`), CI, settings/API v2 |
| 1  | ready-to-test | Voxelized actors + buildings (with greedy meshing); awaits in-game smoke test |
| 2  | code-complete | Procedural building meshes (heuristic landed); gated, awaits in-game smoke test |
| 3  | code-complete (3a) | Trees/bushes/rocks as crossed-quads + cloud refactor pending (see `docs/phase3-architecture.md`) |
| 4  | code-complete (lite) | Mesh water — WaterGerstner shader source landed; AssetBundle bake deferred to Phase 5b |
| 5  | research     | Directional sun + cascaded shadows + SSAO (see `docs/phase5-prep.md`) |
| 6  | planned      | Skeletal animation driver (auto-rigged from sprite anatomy) |
| 7  | code-complete (no selection hook) | Worldspace UI: nameplate, HP bar, damage popups landed; selection ring code in place pending SelectionManager investigation. |
| 8  | code-complete (autonomous) | TimeOfDay autonomous driver + SunRig color gradient; ProceduralSky shader source landed; MapBox.world_time probe falls back since field absent. |
| 9  | code-complete | Particle bursts on 5 effect IDs + 3-channel DecalPool + URP PostFX (opt-in via PostFX flag, default off). |
| 10 | code-complete (no proxy) | FrustumCuller + LodSelector + ImpostorBillboard + softened hardware gate; Proxy tier still routes to Voxel. |

The full plan, including file-by-file changes and verification steps, lives
at `docs/PLAN.md`.

## Installation

This fork uses a different `GUID` (`worldsphere3d.fork`) than upstream so it
is **co-installable** with the original `WorldSphereMod`. Enable only one at a
time in NeoModLoader to avoid double-patching.

NeoModLoader compiles `Code/*.cs` at runtime, so the install copies sources
(plus `Assemblies/`, `AssetBundles/`, `GameResources/`, `Locales/`, `mod.json`)
into `<WorldBox>/Mods/WorldSphereMod3D/`. On Windows the fastest path is:

```powershell
./Tools/install.ps1
```

The script auto-detects the default Steam install at
`C:/Program Files (x86)/Steam/steamapps/common/Worldbox/`. Override with
`-WorldBoxPath`, `-InstallFolderName`, or by setting `$env:WORLDBOX_PATH`.
Pass `-SkipBuild` to skip the `dotnet build` sanity check.

## Building

The build uses your local WorldBox install for reference assemblies. Point
`WORLDBOX_PATH` at it (the folder containing `worldbox_Data/`):

```bash
# Linux/macOS
export WORLDBOX_PATH="$HOME/.steam/steam/steamapps/common/worldbox"
dotnet build WorldSphereMod.csproj -c Release

# Windows PowerShell
$env:WORLDBOX_PATH = "C:/Program Files (x86)/Steam/steamapps/common/worldbox"
dotnet build WorldSphereMod.csproj -c Release
```

If `WORLDBOX_PATH` is unset the build falls back to the default Steam
location for the host OS (see `Directory.Build.props`).

## API

External mods linking against `WorldSphereAPI.dll` keep the v1 surface
unchanged. New v2 calls (`IsModel3D`, `RegisterCustomMesh`, …) safely no-op
when the connected host is upstream rather than this fork.

```csharp
if (WorldSphereAPI.Connect(out var api))
{
    if (api.IsModel3D) {
        api.RegisterCustomMesh("my_unit", myHandMadeMesh, myAlbedo);
    }
}
```

## Compatibility & hardware

The compute-shader / GPU-instancing / indirect-args gate from upstream is
preserved (`Mod.cs:21`). Hardware that fails the gate gets the same red icon
as before. Phase 10 will add an impostor-billboard fallback path so the mod
still does *something* useful on incompatible GPUs.

## Credits

- Upstream mod and `Compound-Spheres` rendering backend: **Melvin Shwuaner**.
- This fork: documented in `docs/PLAN.md`.

## Backend

Upstream ships `CompoundSpheres.dll` as a vendored binary built from
[MelvinShwuaner/Compound-Spheres](https://github.com/MelvinShwuaner/Compound-Spheres).
Phase 0 keeps the vendored DLL for compatibility; Phase 5 replaces it with a
submodule build (`External/Compound-Spheres-3D/`) that emits per-vertex normals
and exposes a water-mask buffer.
