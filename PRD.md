# PRD — WorldSphereMod3D

**Document ID:** PRD-WSM-001
**Status:** Living
**Owner:** @KooshaPari
**Last updated:** 2026-05-18

---

## 1. Problem

Upstream `MelvinShwuaner/WorldSphereMod` ships under the marketing line
"the 3D Worldbox mod". In practice only the **terrain** is a real 3D mesh;
every visible entity — actors, buildings, drops, items, projectiles,
effects, talk bubbles, shadows, and even bosses like Crabzilla — is still a
2D `SpriteRenderer` quad rotated to face the camera. UI is flat Canvas,
water is per-tile colour, foliage is sprite cards, lighting is a skybox
texture plus baked per-tile colour, and animation is frame-swap sprite
arrays. The illusion breaks the moment the camera tilts.

## 2. Vision

`WorldSphereMod3D` is a hard fork that finishes the 3D conversion **without
losing WorldBox's pixel-art identity**. Sprites are voxelized into cube
meshes; buildings get heuristic procgen geometry; trees/clouds/rocks become
crossed quads; water is a mesh surface with Gerstner waves; a directional
sun casts cascaded shadows; actors animate via auto-rigged skeletons; UI
lives in world space; and a graceful impostor-billboard LOD path keeps the
mod usable on hardware that fails the upstream compute-shader gate.

## 3. Target users

| Persona | What they want |
|---|---|
| **WorldBox player** with a modern GPU | A more immersive sandbox — real 3D armies and cities — without giving up the pixel-art look. |
| **Modder building on WorldSphereMod** | A backwards-compatible `WorldSphereAPI` (v1) plus v2 surfaces for registering custom meshes, building rules, and skeletons. |
| **Solo dev / hobbyist** on integrated graphics | The impostor fallback path so the mod still renders in 3D space on Intel UHD-class hardware. |
| **Future maintainers (Phenotype agents)** | A repo whose spec roots, ADRs, plans, and conventions live where every other Phenotype repo puts them. |

## 4. Success criteria

| Dimension | Bar | How measured |
|---|---|---|
| Visual fidelity | Voxel silhouette matches sprite within ~1 voxel from any camera angle | Side-by-side screenshots, `docs/screenshots/phase-N-*.png` |
| Performance (target rig: RTX 3060 / 5600X) | Sustained 60 fps with 5000 actors + 1000 buildings + 5000 trees on a vanilla map | `Perf/FrameProfiler` 1s window flush |
| Fallback performance (Intel UHD 620) | 60 fps with impostor-billboard path active | Same profiler, hardware-gate softens to `ImpostorOnlyMode` |
| API compatibility | All v1 calls (`IsWorld3D`, `MakeActorNonUpright`, `EditEffect`, `GetSetting<T>`) work unchanged against the fork | `WorldSphereTester/` regression mod |
| Co-installability | Mod installs alongside upstream without GUID collision | GUID `worldsphere3d.fork` vs upstream |
| Phase isolation | Every phase ships behind a `SavedSettings` flag; flipping one phase OFF doesn't break another | Manual toggle matrix per release |
| Build hygiene | `dotnet build WorldSphereMod.csproj -c Release` returns 0 errors on any contributor's machine given `WORLDBOX_PATH` | CI on `WorldSphereAPI.csproj`, local build for full mod |

## 5. Non-goals

- **Re-shading the terrain backend from scratch.** `CompoundSpheres.dll` is
  retained as the terrain renderer; a submodule fork (`Compound-Spheres-3D`)
  is the *upgrade path*, not a v1 requirement.
- **Replacing WorldBox's simulation.** This mod is pure rendering + UI.
- **Multiplayer / netcode changes.** Out of scope.
- **Custom shader bake on first ship.** Phases 5/8 ship with placeholder
  unlit materials until a Unity 2022.3 install bakes the lit variants
  (`docs/phase5-prep.md`). Mod functions degraded but non-broken without
  the bake.
- **Modifying upstream's `mod.json` GUID.** Co-install behaviour requires
  it stays `worldsphere3d.fork`.

## 6. Constraints

- **NeoModLoader runtime-compiles `Code/*.cs`** — no precompiled IL ships in
  the install.
- **WorldBox reference assemblies are local-only** — CI cannot build the
  full mod; only `WorldSphereAPI.csproj` (netstandard2.0, Unity-free) is
  CI-buildable.
- **~80 existing Harmony patches** in upstream files (`Core.cs`,
  `QuantumSprites.cs`, `Effects.cs`, etc.) — touching those risks cascade
  failures across phases.
- **Hardware gate** (`Mod.cs:21`) requires compute / instancing /
  indirect-args support, softened in Phase 10 to flip `ImpostorOnlyMode`
  instead of throwing.

## 7. Out of band

- License: dual MIT / Apache-2.0 (org default).
- Distribution: GitHub Releases + GameBanana. Steam Workshop is *not* a
  target until upstream's removal cause is understood.
- Versioning: semver from `VERSION` file; CHANGELOG.md is hand-curated
  Keep-a-Changelog 1.1.0.
