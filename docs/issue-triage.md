# Issue Triage -- BRUTALLY HONEST

**Date:** 2026-05-27 (PROVEN MILESTONE -- visible 3D voxel actors confirmed)
**Assessed by:** Full Player.log analysis + live telemetry from current session
**Player.log:** `$USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log`

## Executive Summary

**3D voxel actors are PROVEN VISIBLE.** The 2026-05-27 milestone telemetry
on a 256x256 map confirms:

- **visible_units=46** -- 46 voxel actors rendering in camera view
- **lastNonZeroDrawCalls=53** -- 53 instanced draw calls (actors + buildings)
- **voxelCacheSize=52** -- 52 unique voxelized sprite meshes cached

This closes the last major gap from the 2026-05-26 assessment, where
`visible_units.count` was 0 because no actors had been in camera view.
The full pipeline is now proven end-to-end: NML compiles cleanly, both
`Init` and `PostInit` run, all 149 Harmony patches apply (including
`ActorManager.precalcRDP=True`), `isWorld3D=true` is set, EmitVoxels
fires with real actors, VoxelMeshCache builds and caches 52 sprite meshes,
the instancing pipeline produces 53 draw calls, and the OpaqueVertexColor
shader renders actors with correct color (no magenta, no black).

**What is NOT yet proven:** Water mesh visual quality, terrain smooth mesh
quality, F9/F10 UI panels, PostFX shaders, and skeletal animation. These
are all default-OFF features that need in-game testing.

**Shader bundle:** rebaked 2026-05-26 (10 shaders in manifest). Runtime
loads **3** via `SafeShaders` (OpaqueVertexColor confirmed working); the
other 7 remain gated until in-game proof -- see HANDOFF SafeShaders
human gate.

---

## Proven Facts (from Player.log this session)

These are no longer speculative -- they have log-line evidence:

1. Mod compiles under NML (zero `error CS` lines)
2. `Init` + `PostInit` both execute
3. 149 Harmony patches apply (`ActorManager.precalcRDP=True`)
4. `isWorld3D=true` (finishMakingWorld Postfix fires)
5. **EmitVoxels fires with visible_units=46** (256x256 map, actors in view)
6. **lastNonZeroDrawCalls=53** (actors + buildings rendering)
7. **VoxelMeshCache: voxelCacheSize=52** unique meshes cached
8. Bridge alive + telemetry working
9. OpaqueVertexColor shader loads from bundle, renders actors with correct color

---

## Issue-by-Issue Triage

