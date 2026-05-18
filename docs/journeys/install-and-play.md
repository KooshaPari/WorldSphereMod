# Journey: Install & play

**Persona:** A WorldBox player who saw a screenshot of voxelized actors and wants to try it.
**Time:** ~10 minutes (plus a Steam download for WorldBox if not already owned).
**Prerequisites:** A licensed copy of WorldBox installed via Steam. Windows 10/11 (Linux/macOS supported but install script targets Windows; manual path works).

## Goal

Get `WorldSphereMod3D` installed alongside (or instead of) upstream `WorldSphereMod`, launched in WorldBox, and verify a voxel actor renders in 3D from a free-rotating camera.

## Steps

1. **Get the release.** Download the latest `WorldSphereMod3D-vX.Y.Z.zip` from
   [GitHub Releases](https://github.com/KooshaPari/WorldSphereMod/releases) or
   GameBanana. (Steam Workshop distribution is not currently available — see
   [`PLAN.md`](/PLAN#risks--open-items) Risks.)

2. **Locate your WorldBox install.** Default Steam path on Windows is
   `C:/Program Files (x86)/Steam/steamapps/common/Worldbox/`. The folder
   contains `worldbox_Data/` and the executable.

3. **Install.** From a PowerShell in the extracted release folder:

   ```powershell
   ./Tools/install.ps1
   ```

   The script auto-detects Steam, copies `Code/`, `Assemblies/`,
   `AssetBundles/`, `GameResources/`, `Locales/`, and `mod.json` into
   `<WorldBox>/Mods/WorldSphereMod3D/`, and runs a `dotnet build` sanity
   check. Override the install path with `-WorldBoxPath` or
   `$env:WORLDBOX_PATH`.

   On Linux/macOS, manually copy the `WorldSphereMod3D` folder to
   `~/.steam/steam/steamapps/common/worldbox/Mods/` (or your XDG path).

   ![PowerShell output from Tools/install.ps1 showing the auto-detected Steam path, per-folder copy log, and 0-Error dotnet build summary](./assets/install-and-play/01-fresh-install.png)

4. **Enable in NeoModLoader.** Launch WorldBox once with NeoModLoader installed;
   open the mod manager, enable `WorldSphereMod3D`. **Disable upstream
   `WorldSphereMod` if present** — they share Harmony patch surfaces and only
   one should patch the game at a time (the different `GUID`
   `worldsphere3d.fork` makes them *installable* side-by-side, not
   *runnable* side-by-side).

   ![WorldBox start screen with NeoModLoader mod manager open and WorldSphereMod3D toggled on alongside the WSM3D icon](./assets/install-and-play/02-mod-loaded.png)

5. **Launch the game.** A new world; sphere or flat shape is fine.

6. **Flip per-phase flags.** Open the WorldSphereMod tab (in-game settings
   panel). Phases default to OFF until validated; turn ON:
   - `Is3D` — master switch
   - `VoxelActors` — Phase 1 voxelization
   - `MeshBuildings` — Phase 2 procgen
   - `MeshWater` — Phase 4
   - `Worldspace UI` — Phase 7
   Leave `HighShadows` / `PostFX` / `RigDriver` off for the first session
   (cost gate, lit-shader bake gate; see [phase 5 prep](/phase5-prep) and
   [phase 6 architecture](/phase6-architecture)).

   ![Settings panel with the WorldSphere tab open, per-phase toggles list visible, the five default-on flags highlighted](./assets/install-and-play/03-settings-tab.png)

   ![Animated capture of actors switching from sprite billboards to voxel meshes when VoxelActors is toggled on mid-game](./assets/install-and-play/04-voxel-on.gif)

7. **Verify in-game.** Spawn a kingdom. Rotate the camera 360°. Voxel
   silhouettes should hold from any angle, with no z-fighting against the
   terrain. Sprite-billboard fallback is what you'll see on flags that are
   OFF.

   ![Wide kingdom overview with voxel actors, mesh buildings, mesh water, and worldspace nameplates all visible on the spherical terrain](./assets/install-and-play/05-after-overview.png)

## Outcome

You see voxel actors and procedural building meshes rendering on the spherical
3D terrain that upstream gave you. UI overlays follow units in world space.

## Variants

- **Side-by-side with upstream**: install both folders, enable one at a time.
  Useful for visual diffing.
- **Hardware fallback path** (older GPU, no compute shader / no indirect
  args): the impostor LOD path engages automatically — you get a
  3D-positioned billboard equivalent to upstream, not voxel meshes. See
  [phase 10 architecture](/phase10-architecture).
- **Locked WorldBox version mismatch**: if the startup log warns about
  Harmony patch signatures not matching, your WorldBox version drifted past
  the build target. Pin to the version called out in the release notes or
  open an issue.

## Troubleshooting

- *Mod icon turns red in NeoModLoader* → GPU does not meet the compute /
  indirect-args gate in `Mod.cs:21`. Open `WorldBox/output_log.txt`, search
  for `IncompatibleHardwareException`. With the Phase-10 soft gate the mod
  still loads in fallback mode, but Phase 1 voxelization will not engage.
- *Crash on load* → confirm upstream `WorldSphereMod` is disabled. Two
  patchers on the same methods will throw a Harmony conflict.
- *Black terrain* → AssetBundle missing for your platform; reinstall from
  the platform-matched release.
