# Phase 6 in-game smoke test — checklist

What to verify when you toggle `SkeletalAnimation = true` for the first time.

**Agentic automation:** [`Tools/wsm3d-playcua/sample-scenarios/phase-6-skeletal-animation.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-6-skeletal-animation.yaml) drives bridge `toggle_flag`, telemetry, and vision screenshot steps. Full programmatic + agentic gate order: [`docs/live-verification.md`](live-verification.md).

## Setup

```powershell
./Tools/install.ps1
```

That builds the mod (sanity check) and copies sources + assets to
`<WorldBox>/Mods/WorldSphereMod3D/`. Default WorldBox path is the standard Steam install.

Launch WorldBox. NeoModLoader compiles `Code/*.cs` at startup. If you see a red mod icon, the compute-shader/instancing hardware gate failed — Phase 10 will add a billboard fallback for that case; nothing to do here.

Load or generate a world with **humanoid actors** (village with citizens/soldiers). Save slot 2 is the baseline used by the PlayCUA scenario.

## Regression checks (with `SkeletalAnimation = false`)

Keep `VoxelEntities` **ON** and Phases 2–5 as needed so voxel actors exist to animate. Only Phase 6 skeletal path is under test here.

Open the map. Confirm:

- Humanoid actors render as **static voxel meshes** (sparse tri-dot or rigid blocks), not limbed walk cycles.
- Settings tab → WorldSphere → **Skeletal Animation** toggle is present and OFF (or flip OFF if your save inherited default-on).
- Non-humanoid creatures still draw (quadruped may stay static until rig mapping lands).

If any of those fail, Phase 0–5 plumbing has regressed. Don't proceed.

## Enable skeletal animation

1. Settings → WorldSphere → toggle **Skeletal Animation** ON.
2. Pan the camera through the village: watch idle and walk cycles on humanoids; top-down and low-angle passes.
3. Optional bridge run (game + listener on `127.0.0.1:8766`):

   ```powershell
   python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-6-skeletal-animation.yaml
   ```

   See [`docs/live-verification.md`](live-verification.md) for OmniRoute vision env and install deps.

### Verify

| Check | Expected | Failure mode if broken |
|---|---|---|
| Humanoids show limbed silhouettes | Arms/legs distinct during walk/idle | Tri-dot static blob → `SkeletalAnimation` didn't apply, or `RigDriver` postfix not running |
| Animation advances over time | Pose changes frame-to-frame on moving units | Frozen T-pose → `AnimationFrameData` not read, or CPU fallback stuck |
| GPU skinning path when available | No hitch after first rig bind; `drawCalls > 0` | Massive stall → `VoxelSkin.compute` missing; falls back to CPU (acceptable for gate) |
| Non-humanoid fallback acceptable | Quadrupeds may stay static voxel mesh | N/A unless you expect quadruped rig — see `docs/phase6-architecture.md` |
| Telemetry shows render work | Bridge `drawCalls > 0`, `instances > 10` after toggle | `drawCalls=0` with actors visible → skinned mesh not in batcher flush |
| No shader error banner | Clean UI, no red compile overlay | Console: `[WorldSphereMod3D]` + compute shader load errors |

## Gpu procedural skinning (optional)

`GpuProceduralSkinning` (when exposed) gates the compute path. With skeletal ON, compare CPU vs GPU fallback via console `[RigDriver]` logs — not required to clear Phase 6.

## Multi-world session check (optional)

`RigCache` LRU may retain stale meshes across world reload without restart. Workaround: restart WorldBox between worlds during testing.

## Capture screenshots

Drop comparison shots into `docs/screenshots/`:

- `phase-6-before.png` — `SkeletalAnimation = false`, same scene.
- `phase-6-after.png` — `SkeletalAnimation = true`, same scene + camera angle.
- `phase-6-actors-rig.png` — humanoid walk/idle closeup (matches PlayCUA artifact `phase-6-skeletal-animation/actors-rig.png`).

Link them in the Phase 6 PR body when marking ready for review. Diff optional against `docs/journeys/phase-previews/` per [`docs/live-verification.md`](live-verification.md#ssim-optional-gate).

## What's expected to look bad

- **Non-humanoid rigs.** Quadrupeds and odd sprites may not map to `HumanoidRig` — see `docs/phase6-architecture.md`.
- **First-bind hitch.** First actor per sprite atlas can spike frame time while `RigCache` builds skinned mesh.
- **Static units.** Idle actors with no locomotion may look stiff; gate is silhouette + bind, not mocap quality.

## If something is broken

- Check the WorldBox console (default toggle: backtick) for `[WorldSphereMod3D]` / `[RigDriver]` errors.
- Check `<WorldBox>/output_log.txt` or NeoModLoader's compile-error log.
- Toggle **Skeletal Animation** OFF — static voxel meshes return without restart.
- Re-run the PlayCUA scenario or `pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-6-skeletal` (mock) for manifest drift.
- Open an issue on the PR with console excerpt + which check from this list failed.
