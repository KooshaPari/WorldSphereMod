# External Sprite-to-Voxel Libraries: Comparative Analysis

Research date: 2026-05-20. Goal: inform replacement of `SpriteVoxelizer.Build` (currently produces flat per-texel slab — RGBA pixel becomes a 1-voxel-deep column). User wants true 3D balloon/inflation output for actors.

---

## 1. Voxel Maker (Unity Asset Store id=24)

**Status: DEPRECATED.** Asset Store page (https://assetstore.unity.com/packages/tools/sprite-management/voxel-maker-24) now only shows a deprecation notice — no longer purchasable, no support. Cannot be evaluated and cannot be legally redistributed inside WorldSphereMod3D regardless of algorithm.

**Verdict: Skip.**

---

## 2. MagicaVoxel Ecosystem (https://ephtracy.github.io/)

Not an algorithm — a **pipeline alternative**. Authors hand-sculpt voxel models in MagicaVoxel (free, closed-source, freeware EULA permits commercial+free distribution of *output*, but not the editor binary). `.vox` is a public, documented chunked binary format. Models ship as a content bundle inside the mod.

### Free Unity importers found

| Repo | License | Stars | Runtime? | Notes |
|---|---|---|---|---|
| [miventech/NativeVoxReaderForUnity](https://github.com/miventech/NativeVoxReaderForUnity) | MIT | ~22 | YES (`ReaderVoxFile.Read()`) | Greedy meshing (~90% poly reduction), texture atlas baking, hierarchy + groups, zero deps |
| [minuJeong/VoxImporter-for-Unity](https://github.com/minuJeong/VoxImporter-for-Unity) | MIT | low | partial | Loads into Texture3D — good for raymarched VFX, not mesh |
| [ray-cast/UnityVOXFileImport](https://github.com/ray-cast/UnityVOXFileImport) | MIT | low | editor-focused | Prefab generator |
| [darkfall/MagicaVoxelUnity](https://github.com/darkfall/MagicaVoxelUnity) | MIT | medium | editor-only | Menu-driven loader |
| [Zarbuz/VoxToVFX](https://github.com/Zarbuz/VoxToVFX) | MIT | medium | runtime (VFX Graph particles) | Not a mesh — GPU particle voxels |

**Output quality:** Whatever the artist sculpts — true 3D, the gold standard.
**Integration cost:** Medium. We must (a) author N hand-made models per actor archetype, (b) bundle them, (c) wire into actor spawn code in place of `SpriteVoxelizer.Build`. The original WorldBox sprite pool is ~hundreds of unique actors — full coverage is weeks of art work.
**Best fit:** Hybrid — use NativeVoxReader for "hero" actors (king, dragon, megafauna), keep procedural voxelizer for the long tail.

---

## 3. GitHub Open-Source Sprite→Voxel Converters

### 3a. [jantepya/Unity-Sprite-Voxelizer](https://github.com/jantepya/Unity-Sprite-Voxelizer)
- **License:** MIT. **Stars:** 49.
- **Algorithm:** Pixel extrusion — each opaque texel becomes a cube at depth = 1 (configurable uniform depth). Editor tool (`Window/Voxelize Sprite`).
- **Output:** **Flat slab** (same shape as our current code, just nicer mesh optimization). Not balloon.
- **Integration cost:** Low (drop-in MIT). **Value-add over current code:** marginal — same algorithm class.

### 3b. [CreggHancock/SpriteToVoxel](https://github.com/CreggHancock/SpriteToVoxel)
- **License:** MIT. **Stars:** ~2. JavaScript web tool, not Unity.
- **Output:** Flat slab voxel exporter for browser preview. **Not usable** directly in C# Unity runtime.

### 3c. [mattatz/unity-voxel](https://github.com/mattatz/unity-voxel)
- **License:** MIT. **Stars:** 800+.
- **Algorithm:** Voxelizes existing **3D meshes** (not sprites) via GPU compute. Wrong direction for our problem — we have 2D, need 3D.
- **Possible indirect use:** combine with an inflation step (§4) that produces a mesh, then voxelize for our grid.

### 3d. AutoVoxel (Unity Asset Store, free, https://assetstore.unity.com/packages/tools/modeling/autovoxel-generate-3d-voxel-meshes-from-sprites-308602)
- **License:** Standard Unity Asset Store EULA — **does not permit source-redistribution inside a free open-source mod**. Per-end-user installation is fine, but bundling DLLs into our GitHub release is on shaky legal ground. **Closed-source, opaque algorithm.** Unity 6000.0.19f1+.
- Description mentions tile sizes 4×4 to 32×32, single-click generation — strongly suggests pixel-extrusion (flat slab), not inflation. No 3D shaping params surfaced.

### 3e. [TheWulo/PixelArt 3D](https://assetstore.unity.com/packages/tools/modeling/pixelart-3d-voxel-models-generator-51242) — $8, EULA, last updated 2016 (Unity 5.3). Pixel-extrusion. **Skip** (stale, paid, EULA-locked).

---

## 4. True 2D→3D Inflation Algorithms

This is where the user's "balloon" intuition maps to real CG literature.

### 4a. Distance-transform balloon inflation
Classic technique (Igarashi et al. "Teddy" 1999; Vicente & Agapito ["Balloon Shapes"](https://www.semanticscholar.org/paper/Balloon-Shapes:-Reconstructing-and-Deforming-with-Vicente-Agapito/1238e53c42ae5e355abbf25606b1e081da1a3e32)):
1. Compute 2D Euclidean signed-distance transform of sprite alpha silhouette.
2. For each opaque texel, set voxel column thickness `d(x,y) = k · sqrt(D(x,y))` centered on the sprite plane, producing a half-ellipsoid cross-section per pixel — i.e., the silhouette "puffs out" along Z.
3. Optionally mirror front/back for symmetric balloon.

**Cost on 32×32 sprite:** sub-millisecond (O(N) two-pass distance transform). Easily runtime.
**Output quality:** True 3D, smooth Pixar-like inflation. Single-view limitation: back of model = mirrored front.
**Integration cost:** ~150 lines of C# in `SpriteVoxelizer`. No dependencies. **Highest value-per-effort.**

Reference: [Growing 3D clouds from 2D maps via full spherization (arXiv 2503.19259, 2025)](https://arxiv.org/pdf/2503.19259) — modern variant explicitly uses interior distance transform to derive 3D thickness from a 2D mask.

### 4b. Pixel Pets / "Crossy Road" multi-view stitch
Author front + side sprite. Algorithm intersects the two extrusions (boolean AND in 3D). Output is true 3D within the silhouette envelope. Requires 2× sprite art — doesn't suit retrofitting WorldBox's existing single-sprite pipeline.

### 4c. Voxel sculpting tools with 2D import
- [Goxel](https://goxel.xyz/) — GPL3. Has "image plane" import (extrusion only). Not a library, an editor.
- Qubicle — proprietary, $50+. Skip.

---

## Ranked Recommendation

1. **Implement distance-transform balloon inflation in-engine** (§4a). Replace the per-texel slab in `SpriteVoxelizer.Build` with a DT-driven per-column thickness. ~150 LOC, MIT-compatible (own code), runtime-cheap, immediate visual win for *every* existing WorldBox actor with no asset authoring. This directly addresses the user's "true 3D balloon" requirement.
2. **Add NativeVoxReader (MIT)** for hero actors (§2). Hand-author dragon/king/etc. in MagicaVoxel; load via `ReaderVoxFile.Read()` at spawn. Greedy meshing keeps tri-count sane. Layer on top of #1 — DT inflation is the fallback for any actor without a `.vox` override.
3. *(Optional later)* Mattatz `unity-voxel` GPU mesh→voxel if we ever ingest external 3D assets.

**Reject:** Voxel Maker (deprecated), AutoVoxel (EULA blocks redist), PixelArt 3D (stale/paid), jantepya (flat slab — same as today), SpriteToVoxel JS (wrong runtime).

### Why this beats current implementation
Current `SpriteVoxelizer.Build` = constant Z-depth = §3a equivalent = the worst of the surveyed options. DT inflation costs the same per-frame budget but produces volumetric silhouettes. Combined with the existing `VoxelScaleMultiplier=8.0` (see WSM3D Phase 1 fix) the visual delta will be dramatic — a king sprite goes from a flat playing-card to a rounded chess-piece-like figure with one self-contained code change and zero external deps.

---

## Sources
- https://assetstore.unity.com/packages/tools/sprite-management/voxel-maker-24
- https://ephtracy.github.io/
- https://github.com/miventech/NativeVoxReaderForUnity
- https://github.com/jantepya/Unity-Sprite-Voxelizer
- https://github.com/CreggHancock/SpriteToVoxel
- https://github.com/mattatz/unity-voxel
- https://github.com/minuJeong/VoxImporter-for-Unity
- https://github.com/Zarbuz/VoxToVFX
- https://assetstore.unity.com/packages/tools/modeling/autovoxel-generate-3d-voxel-meshes-from-sprites-308602
- https://www.semanticscholar.org/paper/Balloon-Shapes-Vicente-Agapito/1238e53c42ae5e355abbf25606b1e081da1a3e32
- https://arxiv.org/pdf/2503.19259
- https://goxel.xyz/
