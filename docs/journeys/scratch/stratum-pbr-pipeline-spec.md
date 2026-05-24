# Stratum PBR Pipeline Spec for WSM3D Voxels

## 1) Objective

Consume Stratum texture-pack material data for voxels with minimal visual/artifacts regression, while keeping runtime generation predictable:

- Stratum block data: `albedo`, `normal`, `ao`, `roughness`, `metallic`, `height`.
- Current runtime: voxel material path is vertex-color-driven and does not use per-voxel UVs.
- Target: sample per-block textures from an atlas and bind to material inputs.

Primary question to answer up front: **migrate to URP/Lit for full PBR, or stay Built-In and encode a reduced PBR representation through the existing vertex-color-centric path?**

## 2) Current state assumptions

- Voxel meshes are generated procedurally and currently feed color via vertex attributes.
- Existing shader path does not rely on `_BaseMap` / `_NormalMap` / `_OcclusionMap` / `_MetallicGlossMap`.
- Import pipeline currently handles Stratum PNG per-block packs, but no atlas packing or PBR map fan-out into mesh material channels.

## 3) End-to-end PBR intake pipeline design

### 3.1 Importer: convert per-block map set into atlases

For every material block entry:

1. Load all six source PNGs.
2. Validate dimensions and alignment (`albedo/normal/ao/roughness/metallic/height` must share dimensions).
3. Pack by texture role into separate atlases:
   - `Stratum_AlbedoAtlas`
   - `Stratum_NormalAtlas`
   - `Stratum_AOAtlas`
   - `Stratum_RoughnessAtlas`
   - `Stratum_MetallicAtlas`
   - `Stratum_HeightAtlas`
4. Reserve UV footprint + padding:
   - tile size = source tile size (16x16, 32x32, etc.) or power-of-two variant as needed
   - include 1–2 px gutter/padding in atlas to avoid bleeding at tile borders
5. Emit stable atlas metadata:
   - `atlasPageId`
   - `tileRect` (`u0,v0,u1,v1`)
   - `mapType` availability flags for optional maps
   - optional `heightScale` from metadata for parallax/parallax-like AO enhancement
6. Cache atlas and metadata with hash-based invalidation tied to source PNG bytes.

Metadata must be queryable at mesh-build time for each voxel/block.

### 3.2 Runtime material model

Voxel materials are selected by material archetype:

- Default non-PBR fallback material (existing path) for packs that only provide albedo and no PBR dependencies.
- PBR material with explicit map slots:
  - `_BaseMap` (albedo/opacity)
  - `_NormalMap`
  - `_OcclusionMap`
  - `_MetallicGlossMap` (or `_MetallicMap` + `_Smoothness` split depending on chosen shader model)
  - optional `_HeightMap` (used only if feature is supported in final shader and material mode)

### 3.3 Per-voxel UV generation

Current color-based assignment must switch to UV attribution:

1. For each voxel instance, determine source sprite pixel coordinates in Stratum block space:
   - map 3D voxel occupancy to source sprite texel using face/axis convention already used by the voxelizer
2. Use importer `tileRect` to convert sprite pixel coordinate into atlas UV:
   - `uv = tileRect.min + spritePixelOffset / tileSize * tileRect.size`
3. Write UVs into mesh vertices (shared with existing UV2/UV3 reservations if present; else UV0).
4. Keep vertex color reserved for secondary data only (if any), e.g.:
   - `r/g/b`: optional height blend/biome tint
   - `a`: optional occlusion multiplier or debug flag
5. Ensure UVs remain stable across LOD/meshing variations and chunk rebuilds.

## 4) Option A: URP Lit migration (full PBR)

## 4.1 What changes

- Project pipeline shift from Built-In to URP for voxel rendering domain:
  - package install / version alignment with existing URP support in project
  - Graphics settings + pipeline asset
  - replace shader references for voxel and dependent materials
- Replace current voxel shader with URP Lit variant or custom HLSL based on URP surface conventions.
- Material binding in runtime to the URP property names used above.
- Importer output: multiple atlases remain the same, but material creation/sampling changes to URP conventions.

## 4.2 Benefits

- Real PBR support:
  - normal mapping
  - AO/roughness/metalness interactions
  - physically meaningful specular energy flow
- Cleaner future expansion:
  - decals/emissive/clear coat variants
  - consistent with modern shader tooling

## 4.3 Risks and costs

- **High dependency risk**: this is effectively a rendering-stack migration, not an isolated feature.
- **Asset recompile risk**: global material/shader incompatibilities and post-processing assumptions across existing non-voxel materials.
- **Tooling risk**: editor and build scripts may need URP-specific fixes for generated assets.
- **Schedule risk**: likely the largest single feature chunk in this scope.

