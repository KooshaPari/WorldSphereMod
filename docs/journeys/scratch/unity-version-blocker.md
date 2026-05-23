# Asset Bundle Unity Version Blocker

## Root cause confirmed 2026-05-22

WorldBox runtime: **Unity 2022.3.54f1** (from `worldbox_Data/Managed/UnityEngine.dll`)

Available locally:
- Unity 2021.3.45f1 ❌ too old
- Unity 6.3.11f1 ❌ too new

Bundles built with either version produce NML "Failed to load asset bundle"
on every launch → shaders never register → Voxel material falls back to
Standard → black-render cascade.

## Fix

Install Unity 2022.3 LTS (closest available — 2022.3.54f1 or any 2022.3.x)
via Unity Hub. Then re-bake:

```powershell
# Update ProjectVersion.txt to '2022.3.<patch>f1'
# Re-run Tools/bake-shaders.ps1
```

Bundle should then load. Workarounds (uncompressed, strict mode) did NOT
overcome the version barrier.

## Status

- 7 shader sources committed in WorldSphereMod/AssetBundles/Shaders/
- BakeShaders.cs ready to bake when correct Unity version present
- All runtime code (Core.LoadAssets shader force-load, VoxelRender shader
  candidates, MountainSlopeSmoothing chain) wired for WSM3D/* shader names
- Single blocker: bake project needs Unity 2022.3 LTS to produce compatible
  bundle binaries

After Unity 2022.3 install + re-bake, expect:
- '[WSM3D] Loaded shader from bundle: WSM3D/OpaqueVertexColor -> ...'
- 'Voxel material resolved via inline WSM3D/OpaqueVertexColor'
- Voxels render with vertex colors instead of Standard-lit-black
- Magenta MountainSlope tris fixed
- HDR skybox CubemapLighting connects to ProceduralSky.shader
