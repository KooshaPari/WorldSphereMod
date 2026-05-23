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
3. For each shader, in inspector → AssetBundle dropdown → choose
   `wsm3d-shaders` (Standalone variant).
4. Build → Build AssetBundles → Target Win64/Linux/macOS.
5. Copy outputs to:
   - `WorldSphereMod/AssetBundles/win/wsm3d-shaders`
   - `WorldSphereMod/AssetBundles/linux/wsm3d-shaders`
   - `WorldSphereMod/AssetBundles/osx/wsm3d-shaders`
6. Commit binary blobs + shipped `.shader` source side-by-side.

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