## 5) Option B: Stay Built-In + reduced PBR (smaller migration)

## 5.1 What changes

- Keep the current Built-In pipeline.
- Keep mesh generation largely intact with UV injection from section 3.3.
- Bake Stratum materials into a Built-In-compatible pseudo-PBR pathway:
  - pack selected channels into textures/vertex channels
  - use custom shader to approximate PBR lookups with limited inputs
- Potential fallback: keep one “legacy” subshader for pure vertex-color path.

## 5.2 Benefits

- Smaller dependency surface, lower immediate integration risk.
- Faster path to ship an improvement over color-only materials.
- Lower chance of broad rendering regressions in unrelated systems.

## 5.3 Limits and quality tradeoffs

- No native URP Lit semantics.
- Normal mapping possible only if shader and map layout are custom-handled.
- Height becomes a fake-only signal (parallax approximation or AO bias), not true geometric displacement.
- AO/roughness/metalness precision is bounded by channel packing and compatibility hacks.
- Risk of artifact differences between current and future texture packs because of custom decode conventions.

## 6) Comparison summary

### URP Lit
- Visual fidelity: high  
- Rendering complexity: high  
- Delivery timeline: longer  
- Regression risk: high in adjacent systems  
- Runtime flexibility: highest  

### Built-In Reduced PBR
- Visual fidelity: medium  
- Rendering complexity: medium  
- Delivery timeline: shorter  
- Regression risk: lower  
- Runtime flexibility: moderate

## 7) Recommendation

Ship in two phases:

1. **Phase 1 (low risk):** implement atlas importer + UV plumbing + optional pseudo-PBR in current Built-In shader path.
   - validates pipeline concept for stratum texture sampling at voxel level.
   - establishes metadata contract and atlas build tooling.
2. **Phase 2 (strategic):** evaluate URP Lit migration once artifact surface is understood and accepted.
   - reuse atlas contracts and UV generation from Phase 1.
   - migrate only voxel material(s) first if full pipeline migration cannot be done globally.

Given the stated “large cost” concern, recommend **Phase 1 first** and make URP migration a gated phase decision after proving content throughput and performance impact.

## 8) Acceptance criteria (for the selected path)

- Every voxel references an atlas tile with stable UVs.
- No per-frame texture loads from disk; only sampled atlases from loaded assets.
- No hard dependency on world-space alpha clipping changes from unrelated systems.
- Stratum block with all 6 maps renders a materially richer result than vertex-color-only path.
- Existing non-PBR textures continue to render without crash and with acceptable fallback.

## 9) Open implementation questions

- Determine final atlas max size/packing strategy for 16K tile sets and total block count.
- Decide if height should be:
  - omitted,
  - used as parallax strength, or
  - deferred to phase 2.
- Confirm whether `_MetallicGlossMap` should be single texture (roughness in alpha) or split map input in custom shader.

## 10) Implementation status (2026-05-23)

| Item | Status |
|------|--------|
| Spec + phased recommendation (§7) | Done |
| `StratumVoxelPBR.shader` BRP scaffold (`_BaseMap` / `_NormalMap` / `_OcclusionMap` / `_MetallicGlossMap` / `_HeightMap`) | Done (albedo pass only; normal/AO/metal not sampled yet) |
| `OpaqueVertexColor.shader` legacy fallback (§5.1) | Done (existing) |
| `WorldSphereMod/Resources/Shaders/VoxelLit.shader` URP track (§4, Phase 2) | Scaffold only |
| Stratum 6-map atlas importer (§3.1) | Not started |
| Atlas metadata + hash cache | Not started |
| Per-voxel UV generation in mesh build (§3.3) | Not started |
| `VoxelRender` / `Core.Sphere.LoadedShaders` Stratum material path | Not started |
| `wsm3d-shaders` bundle bake includes `StratumVoxelPBR` | Not started |
| E2E shader-on-disk invariant (`StratumPbrPipelineInvariantsTests`) | Done |

Shader sources guarded by CI (must exist on disk before bake):

- `WorldSphereMod/AssetBundles/Shaders/StratumVoxelPBR.shader`
- `Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/StratumVoxelPBR.shader`
- `WorldSphereMod/AssetBundles/Shaders/OpaqueVertexColor.shader` (fallback)

## 11) Known gaps (not in Phase 1 scope)

- Normal/AO/roughness/metallic/height are declared on `StratumVoxelPBR` but not wired in the fragment stage yet.
- No runtime atlas loader or material archetype switch in `VoxelRender`.
- URP Lit migration remains a gated Phase 2 decision (see §7).
