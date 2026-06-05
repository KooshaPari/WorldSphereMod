# L1 task: enable real per-biome texture sampling on the heightfield terrain

## Context
- Branch: `wip/208-height-fix` in `E:/Dev/WorldSphereMod` (this is the working tree).
- Existing commit on the branch: `fdcbf56a fix: add terrain texture-array support for heightfield renderer`.
- The WSM3D terrain mesh is built by `External/Compound-Spheres/CompoundSpheres/HeightFieldRenderer.cs`. It supports a `_sampleTexture(tx,ty) -> int` callback that maps each quad to a slice of `_TerrainTexArray` (a Texture2DArray built in `WorldSphereMod/Code/Core.cs::CreateTextures` at line 2352).
- The material has `_UseTerrainTexArray` (float) and `_TerrainTexArray` (Texture2DArray) properties. When the material is bound with `_UseTerrainTexArray > 0.5` AND the array is non-null, `HeightFieldRenderer` duplicates the quad vertices (4 verts per quad) and writes per-quad UV slice indices into `uvsSlice` (channel 1). It also writes 0..1 UVs into channel 0.
- Current consumer (`WorldSphereMod/Code/Core.cs::ConfigureHeightField`, ~line 1349):
  - `sampleTexture` returns `WorldTileTexture(tile)` — wired correctly
  - `sampleColor` returns `GetTileColor(tile)` — currently samples ONE texel from the texture array at `(tileX%8, tileY%8)` and returns a single flat color (see `GetTexturePixelColor` line 945). User sees "biome color blocks" (one solid color per biome) because every quad in a biome gets the same flat color via corner-averaging of the same texel.
  - The texture-array path is set: lines 1426-1436 set `_UseTerrainTexArray=1` when `terrainTexArray != null`.

## Hard rules
- Build must be GREEN before claiming done. If `WorldSphereMod/Code/Bridge/BridgeServer.cs` has the `CS0234 WorldSphereMod.Sphere` error, this task is BLOCKED — return BLOCKED, do not try to fix that pre-existing error.
- DO NOT touch `WorldSphereMod/Code/Bridge/BridgeServer.cs`.
- DO NOT touch `WorldSphereMod/AssetBundles/win/win` or `.manifest` (those are bundle binaries; ignore them).
- Stay in the WIP branch `wip/208-height-fix` (already checked out).
- Use codex, not inline edits.
- The minimal MVP: make the terrain surface look like a per-biome textured pattern (sand grain, grass blade variation, dirt cluster, etc.) by sampling a small neighborhood from the texture array per quad — not just a single texel — and blending that into the per-quad vertex color. The texture-array path is already plumbed, so the shader will still sample the texture array per-quad too, but the per-quad vertex color adds within-biome variation that survives the corner-average. Real per-pixel variation needs a shader rewrite (out of scope for this fix).

## Plan

### 1. Replace single-texel sampling with multi-texel neighborhood sampling
- File: `WorldSphereMod/Code/Core.cs`
- Function: `GetTexturePixelColor` (line 945) and `GetTileColor` (line 929)
- Change: sample a small NxN neighborhood (suggest 4..8 samples spread across the 8x8 texture slice), blend them, and return a single Color32 that better represents the per-tile texture pattern. This means within a biome, adjacent quads will get slightly different blended colors (texture variation) instead of all the same texel.
- Acceptable: average of 4 corners (0,0), (0,7), (7,0), (7,7) of the 8x8 slice, OR a 3x3 sample grid. Either is fine. Use jittered sampling if you want extra variety.
- The neighborhood coords should still be derived from the tile position (tileX, tileY) so two tiles in the same biome pick different neighborhoods and produce different averaged colors.

### 2. (Optional) per-quad color variation beyond corner-average
- The `sampleColor` callback returns a flat color per (tx,ty). Adjacent quads that all live in the same biome pick the same (tx,ty)-based neighborhood and so might still look the same.
- Add a small per-call hash/noise offset to the neighborhood sampling position so the same (tx,ty) sampled twice in the same rebuild returns slightly different colors. Use a deterministic 2D hash of (tx,ty) to derive sub-texel offsets, e.g. `hash(tx,ty) * 0.5`. This gives within-biome variance >5 RGB units while keeping the result reproducible per-tile.

### 3. DO NOT enable `BuildTerrainTextureAtlas`
- The atlas UV remap is not ready (see the comment at line 2374). Leave that code path alone.

### 4. Verify
- `dotnet build WorldSphereMod.csproj -c Release -f net48` → 0 errors.
- Commit with message "fix(208): per-biome texture pattern via multi-texel sampling in GetTileColor"
- Return the commit SHA.

## Return
- Root cause file:line (`GetTexturePixelColor` at Core.cs:945)
- The actual code change (paste the new function or the relevant hunk)
- Commit SHA
- Build result
- "Blocker" or "Done"
