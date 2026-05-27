# Issue Triage -- BRUTALLY HONEST

**Date:** 2026-05-26 (shader rebake status refresh)
**Assessed by:** Full Player.log analysis + screenshot audit + source doc review
**Player.log:** `$USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log`

## Executive Summary

**The mod is fundamentally broken at runtime.** The Harmony patches that
are supposed to make voxel actors/buildings/everything visible are NOT
actually being applied (`0/4 Harmony types affected`). The screenshot
"proof" artifacts are all captures of the Steam store page for
"Diplomacy is Not an Option" -- a completely different game -- NOT
WorldBox. There are 10 crash reports, 61 NullReferenceExceptions, and
78 "isReadable is false" mesh access errors in the current Player.log.
The automation tooling (PlayCUA, do-all.ps1) reports "13/13 pass" but
**historical** screenshot artifacts captured the foreground window (Steam
store page for "Diplomacy is Not an Option"), not WorldBox. **Screenshot
window-targeting is now fixed** in `Tools/wsm3d-playcua/main.py`
(`Win32Capture` enumerates WorldBox process/window, sets
`capture_target: worldbox_window` in step details; desktop fallback only
when no hwnd). Re-run PlayCUA + `sync-playcua-screenshots.ps1` to refresh
`docs/screenshots/`. No phase has been visually verified by a human in the
actual game with post-fix captures.

**Shader bundle:** rebaked 2026-05-26 (10 shaders in manifest). Runtime
intentionally loads **3** via `SafeShaders`; the other 7 remain gated until
in-game proof — see HANDOFF § SafeShaders human gate.

---

## Issue-by-Issue Triage

