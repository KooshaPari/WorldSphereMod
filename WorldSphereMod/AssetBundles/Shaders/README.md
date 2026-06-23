# WSM3D Shipped Shaders - AssetBundle Bake Manifest

Shipped `.shader` source files that need baking into the platform-specific
`wsm3d-shaders` AssetBundles (`win/`, `linux/`, `osx/`). Runtime resolves them
via `Shader.Find("WSM3D/<name>")` after the bundle loads. The bundle is BRP-
only; `OpaqueVertexColorURP.shader` is intentionally excluded from the shipped
set.

| Shader | Replaces | Pipeline |
|---|---|---|
| `OpaqueVertexColor.shader` | Standard for voxel meshes (eliminates Standard-lit-blackness) | BRP |
| `GerstnerWater.shader` | Standard for water surface (eliminates water-blackworld) | BRP |
| `ScreenSpaceAO.shader` | Falls back to OnRenderImage SSAO pass | BRP |
| `ColorGradingLUT.shader` | OnRenderImage LUT lookup with exposure + saturation | BRP |
| `ProceduralSky.shader` | HDR atmospheric skybox with sun disc | BRP |
| `Impostor.shader` | 8-direction octahedral impostor billboard for LOD | BRP |

## Bake procedure (Unity 2022.3 Editor required)

1. Open the Unity bake project used by `Tools/Unity-Bake-Project/`.
2. Drop the `.shader` files into `Assets/WSM3D/Shaders/`.
3. Run the menu command **`WSM3D → Bake wsm3d-shaders AssetBundles`**.
   `BakeShaders.BakeAll()` copies sources, tags the bundle, builds the
   ShaderVariantCollection, applies the keep-all-variants / explicit-graphics-API
   guard, and builds win/linux/osx bundles into `WorldSphereMod/AssetBundles/`.
4. Commit binary blobs + shipped `.shader` source side-by-side.

### Verify the bake produced FULL shaders (#204)

The 80-vs-4936 ManagedStream crash is caused by program-less shader stubs.
After re-baking, confirm the program data is present BEFORE re-enabling any
shader in `Core.Sphere.SafeShaders`:

- `WorldSphereMod/AssetBundles/win/wsm3d-shaders` should grow well past the
  prior ~157 KB (each full postFX shader adds several KB of compiled program
  data; a stubbed bake keeps the file small with ~80-byte per-shader blobs).
- In the Editor Console, every shader should log a non-zero
  `[WSM3D-Bake] SVC +N variants: <name>` (N ≥ 1). Any `SVC +0 variants`
  warning means that shader will still strip — investigate before shipping.
- Load in WorldBox with the full `SafeShaders` set re-enabled in a throwaway
  test: NO `Mismatched serialization in the builtin class 'Shader'` and NO
  `ManagedStream object must be readable` for any of GerstnerWater /
  ProceduralSky / ColorGradingLUT / ScreenSpaceAO / ScreenSpaceGI / BrpBloom /
  BrpACES / FoliageWind. Only then commit the new bundle + re-enable SafeShaders.

## Runtime loader

At `Mod.PostInit`, `WorldSphereMod.Core.LoadAssets` resolves the platform's
main bundle via `AssetBundleUtils.GetAssetBundle("worldsphere")` and the shader
bundle via `AssetBundleUtils.GetAssetBundle("wsm3d-shaders")`. Any
`Shader.Find` call for `WSM3D/*` then resolves cleanly.

## Verification

Each shader's resolution path is logged:

```
[WSM3D][MATERIAL] Shader probe: 'WSM3D/OpaqueVertexColor' FOUND
[WSM3D] Voxel material resolved via inline 'WSM3D/OpaqueVertexColor'.
```

If a shader is FOUND at probe but the material fallback chain still picks
Standard/Sprites-Default, the inline-shader try-compile guard at
`VoxelRender.TryCompileInlineVoxelShader` is the place to fix.

## Why no URP variant?

WorldBox ships Unity 2022.3 with the **Built-in Render Pipeline (BRP)**.
We can't change the pipeline from a mod, so the bake script intentionally
skips the URP shader variant instead of carrying it in the shipped bundle set.
If `OpaqueVertexColorURP.shader` appears in the bake project, delete it before
rebaking.
