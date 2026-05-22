# WSM3D Shipped Shaders — AssetBundle Bake Manifest

Shipped `.shader` source files that need baking into the platform-specific
`worldsphere` AssetBundles (`win/`, `linux/`, `osx/`). Runtime resolves them
via `Shader.Find("WSM3D/<name>")` after the bundle loads.

| Shader | Replaces | Pipeline |
|---|---|---|
| `OpaqueVertexColor.shader` | Standard for voxel meshes (eliminates Standard-lit-blackness) | BRP |
| `OpaqueVertexColorURP.shader` | URP variant of above | URP |
| `GerstnerWater.shader` | Standard for water surface (eliminates water-blackworld) | BRP/URP |
| `ScreenSpaceAO.shader` | Falls back to OnRenderImage SSAO pass | BRP |
| `ColorGradingLUT.shader` | OnRenderImage LUT lookup with exposure + saturation | BRP |
| `ProceduralSky.shader` | HDR atmospheric skybox with sun disc | BRP |
| `Impostor.shader` | 8-direction octahedral impostor billboard for LOD | BRP |

## Bake procedure (Unity 2022.3 Editor required)

1. Open `WorldSphereMod-AssetBundles/` project (or create it: Unity 2022.3
   LTS, empty 3D template).
2. Drop the `.shader` files into `Assets/WSM3D/Shaders/`.
3. For each shader, in inspector → AssetBundle dropdown → choose
   `worldsphere` (Standalone variant).
4. Build → Build AssetBundles → Target Win64/Linux/macOS.
5. Copy outputs to:
   - `WorldSphereMod/AssetBundles/win/worldsphere`
   - `WorldSphereMod/AssetBundles/linux/worldsphere`
   - `WorldSphereMod/AssetBundles/osx/worldsphere`
6. Commit binary blobs + shipped `.shader` source side-by-side.

## Runtime loader

At `Mod.PostInit`, `WorldSphereMod.Core.LoadAssets` resolves the platform's
bundle via `AssetBundleUtils.GetAssetBundle("worldsphere")`. Any `Shader.Find`
call for `WSM3D/*` then resolves cleanly.

## Verification

Each shader's resolution path is logged:

```
[WSM3D][MATERIAL] Shader probe: 'WSM3D/OpaqueVertexColor' FOUND
[WSM3D] Voxel material resolved via inline 'WSM3D/OpaqueVertexColor'.
```

If a shader is FOUND at probe but the material fallback chain still picks
Standard/Sprites-Default, the inline-shader try-compile guard at
`VoxelRender.TryCompileInlineVoxelShader` is the place to fix.

## Why not URP-only?

WorldBox ships Unity 2022.3 with the **Built-in Render Pipeline (BRP)**.
We can't change the pipeline from a mod. URP shaders simply won't resolve
at runtime in WorldBox. The URP variants are for downstream consumers /
future-proofing only.