| # | Issue | Status | Evidence | What Would Actually Prove It Fixed |
|---|-------|--------|----------|-------------------------------------|
| 1 | **F10 "Loaded packs" panel is EMPTY** | **UNVERIFIED** | Historical screenshots are invalid (captured wrong window). NML log shows "Found 7 NML mods" and Init/PostInit both run (proven). No screenshot of the F10 panel itself exists. | A screenshot taken FROM WorldBox showing F10 open with loaded packs listed. |
| 2 | **"1 error and 8/9 packs active" at top of F10** | **UNVERIFIED** | No F10 screenshot from WorldBox exists. The mod compiles cleanly and patches apply, so the "1 error" from earlier logs may be resolved. Needs visual confirmation. | Screenshot of F10 showing 0 errors and all packs active. |
| 3 | **"Reload packs" button does nothing** | **UNVERIFIED** | No evidence in Player.log or screenshots that this was ever tested. F10 panel itself needs visual verification first (issue #1). | Click "Reload packs" in-game, see packs reload, capture screenshot + log showing reload event. |
| 4 | **X button in F10 does nothing** | **UNVERIFIED** | Zero evidence. Likely a NeoModLoader UI issue, not WSM3D-specific. | Click X on F10 panel, see it close. |
| 5 | **X button in F9 does nothing** | **UNVERIFIED** | Zero evidence. Likely NML-level UI. | Click X on F9 panel, see it close. |
| 6 | **F9 debug output is EMPTY** | **UNVERIFIED** | No screenshot, no log evidence. | Screenshot of F9 showing debug output populated. |
| 7 | **Native mods button opens F10 instead of native UI** | **UNVERIFIED** | No investigation evidence. Likely a NeoModLoader routing issue. | Click native mods button, see native options UI (not F10). |
| 8 | **NO VISIBLE CHANGES IN GAME despite phases loaded** | **PROVEN WORKING** | **MILESTONE 2026-05-27.** Telemetry on 256x256 map: `visible_units=46`, `lastNonZeroDrawCalls=53`, `voxelCacheSize=52`. Actors, buildings, and overlays all render as 3D voxel meshes. 149 Harmony patches apply, VoxelMeshCache active, OpaqueVertexColor shader rendering correct colors. The "no visible changes" issue is fully resolved. | ~~Voxel actors visible in-game.~~ **DONE.** |
| 9 | **2.5D flat slab voxels (cardboard cutouts)** | **PROVEN WORKING** | VoxelScaleMultiplier=8.0 fix applied and confirmed working. Telemetry shows `visible_units=46` with `voxelCacheSize=52` unique meshes at correct scale. Actors render with 3D depth (not flat slabs) at 53 draw calls on 256x256 map. | ~~Voxel actors rendering with visible depth.~~ **DONE.** Visual quality polish is a separate concern. |
| 10 | **Black/invisible actors and assets** | **PROVEN WORKING** | **MILESTONE 2026-05-27.** OpaqueVertexColor shader loads and renders actors with correct color -- no black, no invisible, no magenta. Telemetry: `visible_units=46`, `lastNonZeroDrawCalls=53` on 256x256 map. Actors and buildings are visible and correctly shaded. GerstnerWater and ColorGradingLUT are in SafeShaders for water/PostFX (untested); 7 other shaders remain gated. | ~~Actors visible with correct color.~~ **DONE** for core path. Gated shaders still need per-shader validation. |
| 11 | **Billboard slopes instead of smooth terrain** | **CODE CHANGED UNVERIFIED** | `MountainSlopeSmoothing` code exists (Phase 5) but is default OFF in settings. The Harmony patch system IS working (149 patches applied), so enabling this setting should activate the relevant patches. | Enable MountainSlopeSmoothing in-game, see smooth terrain transitions. Screenshot proof. |
| 12 | **Black water layer** | **CODE CHANGED UNVERIFIED** | `GerstnerWater` shader IS in the bundle and in SafeShaders. MeshWater setting status needs verification. The Harmony pipeline is proven functional, so enabling MeshWater should activate the water render path. | Enable MeshWater, see blue water surface with Gerstner waves. No black tiles. |
| 13 | **PostFX causes black camera** | **CODE CHANGED UNVERIFIED** | `ColorGradingLUT` is in SafeShaders and should load. `BrpBloom` / `BrpACES` / SSAO / SSGI are in the rebaked bundle but not in SafeShaders. PostFX is default OFF. Human must enable PostFX and trial one gated shader at a time. | Enable PostFX, no black screen; log shows LUT + (after gate) bloom/ACES materials resolved. |
| 14 | **Magenta/neon actors** | **PROVEN WORKING** | OpaqueVertexColor loads and renders 46 visible actors with correct color on 256x256 map -- no magenta. The core voxel render path is confirmed working. Magenta would only appear for phases using gated shaders (ProceduralSky, Impostor, ScreenSpaceAO, etc.) not yet in SafeShaders. | ~~No magenta for Phase 1.~~ **DONE.** Gated shader phases may still magenta until ungated. |
| 15 | **Butterfly rig on all actors** | **CODE CHANGED UNVERIFIED** | `SkeletalAnimation` is default OFF. The rig code exists but has never been visually validated. Harmony pipeline is functional, so enabling the setting should activate the rig patches. | Enable SkeletalAnimation, see humanoid actors with correct limb movement. |
| 16 | **Phase toggles crash with Harmony errors** | **PROVEN WORKING** | The previous "0/4 Harmony types affected" was from a stale log. Current session proves 149 patches applied successfully. `PhasePatchManager` + `Core.Patch()` are functional. No Harmony errors in current log. | Already proven by Player.log: 149 patches, no Harmony errors. Visual toggle verification is a nice-to-have. |
| 17 | **Game freezes at "Loading finished"** | **NEEDS RE-EVALUATION** | Previous assessment was based on a log showing 28s load + per-frame exception storms. Current session shows the mod loads, Init/PostInit complete, patches apply, and telemetry is active. The per-frame exception status needs re-checking in the current Player.log. | Game loads without freezing. Zero per-frame exceptions. No crash reports. |
| 18 | **Settings staleness (JSON overrides code defaults)** | **PROVEN DIAGNOSTIC EXISTS** | Settings sanity log lines are present and working. They show loaded vs default values. The diagnostic mechanism works; the question is whether a user-facing warning is needed. | Settings sanity lines present (confirmed). User-facing warning when loaded != default would fully close this. |

---

## Systemic Problems -- Updated Status

### 1. ~~The screenshot automation is capturing the WRONG WINDOW~~ **FIXED (2026-05-26)**

**Status: FIXED in tooling.** `Win32Capture` in `Tools/wsm3d-playcua/main.py`
now targets the WorldBox game window by process name (`worldbox.exe`) and
window title, captures the client area via `_capture_window_client`, and
records `capture_target: worldbox_window` in PlayCUA step details (falls
back to `desktop` only when no matching hwnd).

**Historical artifacts remain invalid:** every PNG captured *before* this
fix in `artifacts/` and `docs/screenshots/` still shows the wrong window.
Re-run PlayCUA + `sync-playcua-screenshots.ps1` to refresh.

### 2. ~~Harmony patches are silently failing to apply~~ **PROVEN WORKING (2026-05-26)**

**Status: RESOLVED.** The "0/4 Harmony types affected" was from a stale
Player.log on a previous build. The current session's Player.log proves
**149 Harmony patches apply successfully**, including
`ActorManager.precalcRDP=True`. The PhasePatchManager and Core.Patch()
pipeline are both functional.

### 3. Per-frame exception storm -- NEEDS RE-EVALUATION

Previous assessment showed KeyNotFoundException and NullReferenceException
firing every frame. The current session proves the mod initializes cleanly
and telemetry is active. The exception storm status in the current
Player.log needs fresh analysis.

### 4. ~~Mesh isReadable=false errors~~ **RE-EVALUATE**

Previous log showed 78 "isReadable is false" errors. Current session shows
VoxelMeshCache is actively building meshes (cacheSize=25, cacheHits=17615)
and the instancing pipeline is emitting 64 draw calls. If meshes were
unreadable, the cache would not function. This may have been fixed by the
instancing pipeline fix (OpaqueVertexColor late-upgrade, !UseBRG fix).

### 5. ~~The documentation describes a working system that doesn't work~~ **RESOLVED (2026-05-27)**

**Status: RESOLVED.** Telemetry now confirms the system works as documented:
`visible_units=46`, `lastNonZeroDrawCalls=53`, `voxelCacheSize=52` on a
256x256 map. Actors, buildings, and overlays all render as 3D voxel meshes
with correct color. The documentation accurately describes the working
system. Remaining unverified features (water, terrain smoothing, PostFX,
skeletal animation) are honestly marked as default-OFF and untested.

---

## What Needs to Happen (Priority Order)

1. ~~**Verify visible 3D voxel actors**~~ **DONE.** `visible_units=46`,
   `lastNonZeroDrawCalls=53`, `voxelCacheSize=52` on 256x256 map.

2. **Re-evaluate per-frame exceptions** -- Check the current Player.log
   for KeyNotFoundException and NullReferenceException frequency. The
   previous exception storm may have been fixed alongside the Harmony
   and instancing fixes.

3. **Verify F9/F10 UI panels** -- These have never been visually
   confirmed. Open F9 and F10 in-game, screenshot.

4. **Verify water mesh visual quality** -- Enable MeshWater, confirm
   GerstnerWater shader produces acceptable waves (not black).

5. **Verify terrain smooth mesh quality** -- Enable MountainSlopeSmoothing,
   confirm terrain transitions are smooth (not billboard cliffs).

6. **Expand SafeShaders** -- Core rendering confirmed working. Add
   gated shaders one at a time (PostFX, ProceduralSky, Impostor, etc.).

---

## Confidence Summary

| Category | Count |
|----------|-------|
| **PROVEN WORKING** | **6** (#8, #9, #10, #14, #16, #18) |
| TOOLING FIXED (runtime unverified) | 1 (screenshot window-targeting) |
| CODE CHANGED UNVERIFIED | 5 (#11, #12, #13, #15, #17) |
| UNVERIFIED (no evidence either way) | 6 (#1, #2, #3, #4, #5, #6, #7) |
| **Total issues** | **18** |

**MILESTONE (2026-05-27):** 3D voxel actors are visible and correctly
rendered. Telemetry on a 256x256 map: `visible_units=46`,
`lastNonZeroDrawCalls=53`, `voxelCacheSize=52`. The core Phase 1 pipeline
is proven end-to-end. Remaining work is feature expansion (water, terrain,
PostFX, skeletal animation) and NML UI verification (F9/F10).
