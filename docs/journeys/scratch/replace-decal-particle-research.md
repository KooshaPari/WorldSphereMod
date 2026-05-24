# DecalPool + ParticleEffects research

Scope: compare Unity Shuriken `ParticleSystem`, VFX Graph, `Decal Projector`, JBooth MicroSplat-Decal, and open-source decal/particle libs for WSM3D Phase 9.

## Bottom line

Best path for this mod is **keep the current runtime-owned pools built on built-in `ParticleSystem` + custom decal meshes/projectors, and avoid VFX Graph / SRP-only decal tech**. The mod has no scene-editor workflow, so anything that depends on authored graph assets, URP/HDRP-only components, or inspector-heavy setup adds friction without solving a blocker.

## Comparison

| Option | Built-in pipeline compat | Integration cost without scene editor | Verdict |
|---|---|---|---|
| **Unity Shuriken `ParticleSystem`** | Yes. It is Unity’s built-in particle system and works outside SRP-specific features. | Lowest. Can be spawned/configured entirely at runtime, pooled, and driven by code. Matches the current Phase 9 design. | **Best fit** |
| **Unity VFX Graph** | No for this mod’s target path. It is tied to URP/HDRP and requires the VFX Graph package + compatible render pipeline. | High. Needs graph assets and pipeline/package setup; runtime-only authoring is awkward. Good for later if the mod ever standardizes on SRP. | **Do not lead with it** |
| **Unity Decal Projector** | No. Unity’s Decal Projector is URP/HDRP-only. | Medium-high. Works if the project is already on URP/HDRP and the materials/pipeline are configured, but it is a dead end for built-in-pipeline compatibility. | **Not a fit now** |
| **JBooth MicroSplat-Decal** | Effectively tied to MicroSplat terrain/material workflow, not a general built-in-pipeline decal layer. | High. Stronger if the whole world rendering stack already uses MicroSplat; otherwise it is extra dependency surface for one effect type. | **Only if terrain already depends on MicroSplat** |
| **Open-source decal libs** | Mixed. Some target built-in, many are demo/special-case code. | Usually medium-high. They often assume editor setup, custom shaders, or a specific render path. Runtime-only pooling is still on us. | **Possible, but not a win over current code** |
| **Open-source particle libs** | Mixed. Most public options are samples or older GPU particle systems, not a durable general-purpose stack. | Usually high. They tend to be effect-specific or editor-centric, so they do not reduce the mod’s integration burden much. | **Not worth switching for Phase 9** |

## Recommendation

1. Keep the current Phase 9 approach: runtime-pooled `ParticleSystem` bursts and custom decal pooling/cleanup.
2. If you want a future upgrade path, make the abstraction boundary asset-backed so a later SRP/VFX Graph swap is possible without rewriting the Harmony hooks.
3. Do not migrate Phase 9 to VFX Graph or Unity Decal Projector unless the whole mod is already moving to URP/HDRP.

## Sources

- Unity Particle System manual: https://docs.unity3d.com/Manual/ParticleSystem.html
- Unity VFX Graph manual: https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@latest
- Unity Decal Projector docs: https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest
- MicroSplat Decals asset page: https://assetstore.unity.com/
- Open-source decal example: https://github.com/andywiecko/DynamicDecals
- Open-source particle example: https://github.com/keijiro/KvantStream
