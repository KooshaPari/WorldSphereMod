# Focal Engine (1.0.9 MC 1.20.4) — shader techniques extract

**Input artifact:**
`D:/koosh/Downloads/focal-stack/PrismLauncher/instances/Focal-1.20.4/.minecraft/mods/Focal-Engine-Universal-1.0.9-MC-1.20.4.jar`

## What exists in this jar
- Archive scan found no `README.md`/`README.txt`/`readme` file.
- No `.shader`, `.glsl`, `.vsh`, or `.fsh` resources were present.
- Non-class resources were limited to JSON/XML/toml properties/manifest files and PNG/logo.
- `jar` entries point to runtime/runtime-framework classes such as:
  - `graphics/continuum/shaderpack/FocalShaderpackBase.class`
  - `graphics/continuum/shaderpack/impl/*.class`
  - OptiFine integration mixins under `graphics/continuum/**/optifine` and `mixins...`
- This indicates a loader/bridge that hosts the **Continuum shader framework**, not bundled shader code itself.

## Technique scan inside this jar
I searched text-bearing resources and class-constant content for these techniques:
- `PTGI`
- `voxel cone tracing`
- `RTAO`
- `volumetric`
- `GI`
- `SSAO` / `AO`

Result: **no explicit in-jar shader implementation hits** for PTGI, volumetric lighting, voxel cone tracing, or RTAO.

## Continuum shaders status from this artifact
- No Continuum shader source files were found in this jar.
- Therefore, this artifact alone cannot be used to confirm whether the bundled **Continuum shader packs** (if any) use those techniques.
- Likely behavior comes from external shader packs downloaded/manually installed outside this jar.

## License / implementation notes
- **Re-implementable (license-safe):** the *algorithmic concept* itself (e.g., GI/AO/volumetric concepts, tracing approximations) and the *idea* is generally not copyrightable.
- **Not license-safe:** copying Continuum’s actual GLSL/HLSL code, parameter naming, or shader-side constants/layout from the actual shader pack.
- Since this jar has no shader source, the file does **not** provide reusable shader code for direct porting.

## Technique → WorldSphereMod shader coverage

| Technique | Continuum artifact evidence from this jar | WSM equivalent |
|---|---|---|
| PTGI | **Not evidenced** in-jar. | **Gap** |
| Volumetric lighting | **Not evidenced** in-jar. | **Gap** |
| Voxel cone tracing | **Not evidenced** in-jar. | **Gap** |
| RTAO | **Not evidenced** in-jar. | **Gap** *(closest is screen-space AO in `ScreenSpaceAO.shader`, which is still a different technique than real-time ray AO)* |

## Coverage against provided WSM shader list
- `ContinuumACES.shader`: likely tone mapping/post chain utility, no PTGI/RTAO/VCT evidence from this artifact.
- `ContinuumSkybox.shader`: sky environment path only, no match to PTGI/VCT/RTAO scan.
- `ContinuumWaterGerstner.shader`: water surface simulation only.
- `ScreenSpaceAO.shader`: could satisfy **screen-space AO** use-cases, not RTAO.
- `ScreenSpaceGI.shader`: screen-space GI, not PTGI/voxel cone tracing.

## Practical follow-up
To definitively map Continuum pack techniques, inspect the downloaded `.zip` shader pack files used by Focal Engine (typically outside this jar).
