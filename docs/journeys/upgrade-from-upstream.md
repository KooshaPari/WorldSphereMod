# Journey: Upgrade from upstream WorldSphereMod

**Persona:** An existing player or mod author of
[`MelvinShwuaner/WorldSphereMod`](https://github.com/MelvinShwuaner/WorldSphereMod)
who has heard about the fork and wants to switch.
**Time:** ~10 minutes (player) / ~1 hour (mod author with custom integrations).
**Prerequisites:** Current upstream `WorldSphereMod` installation.

## Goal

Switch from upstream to `WorldSphereMod3D` cleanly, understand what
behavior changes, and (for mod authors) port any custom API integrations.

## Steps

### As a player

1. **Disable upstream `WorldSphereMod`** in NeoModLoader. Don't uninstall
   yet — keep it around in case you want to revert. The two mods share
   Harmony patch surfaces and must not both patch at once.

2. **Install the fork**, following the [install & play
   journey](/journeys/install-and-play). The fork uses
   `GUID = worldsphere3d.fork`, so it lives in a separate `Mods/` folder
   and won't overwrite the upstream install.

   ![NeoModLoader mod manager listing both upstream WorldSphereMod and WorldSphereMod3D side-by-side, upstream disabled and fork enabled](./assets/upgrade-from-upstream/01-coexist.png)

3. **Enable `WorldSphereMod3D`** in NeoModLoader.

4. **Load your existing save.** Upstream saves are compatible — the fork
   doesn't change save layout. The terrain renderer is the same backend
   (`Compound-Spheres-3D` is a build-compatible rebuild of upstream's
   vendored `CompoundSpheres.dll`, see
   [ADR-0002](/adr/0002-defer-shader-bake-to-unity-2022-3) and
   [`PLAN.md` Phase 0](/PLAN#phase-0--fork-plumbing-12-days)).

   ![Player.log excerpt showing the settings migration v1.5 → v2.0 log line as the fork picks up the existing config and defaults the new v2 flags](./assets/upgrade-from-upstream/02-migration-log.png)

5. **Compare visuals.** Out of the box you'll see:
   - **Same** terrain mesh, water tiling, camera, settings tab.
   - **Different**: nameplates / HP / damage popups now in worldspace
     (Phase 7), trees / bushes / rocks are crossed quads instead of
     billboards (Phase 3), and particle bursts on 5 effect IDs (Phase 9).
   - **Off by default**: voxel actors, procgen buildings, mesh water,
     skeletal animation, high shadows, day/night, PostFX. Flip the
     corresponding flags in the settings tab to opt in (see [install &
     play](/journeys/install-and-play) step 6).

   ![Before/after split-screen: upstream sprite-billboard actors on the left, WorldSphereMod3D voxel actors with worldspace UI on the right, same world seed](./assets/upgrade-from-upstream/03-before-after.png)

### As a mod author (you ship a mod that uses `WorldSphereAPI`)

1. **Your existing API calls still work.** The v1 surface is preserved
   verbatim: `IsWorld3D`, `MakeActorNonUpright`,
   `MakeBuildingNonUpright`, `MakeProjectileNonUpright`, `EditEffect`,
   `GetSetting<T>`. Nothing to port.

2. **Detect the fork at runtime.** A new `IsModel3D` property tells you
   whether you're talking to the fork or upstream:

   ```csharp
   if (WorldSphereAPI.Connect(out var api)) {
       if (api.IsModel3D) {
           // You're on WorldSphereMod3D — v2 calls are available.
       } else {
           // Upstream. Skip v2-only registrations.
       }
   }
   ```

3. **Opt into v2 features** — see [extend via API](/journeys/extend-via-api)
   for `RegisterCustomMesh`, `RegisterBuildingRules`, `RegisterRig`,
   `RegisterEffectMesh`, `OnTimeOfDayChanged`.

4. **Rebuild and ship.** No reference change needed: v2 additions are
   backwards-compatible on the same `WorldSphereAPI.dll`. The fork's DLL
   has a higher version stamp but exposes a superset.

## Outcome

You're running the fork. Your save works. Your downstream mod continues to
function unchanged, and can now opt in to fork-specific 3D extensions.

## Variants

- **Side-by-side install** (advanced): keep both folders, enable only one
  at a time in NeoModLoader. Useful for visual diffing or for fall-back to
  upstream if you hit a regression. See [install & play](/journeys/install-and-play)
  variants.
- **Revert**: re-enable upstream `WorldSphereMod` in NeoModLoader, disable
  the fork. No save migration needed.
- **Compat layer for the fork being upstream-feature-equivalent on a flag-
  off setup**: with every v2 flag OFF, the fork behaves as a
  bug-fix-and-perf-improved upstream (you keep the worldspace UI and
  crossed-quad foliage default-ons, but those are visually-additive). To
  match upstream pixel-for-pixel, also turn off `Worldspace UI` and Phase
  3 — but at that point you may as well run upstream.

## Pitfalls

- **Both mods enabled at once** → Harmony will scream on startup. Disable
  one. The fork won't load over the top of upstream cleanly.
- **Custom `Constants.PerpActors` / `PerpBuildings` overrides** from a
  mod that monkey-patched upstream's constants: those still apply, but
  may interact with Phase 1 voxelization. If your mod overrides
  orientation for an asset, register a custom mesh too
  (`RegisterCustomMesh`) so voxelization doesn't override your
  orientation choice.
- **`mod.json` GUID expectations**: any mod that hard-checks
  `worldsphere.melvinshwuaner` will not detect the fork (`worldsphere3d.fork`).
  Update such checks to `IsModel3D` instead. See [ADR-0001](/adr/0001-hybrid-sprite-to-3d-strategy)
  for why the fork ships under a distinct GUID.