| # | Issue | Status | Evidence | What Would Actually Prove It Fixed |
|---|-------|--------|----------|-------------------------------------|
| 1 | **F10 "Loaded packs" panel is EMPTY** | **UNFIXED** | The screenshots in `artifacts/` and `docs/screenshots/` ALL show "Diplomacy is Not an Option" Steam store page, not WorldBox. No screenshot exists showing the F10 panel populated with mod packs. The NML log shows "Found 7 NML mods" but there is zero evidence the F10 UI displays them. | A screenshot taken FROM WorldBox showing F10 open with loaded packs listed. |
| 2 | **"1 error and 8/9 packs active" at top of F10** | **UNFIXED** | No F10 screenshot from WorldBox exists. The "1 error" likely relates to the `KeyNotFoundException: 'WorldBox (WorldLayer)'` that fires during world creation (line 1214, 1246 of Player.log). This exception is STILL happening in the current log. | Screenshot of F10 showing 0 errors and all packs active, OR a log showing no KeyNotFoundException during world creation. |
| 3 | **"Reload packs" button does nothing** | **UNFIXED** | No evidence in Player.log or screenshots that this was ever tested. No `[WSM3D]` log lines related to pack reload. F10 panel itself is broken (issue #1). | Click "Reload packs" in-game, see packs reload, capture screenshot + log showing reload event. |
| 4 | **X button in F10 does nothing** | **UNFIXED** | Zero evidence. No screenshot of F10 exists from WorldBox. No code search reveals F10-related handling in the WSM3D codebase -- this is likely a NeoModLoader UI issue, but it has never been investigated. | Click X on F10 panel, see it close. |
| 5 | **X button in F9 does nothing** | **UNFIXED** | Zero evidence. No code search reveals F9-related handling in WSM3D. Same as #4 -- likely NML-level UI. | Click X on F9 panel, see it close. |
| 6 | **F9 debug output is EMPTY** | **UNFIXED** | No screenshot, no log evidence, no investigation. | Screenshot of F9 showing debug output populated. |
| 7 | **Native mods button opens F10 instead of native UI** | **UNFIXED** | No investigation evidence found. This is likely a NeoModLoader routing issue. | Click native mods button, see native options UI (not F10). |
| 8 | **NO VISIBLE CHANGES IN GAME despite phases loaded** | **UNFIXED -- ROOT CAUSE IDENTIFIED** | **This is the #1 smoking gun.** Player.log line 1145: `PhasePatchManager: VoxelEntities -> True (0/4 Harmony types affected, 0 non-Harmony types skipped)`. The PhasePatchManager found 4 candidate patch types (ActorVoxelEmit, BuildingVoxelEmit, DropVoxelEmit, ProjectileVoxelEmit) but ZERO of them were actually applied as Harmony patches. The patches are not hooking into WorldBox's methods. Additionally: (a) `DrawMeshInstanced blocked: Standard shader does not support GPU instancing` (line 640); (b) Fallback to per-instance `Graphics.DrawMesh` then throws `ArgumentNullException` (line 1457); (c) Mesh access throws "isReadable is false" 78 times; (d) `NullReferenceException` in `TileMapToSphere.PixelArray.Set` fires every frame (61 occurrences). The telemetry at line 1455 says `drawCalls=1 instances=2 cacheSize=0` -- only the SanityTestCube is rendering, not actual game entities. | Voxel actors visible in-game. Player.log showing `N/4 Harmony types affected` where N > 0. Telemetry showing `instances > 100` and `cacheSize > 0`. |
| 9 | **2.5D flat slab voxels (cardboard cutouts)** | **CODE CHANGED UNVERIFIED** | The VoxelScaleMultiplier=8.0 fix was applied per MEMORY.md (`project_wsm3d_phase1_visible.md`). But since Harmony patches are NOT applying (0/4 affected), the voxel render path never executes on real actors. The "fix" exists in code but cannot be observed because the pipeline is broken upstream of it. | Voxel actors rendering with visible depth, not flat slabs. Requires issue #8 fixed first. |
| 10 | **Black/invisible actors and assets** | **UNFIXED (shaders partially unblocked)** | **Shader rebake (2026-05-26):** `wsm3d-shaders.manifest` lists **10** BRP shaders + SVC. Runtime still loads **3** only via `Core.Sphere.SafeShaders` (`OpaqueVertexColor`, `GerstnerWater`, `ColorGradingLUT`) — the other 7 remain gated (historical ManagedStream / crash-reporter risk; see HANDOFF § SafeShaders human gate). Prior log (2026-05-25) showed 6/9 with empty `.name`; **rebake may fix compile** but **in-game `LoadedShaders[count=…]` not yet re-verified** on this branch. Separate blockers remain: Harmony 0/4, `DrawMeshInstanced blocked`, mesh `isReadable=false`. | Actors/buildings visible. Player.log: `LoadedShaders[count=3]` with three successful bundle loads; then per-shader expansion tests if adding PostFX/sky/LOD shaders to `SafeShaders`. |
| 11 | **Billboard slopes instead of smooth terrain** | **CODE CHANGED UNVERIFIED** | `MountainSlopeSmoothing` code exists (Phase 5) but is default OFF in settings. Even if turned ON, the underlying Harmony patch application mechanism is suspect given VoxelEntities patches fail to apply. Settings sanity log (line 646) shows `MountainSlopeSmoothing loaded=False`. | Enable MountainSlopeSmoothing in-game, see smooth terrain transitions instead of billboard cliffs. Screenshot proof. |
| 12 | **Black water layer** | **CODE CHANGED UNVERIFIED** | `GerstnerWater` shader IS in the bundle and loads successfully. But `MeshWater` is default OFF in settings (line 654: `loaded=True` suggesting user enabled it). The `KeyNotFoundException: 'WorldBox (WorldLayer)'` at line 1214 fires during world creation in `TileMapToSphere.AddLayers.TextureNew3D` -- this is a terrain layer dictionary miss that could cause black tiles/water. No screenshot from WorldBox exists to verify. | Enable MeshWater, see blue water surface with Gerstner waves. No black tiles. |
| 13 | **PostFX causes black camera** | **CODE CHANGED UNVERIFIED** | `PostFX loaded=False` (line 652). `ColorGradingLUT` is in `SafeShaders` and should load. `BrpBloom` / `BrpACES` / SSAO / SSGI are **in the rebaked bundle** but **not** in `SafeShaders` — PostStack falls back via `Shader.Find` / skips passes. Rebake (2026-05-26) may have fixed empty-name bakes; **human must enable PostFX after confirming `LoadedShaders[count=3]`** and optionally trial one gated shader at a time. | Enable PostFX, no black screen; log shows LUT + (after gate) bloom/ACES materials resolved. |
| 14 | **Magenta/neon actors** | **CODE CHANGED UNVERIFIED** | Magenta = missing shader in Unity. The log shows 6/9 bundle shaders are corrupted. When `OpaqueVertexColor` resolves (it does for the 3 working shaders), voxels should not be magenta. But since actors don't render at all (issue #8), this is moot. If issue #8 were fixed, the 3 working shaders might prevent magenta for basic voxels, but phases using `ProceduralSky`, `Impostor`, `ScreenSpaceAO`, etc. would still show magenta. | No magenta objects visible in-game. All shaders resolving to non-error materials. |
| 15 | **Butterfly rig on all actors** | **CODE CHANGED UNVERIFIED** | `SkeletalAnimation` is default OFF (line 647: `loaded=True` suggesting user enabled it). The rig code (`HumanoidRig.Bones`) exists but has never been visually validated. Since base voxel rendering is broken (issue #8), skeletal animation cannot be verified. | Enable SkeletalAnimation, see humanoid actors with correct limb movement (not butterfly/splayed). |
| 16 | **Phase toggles crash with Harmony errors** | **PARTIALLY FIXED / STILL BROKEN** | `PhasePatchManager` now exists and scans for phase patches. The ADR-0007 conditional patch dispatch is "Accepted." But the log proves it's not working: `VoxelEntities -> True (0/4 Harmony types affected)` means the toggle runs but patches don't actually apply. No Harmony errors logged (no stack traces from HarmonyX), but 0/4 applied means silent failure. The `AccessTools.Field: Could not find field for type System.Array and name Empty` warning at line 562 might be related. | Toggle a phase, see N/N patches applied (not 0/N), no Harmony errors in log, visual change in game. |
| 17 | **Game freezes at "Loading finished"** | **PARTIALLY FIXED** | The log shows `151: Loading finished = 28.3191` (28 seconds!) which is extremely slow but does eventually complete. After loading, the game enters a crash loop: `KeyNotFoundException: 'Pixel Flash Effect (PixelFlashEffects)'` fires every frame, plus `NullReferenceException` in `TileMapToSphere.PixelArray.Set` fires every frame. The game doesn't hard-freeze but is being hammered with per-frame exceptions that degrade performance. 10 "Uploading Crash Report" events in one session. | Game loads in < 5 seconds. Zero per-frame exceptions in Player.log after loading. No crash reports uploaded. |
| 18 | **Settings staleness (JSON overrides code defaults)** | **CODE CHANGED UNVERIFIED** | Settings sanity log lines DO exist (lines 645-656), which is good. They show loaded vs default values. Several settings show `loaded=True default=False` (WorldspaceUI, SkeletalAnimation, HdrSkybox, HighShadows, DayNightCycle, ParticleEffects, CrossedQuadFoliage, MeshWater, ProceduralBuildings), meaning the user's JSON has these enabled despite code defaults being OFF. The sanity lines help diagnose this, but there's no mechanism to WARN the user or auto-correct. | Settings sanity lines present (YES, they are). But: a user-facing warning when loaded != default would actually close this issue. Currently it's diagnostic-only, invisible unless you read Player.log. |

---

## Systemic Problems Identified

### 1. ~~The screenshot automation is capturing the WRONG WINDOW~~ **FIXED (2026-05-26)**

**Status: FIXED in tooling.** `Win32Capture` in `Tools/wsm3d-playcua/main.py`
now targets the WorldBox game window by process name (`worldbox.exe`) and
window title, captures the client area via `_capture_window_client`, and
records `capture_target: worldbox_window` in PlayCUA step details (falls
back to `desktop` only when no matching hwnd). A prior bug used bare
`ctypes.wintypes` references that failed on some Python builds; fixed by
importing `from ctypes import wintypes as _wintypes` on Win32 and using
that alias in `PROCESSENTRY32W` / `EnumWindows` callbacks.

**Historical artifacts remain invalid:** every PNG captured *before* this
fix in `artifacts/` and `docs/screenshots/` still shows "Diplomacy is Not
an Option" Steam store page. PlayCUA may still report "13/13 pass" with
vision backend off without checking screenshot content. Re-run
`pwsh Tools/do-all.ps1` (or PlayCUA `run-all`) + `sync-playcua-screenshots.ps1`
and confirm step details show `capture_target: worldbox_window` before
trusting any visual verification claims in HANDOFF.md or phase-visual-audit.md.

### 2. Harmony patches are silently failing to apply

`PhasePatchManager: VoxelEntities -> True (0/4 Harmony types affected)`
This is the single most important line in the entire log. The system
FOUND the 4 patch types, DECIDED to enable them (True), but ZERO were
actually applied. The root cause is not investigated. Possible causes:
- The `[HarmonyPatch]` attributes reference methods that don't exist in
  this version of WorldBox
- The Harmony instance used by PhasePatchManager is different from the
  one that `Core.Patch()` uses
- The patch types have already been registered by the initial
  `PatchAll()` call and the conditional dispatch is a second redundant
  scan that can't re-patch

### 3. Per-frame exception storm

After world generation, TWO different exceptions fire EVERY FRAME:
- `KeyNotFoundException: 'Pixel Flash Effect (PixelFlashEffects)'` in
  TileMapToSphere
- `NullReferenceException` in `TileMapToSphere.PixelArray.Set`

These are uploaded as crash reports (10 total in one session). Each
frame's exception adds overhead and prevents normal rendering.

### 4. Mesh isReadable=false errors (78 occurrences)

Every voxelized sprite triggers "Not allowed to access vertices/colors/
triangles on mesh 'voxel:main_*' (isReadable is false)". This means the
mesh data is being locked after creation, and subsequent reads/validation
fail. The voxels ARE being built (the BuildGreedy logs show vertex/tri
counts) but the resulting meshes cannot be read back.

### 5. The documentation describes a working system that doesn't work

HANDOFF.md says all 10 phases are "shipped" with green checkmarks.
The phase-visual-audit.md describes shader chains and visual output.
The maturity-audit.md rates UX at 3/5. The test suite reports
"486 pass / 3 skip." But the actual Player.log tells a different
story: the mod loads, patches fail to apply, shaders are corrupted,
meshes can't be read, and per-frame exceptions fire continuously.

---

## What Needs to Happen (Priority Order)

1. **Fix Harmony patch application** -- The 0/4 issue is the blocker
   for everything. Until ActorVoxelEmit, BuildingVoxelEmit, etc.
   actually hook into WorldBox's render passes, zero 3D content will
   appear.

2. **Fix the per-frame exceptions** -- KeyNotFoundException for
   'WorldBox (WorldLayer)' and 'Pixel Flash Effect (PixelFlashEffects)'
   plus the PixelArray NullRef. These fire every frame and generate
   crash reports.

3. **Verify rebaked shader bundle in-game** — Rebake **done** (2026-05-26; `pwsh Tools/bake-shaders.ps1`, 10 shaders in `wsm3d-shaders.manifest`). Runtime still loads **3** via `SafeShaders`. Human must confirm `LoadedShaders[count=3]` in Player.log after `install.ps1`, then expand `SafeShaders` one shader at a time (HANDOFF § SafeShaders human gate). Headless rebake is **not** exposed as `wsm3d.ps1` subcommand.

4. ~~**Fix the screenshot tooling**~~ **DONE (2026-05-26)** — `Win32Capture`
   targets WorldBox hwnd (`capture_target: worldbox_window`); ctypes.wintypes
   import fixed. **Next:** re-run PlayCUA + screenshot sync and replace stale
   `docs/screenshots/` PNGs; then re-audit visual claims.

5. **Fix mesh isReadable** -- 78 "isReadable is false" errors. The
   voxel meshes need `mesh.MarkDynamic()` or the read-back path needs
   to be removed.

6. **Get a HUMAN to look at the game** -- No amount of automation
   substitutes for someone actually looking at WorldBox with the mod
   installed and saying "I see voxel actors." That has never happened
   with the current build.

---

## Confidence Summary

| Category | Count |
|----------|-------|
| PROVEN FIXED | 0 |
| TOOLING FIXED (runtime unverified) | 1 (screenshot window-targeting) |
| CODE CHANGED UNVERIFIED | 8 (#9, #10, #11, #12, #13, #14, #15, #18) |
| PARTIALLY FIXED | 2 (#16, #17) |
| UNFIXED | 8 (#1, #2, #3, #4, #5, #6, #7, #8) |
| **Total issues** | **18** |

Zero in-game issues have been proven fixed from the user's perspective.
Screenshot window-targeting is fixed in PlayCUA but stale PNGs must be
refreshed before visual evidence counts.
