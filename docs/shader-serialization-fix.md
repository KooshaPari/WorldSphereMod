# Shader Serialization Fix

## Goal

Make the `wsm3d-shaders` AssetBundle survive a 2022.3.62f3 bake and still deserialize with non-empty `Shader.name` values in a 2022.3.60f1 runtime.

## What Changed

I hardened the shader sources that had been coming back empty-name at runtime:

- Removed bundle-absent fallbacks from the fragile shaders and replaced them with `Fallback Off` where a fallback was not required.
- Added `#pragma target 3.0` to the remaining shaders that were missing it.
- Kept the BRP shaders on `CGPROGRAM` + `UnityCG.cginc`.
- Left the working `OpaqueVertexColor` shader untouched as the baseline reference.

Files updated:

- `Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/GerstnerWater.shader`
- `Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/ScreenSpaceAO.shader`
- `Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/ScreenSpaceGI.shader`
- `Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/ProceduralSky.shader`
- `Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/Impostor.shader`
- `Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/FoliageWind.shader`
- `Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/BrpBloom.shader`
- `Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/BrpACES.shader`
- `WorldSphereMod/Resources/Shaders/BrpBloom.shader`
- `WorldSphereMod/Resources/Shaders/BrpACES.shader`
- `WorldSphereMod/Resources/Shaders/FoliageWind.shader`

## Bake Result

I rebaked with:

```powershell
pwsh -File Tools/bake-shaders.ps1 -UnityExe "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
```

The bundle verifier reported:

- `11 shaders with valid names, 0 empty/null.`

The verified shaders were:

- `Hidden/WSM3D/BrpACES`
- `Hidden/WSM3D/BrpBloom`
- `WSM3D/ColorGradingLUT`
- `WSM3D/FoliageWind`
- `WSM3D/GerstnerWater`
- `WSM3D/Impostor`
- `WSM3D/OpaqueVertexColor`
- `WSM3D/ProceduralSky`
- `WSM3D/ScreenSpaceAO`
- `Hidden/ScreenSpaceGI`
- `WSM3D/StratumVoxelPBR`

## Notes

- This run used Unity 2022.3.62f3 for the bake, even though the repo script warns that WorldBox runtime is 2022.3.60f1.
- I did not edit `Core.cs`.
- The bundle output was regenerated under `WorldSphereMod/AssetBundles/win/wsm3d-shaders`.
