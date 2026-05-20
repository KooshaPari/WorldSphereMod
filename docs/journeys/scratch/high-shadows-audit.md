# High Shadows Audit

1. `HighShadows` does **not** add new shadow casters. `SunDriver.Init()` always creates one directional sun light with `LightShadows.Soft`, then calls `ShadowCascadeConfig.Apply(Core.savedSettings.HighShadows)` to mutate the active URP asset only if URP is present. The code here changes cascade count / distance / bias; it never registers renderers or caster lists. The actual casting geometry comes from the existing mesh renderers and batchers that already submit with shadows on by default (`MeshInstanceBatcher.Flush()` defaults to `ShadowCastingMode.On`).  
`WorldSphereMod/Code/Lighting/SunDriver.cs:18-41`  
`WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs:71-109`  
`WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:134-178`

2. The legacy 2D sprite shadow path still runs alongside the 3D path, so there is double-rendering for the same entity silhouette when real shadows are active. `Core.cs` still rewires `drawShadowsBuildings` / `drawShadowsUnit` through `ToShadow()`, which only positions the sprite-shadow geometry in 3D space. Building precalc still fills `render_data.shadows` and `shadow_sprites` when `shouldRenderBuildingShadows()` is true, and `SpriteShadow.LateUpdate` is still patched to update the shadow sprite every frame. That means you get both the sprite decal and the URP shadow map caster.  
`WorldSphereMod/Code/Core.cs:235-237`  
`WorldSphereMod/Code/QuantumSprites.cs:606-660`  
`WorldSphereMod/Code/Effects.cs:289-307`

3. Cost estimate: `HighShadows` itself is O(1) at toggle/init time, just reflection writes. The real cost is shadow-map rasterization for every visible shadow-casting renderer in each cascade. Going from 2 cascades to 4 cascades is roughly a 2x increase in shadow-pass work for the same visible caster set; per visible entity, expect one additional shadow-caster render per extra cascade, not a new CPU-side per-entity loop. The per-entity CPU work in this repo is already in the existing `calculateactordata3D` / `calculatebuildindata3D` fills.  
`WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs:86-109`  
`WorldSphereMod/Code/QuantumSprites.cs:448-553`  
`WorldSphereMod/Code/QuantumSprites.cs:606-673`

4. Latent bugs / risks:
   - Runtime toggle is effectively a no-op for lighting: `ApplyPhaseToggle()` only patches/unpatches classes marked with `[Phase]`, and I found no `HighShadows`-tagged patch class in `Lighting/`. So the UI toggle updates `SavedSettings`, but it does not re-run `ShadowCascadeConfig.Apply()` after init.  
   - `Shadow3D()` can null-deref if a `SpriteShadow` instance lacks `BaseEffect` or `sprite_renderer`; it blindly dereferences `effect.sprite_renderer`.  
   - `calculatebuildindata3D()` writes `tNeedNormalCheck` from inside `Parallel.For`, which is a data race even though the shadow arrays themselves are per-index.  
   - `ShadowCascadeConfig` captures original URP values once and never clears `_hasOriginals`, so a later pipeline swap in the same session can restore stale originals.  
`WorldSphereMod/Code/Core.cs:81-83`  
`WorldSphereMod/Code/Core.cs:122-147`  
`WorldSphereMod/Code/PhasePatchManager.cs:15-49`  
`WorldSphereMod/Code/Effects.cs:293-300`  
`WorldSphereMod/Code/QuantumSprites.cs:630-671`  
`WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs:13-20`  
`WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs:113-127`
