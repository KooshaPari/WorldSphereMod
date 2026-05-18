# Journey: Diagnose performance

**Persona:** A power user (or contributor) whose framerate has dropped in
a specific scenario — large kingdom, dense forest, near the shoreline —
and wants to identify which phase is the bottleneck.
**Time:** ~15 minutes per scenario.
**Prerequisites:** WorldSphereMod3D installed, the game launched, the
problem world either loaded or reproducible from a fresh map.

## Goal

Turn on the profiler, read the `[WSM-PROF]` log lines, and identify which
phase (voxel / procgen / foliage / water / lighting / rig / UI / sky /
particles / LOD) is consuming the most frame time.

## Steps

1. **Enable the profiler.** In `SavedSettings` (in-game UI tab, or
   directly in the settings JSON), turn on `ProfilerDump`. This is the
   `--profile-mode` console flag from
   [`PLAN.md` Phase 10](/PLAN#phase-10--performance-fallbacks-polish-3-5-days);
   it dumps a per-system breakdown of `Time.deltaTime` once per second.

2. **Reproduce the slow scenario.** Load the save / generate the world
   that triggers the drop. Sit on the laggy view for 10–30 seconds so the
   profiler has time to log several samples.

3. **Read `[WSM-PROF]` lines.** Open the WorldBox log:
   - Windows: `%APPDATA%/../LocalLow/Maxim Karpenko/WorldBox/Player.log`
     (or via NeoModLoader's log viewer if enabled).
   - Linux: `~/.config/unity3d/Maxim Karpenko/WorldBox/Player.log`

   Filter for `[WSM-PROF]`. Each line is:

   ```
   [WSM-PROF] frame=4321 dt=18.6ms voxel=4.2 procgen=1.1 foliage=2.0 water=0.8 lit=3.4 rig=2.1 ui=0.6 sky=0.3 fx=0.9 lod=0.4 misc=2.8
   ```

   The largest bucket is your hot phase. `dt` in excess of ~16 ms means
   sub-60 fps.

4. **Cross-reference with the per-phase target.** Each architecture doc
   has a perf budget in its "Verify" section. Recap:

   | Phase | Budget on RTX 3060 / 5600X reference |
   |---|---|
   | 1 Voxel actors        | 500 actors ≤ ~3 ms     |
   | 2 Procgen buildings   | 1000 buildings ≤ 5 ms  |
   | 3 Crossed-quad foliage| 5k trees ≤ 3 ms        |
   | 4 Mesh water          | 1 sea ≤ 1 ms           |
   | 5 Sun + shadows       | gated; ≤ 4 ms with `HighShadows` on |
   | 6 Rig driver          | 1000 skinned ≤ 4 ms    |
   | 7 Worldspace UI       | 100 elements ≤ 1 ms    |
   | 8 Sky + TOD           | ≤ 0.5 ms               |
   | 9 Particles + decals  | ≤ 1 ms                 |
   | 10 LOD / culling      | overhead < 0.5 ms      |

5. **Bisect with flags.** Turn off the suspected phase's flag in
   `SavedSettings` and re-measure. The delta confirms (or refutes) the
   suspicion.

6. **Drop to the LOD fallback.** For Phase 1, lower `LODScale` to push the
   voxel-to-proxy and proxy-to-impostor thresholds closer. The
   billboard-impostor tier is the same path used by the hardware-fallback
   in [phase 10](/phase10-architecture); if it brings you back to 60 fps,
   the bottleneck is voxel/proxy mesh count, not lighting.

7. **Report.** If a phase consistently exceeds its budget, open a GitHub
   issue with:
   - The `[WSM-PROF]` lines (10+ frames).
   - Repro: world seed, age, approximate actor / building / tree counts.
   - GPU + CPU model.

## Outcome

You know which phase is hot, whether it's a config / scale issue (LOD
threshold, foliage density) or a code issue (genuine regression). You
either tune it via the settings the phase exposes, or file an actionable
bug.

## Variants

- **No `[WSM-PROF]` lines at all** → `ProfilerDump` is off, or
  `WorldSphereMod3D` failed to load (check the same Player.log for
  `IncompatibleHardwareException` or Harmony conflicts).
- **`dt` is fine but the game feels stuttery** → GC spikes, not steady-
  state cost. Enable Unity's built-in profiler (deep profiling mode), look
  for allocation spikes in the WorldSphere modules.
- **Phase 5 (`HighShadows`) is the hot phase** → expected on lower-tier
  GPUs; drop shadow cascades from 4 to 2 in `ShadowCascadeConfig`, or turn
  off `HighShadows` (the mod still uses URP's default-quality shadows from
  the lit shader).
- **Phase 6 (`RigDriver`) is the hot phase** → ensure the compute-skin
  path is active (not the CPU fallback). Check the log for
  `[WSM-RIG] compute=available` vs `[WSM-RIG] compute=missing fallback=cpu`.

## Where the profiler lives

- Hook into the profiler: `WorldSphereMod/Code/Perf/ProfilerDump.cs`
  (see [`PLAN.md`](/PLAN#phase-10--performance-fallbacks-polish-3-5-days)
  and [phase 10 architecture](/phase10-architecture)).
- Per-system samples are recorded by each phase's driver (e.g.
  `Voxel/VoxelFrameDriver`, `Rig/RigDriver`). They push to a global
  `ProfilerDump` static which flushes once per second.
- Bucket names map 1:1 to the directory names under
  `WorldSphereMod/Code/` (`Voxel`, `ProcGen`, `Foliage`, `Water`,
  `Lighting`, `Rig`, `Worldspace`, `Fx`, `LOD`). `misc` is everything
  else (Harmony patches, transpilers, coordinate math).
