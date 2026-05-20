# TileMapToSphere Audit

Scope: [`WorldSphereMod/Code/TileMapToSphere.cs`](../../../WorldSphereMod/Code/TileMapToSphere.cs), with lifecycle context from [`Core.cs`](../../../WorldSphereMod/Code/Core.cs) and [`Voxel/WorldUnloadPatch.cs`](../../../WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs).

## 1) What is patched, and where it gates on `IsWorld3D`

- `ZoneCamera.update` prefix (`TileMapToSphere.cs:12-24`) is the cleanest gate: it only replaces vanilla when `Core.IsWorld3D` is true, otherwise it returns `true` and leaves the 2D path alone.
- `WorldTilemap.generate` prefix (`TileMapToSphere.cs:52-63`) has **no** `IsWorld3D` check. It just rebuilds the three `TileQueue` statics every time `generate` runs.
- `WorldTilemap.addToQueueToRedraw` prefix (`TileMapToSphere.cs:65-80`) gates on `Core.GeneratingSphere` first, then on `Core.IsWorld3D`. In 3D it consumes the redraw request into the custom queues and suppresses vanilla queueing.
- `WorldTilemap.redrawTiles` prefix (`TileMapToSphere.cs:127-175`) does not gate on `IsWorld3D` directly. It only short-circuits the vanilla call during sphere generation when `Core.GeneratingSphere && pForceAll` is true.
- `WorldTilemap.enableTiles` prefix (`TileMapToSphere.cs:275-284`) is a direct 3D gate: it forces `pValue = false` only when `Core.IsWorld3D` is true.
- `MapBox.renderStuff` prefix (`TileMapToSphere.cs:286-330`) is always active. The 3D/2D split happens inside `render3DStuff()`, not at patch entry.
- `MapBox.updateDirtyTile` transpiler (`TileMapToSphere.cs:340-358`) has no runtime `IsWorld3D` branch. It rewrites pixel access regardless of mode; the 3D behavior depends on the downstream `MapLayer`/`PixelArray` hooks.

## 2) Hot per-frame work

- The only clearly hot patch is `MapBox.renderStuff` (`TileMapToSphere.cs:286-330`). It runs every frame, always calls `QuantumSpriteManager.update()`, and always runs `World.world.updateDebugGroupSystem()`.
- In 3D, that same path also checks the redraw queues and, every 0.1 seconds, calls `Redraw3DTiles()` and `Core.Sphere.RefreshSphere()` (`TileMapToSphere.cs:293-323`). That is the main recurring cost.
- `WorldTilemap.addToQueueToRedraw` is O(1) hash-set bookkeeping (`TileMapToSphere.cs:68-79`, `82-125`) and is not itself a frame hog.
- `updateDirtyTile` is IL plumbing only; the hot work is whatever code later writes pixels into `PixelArray` (`TileMapToSphere.cs:340-358`, `454-490`).

## 3) Reload / state-corruption risk

- I did **not** find evidence that Harmony patches are re-registered on world reload; `Core.Init()` patches once, and the risk is not duplicate patching.
- The real risk is stale static state. `Core.Sphere.Prepare()` builds `BaseLayers` and `CachedColors` once in `PostInit` (`Core.cs:436-443`), and I found no unload-time reset for those structures.
- `WorldUnloadPatch.OnFinish()` clears other long-lived caches, but not the `TileMapToSphere` queues or the `BaseLayers` / `CachedColors` mapping (`WorldUnloadPatch.cs:12-31`).
- `generate` does replace the three queue statics (`TileMapToSphere.cs:52-63`), so repeated generation is mostly safe. The unsafe edge is a world reload path that reuses the same process without re-running `Sphere.Prepare()`: then `MapLayer` keys in `CachedColors` can go stale, and later `MapLayer.clear` / `createTextureNew` / `updateDirtyTile` hooks may point at old layer wrappers.

## Verdict

The patching itself is mostly well-gated. The main runtime cost is the always-on `renderStuff` detour, and the main reload hazard is stale static cache state, not patch duplication.
