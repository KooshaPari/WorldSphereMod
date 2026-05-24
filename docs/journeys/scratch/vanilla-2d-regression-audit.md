# Vanilla 2D Regression Audit

**Verdict:** loading WSM3D with every phase flag off does **not** guarantee exact vanilla 2D rendering. The phase system itself is clean, but a few always-on hooks still replace shared 2D rendering helpers.

## 1) Patch classification

Phase-gated patches are the safe part. `Core.Patch()` only applies classes marked with `[Phase]` when the matching `SavedSettings` flag is true (`Core.cs:122-147`), and the `PhaseAttribute` contract exists specifically to skip disabled phases (`PhaseAttribute.cs:6-8`). The phase toggles also unpatch when turned off (`PhasePatchManager.cs:15-49`).

The always-on patch set is bigger than the phase set. Most of it is guarded at runtime by `Core.IsWorld3D` and falls through to vanilla in 2D, especially the camera hooks in `3DCamera.cs` (`3DCamera.cs:17-24`, `3DCamera.cs:30-38`, `3DCamera.cs:54-60`, `3DCamera.cs:73-79`, `3DCamera.cs:125-145`, `3DCamera.cs:207-216`, `3DCamera.cs:229-262`, `3DCamera.cs:268-275`, `3DCamera.cs:377-385`, `3DCamera.cs:424-432`, `3DCamera.cs:448-475`) and the effect/actor/world-loop hooks in `General.cs` and `Effects.cs` (`General.cs:49-76`, `General.cs:206-247`, `General.cs:250-287`, `Effects.cs:186-325`).

The 2D-risking always-on hooks are the ones that do **not** check `IsWorld3D` before overriding shared rendering helpers:
- `MapLayer.clear` and `MapLayer.createTextureNew` are fully replaced in `TileMapToSphere.cs` (`TileMapToSphere.cs:404-445`).
- `QuantumSpriteLibrary.showLightAt` is always intercepted (`QuantumSprites.cs:221-229`).
- `GroupSpriteObject.setScale(float)` is always intercepted (`QuantumSprites.cs:242-253`).
- `PreviewHelper.convertMapToTexture` / `getCurrentWorldPreview` always run `PreviewPatch` (`General.cs:289-300`).

## 2) Does “all flags off” restore exact vanilla rendering?

No. The phase-gated render modes can be removed, but the unconditional hooks above still modify core rendering plumbing. In particular, the `MapLayer` detours are not wrapped in `IsWorld3D`, so vanilla 2D tilemap texture creation and clear behavior are not byte-for-byte upstream anymore (`TileMapToSphere.cs:404-445`). `PreviewPatch` also rewrites preview texture generation even when no 3D phase is active (`General.cs:289-300`).

## 3) Persistent state / leak candidates

The 2D exit path only calls `Sphere.Finish()` and swaps cameras (`Core.cs:264-275`). `CameraManager.MakeCamera2D()` disables the 3D camera and re-enables the original one, but it does not destroy the 3D camera object (`3DCamera.cs:87-98`). That means camera state is retained in-scene across enable/disable cycles.

Lighting has a similar shape. `SunDriver.Init()` creates `LightingRoot` and a directional sun, and `Teardown()` exists to destroy them and reset shadow settings (`SunDriver.cs:18-53`), but the visible 2D transition path does not call it (`Core.cs:264-275`). That makes the sun/root a likely cross-mode survivor if 3D was ever enabled.

The good news is that the heavier phase subsystems do clean up on world unload. `WorldUnloadPatch` drains voxel, foliage, impostor, rig, batcher, and UI caches on `Sphere.Finish` (`WorldUnloadPatch.cs:12-30`), and `WorldUIRenderer.OnWorldUnload()` destroys its rigs, popup root, and selection state (`WorldUIRenderer.cs:39-63`). `PostFxController.Destroy()` also reverses `renderPostProcessing` and destroys its volume/profile when toggled off (`PostFxController.cs:257-289`).

One remaining static-bookkeeping concern is tile-map state: `Core.Sphere.CachedColors` is a static dictionary (`Core.cs:329-330`), and the tile queues in `TileMapToSphere` are static as well (`TileMapToSphere.cs:139-166`, `TileMapToSphere.cs:234-258`). `Finish()` clears queue membership, but I did not find a matching global reset for those statics in the 2D exit path.
