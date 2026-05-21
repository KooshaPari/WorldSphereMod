# URP Migration Plan for WSM3D (Built-In → URP)

## Objective
Migrate WSM3D rendering from Built-In pipeline to URP to unlock:
- Stratum PBR compatibility
- DLSS path
- Post-process volumes + Color Grading
- Decals
- RT lighting
- Better long-term rendering extensibility

This plan is scoped to the requested render surface migration; gameplay and logic stay untouched.

## Migration scope and assumptions
- Target pipeline: URP in WorldBox-compatible runtime context used by WSM3D.
- Keep WorldBox-specific compatibility shims intact; do not refactor unrelated graphics abstractions.
- Preserve existing material assignment behavior and inspector expectations where practical.
- Treat unknown WorldBox API/asset references via decompile review before final code changes.
- Phase-gated approach with `SavedSettings` flag (default OFF) for camera/post-stack rollout.

## Inventory (catalog)
The following assets are the primary migration set. Some are single `.shader` files and some are `.mat` materials currently bound to legacy built-in shaders.

1. `WaterGerstner.shader` (Built-In Surface)
   - Current: Standard surface shader with legacy lighting, reflection, and property wiring.
   - Target: URP ShaderGraph or hand-authored HLSL path.
   - Functional notes: water normals, animation masks, spec/specular highlights, fresnel/refraction behavior must be preserved.

2. `FoliageWind.shader`
   - Current: Built-in shader for wind sway and vertex motion.
   - Target: URP-compatible shader supporting vertex manipulation + vertex-color/layered tint.
   - Functional notes: ensure motion stability and wind-mask compatibility.

3. `WSM3D.Voxel.Placeholder` material (`Sprites/Default`, opaque cutout)
   - Current: Built-In default sprite path used as placeholder.
   - Target: URP `SimpleLit` shader with cutout + vertex color support.
   - Functional notes: transparent/cutout behavior and vertex color tinting must remain deterministic.

4. `ImpostorBillboard` material
   - Current: likely URP-incompatible legacy/unlit-style setup.
   - Target: URP unlit/particle-compatible billboarding material.
   - Functional notes: keep orientation stability, per-instance UV logic, and alpha behavior identical.

5. `ProceduralSky.shader`
   - Current: sky shader in legacy pipeline.
   - Target: URP `Skybox`-style or lightweight URP sky shader replacement compatible with your existing camera setup.
   - Functional notes: preserve horizon/gradient control and day-night integration if present.

Additional assumed migration items for total **~7 shader rewrites**:
6. Placeholder terrain/detail terrain-layer related shader(s) encountered during material pass (often one shared foliage/ground shader fallback)
7. Shared decal/overlay compatible pass shaders in the same rendering feature set (to avoid mixed pipelines)

> There are likely to be 6–8 dependent materials tied to #1-#5; migration must include fallback checks for each material instance and override slot.

## Cost estimate by item
Effort is in engineering dev-days with risk notes.

### 1) `WaterGerstner` (URP conversion) — **2.0 days**
- 1.0: port lighting model, surface inputs, specular/roughness workflow, texture/normal stacks
- 0.5: refraction/fresnel parity pass
- 0.5: render-order/depth/water blend validation
- Risk: high visual regression risk; requires tuned test renders.

### 2) `FoliageWind` — **1.0 day**
- 0.6: vertex-stage wind transform port
- 0.2: alpha/cutout and shadow caster parity
- 0.2: wind mask + tint parity and perf sanity
- Risk: motion artifacts in LOD transitions.

### 3) `WSM3D.Voxel.Placeholder` (`Sprites/Default`→URP `SimpleLit`) — **0.5 day**
- 0.3: new URP material + cutout + vertex color setup
- 0.2: prefab/asset-link rebind + prefab override tests
- Risk: tiny visual differences in alpha threshold and tinting.

### 4) `ImpostorBillboard` material rewrite — **0.5 day**
- 0.3: URP-compatible shader/material path
- 0.2: alpha blending + LOD/culling verification
- Risk: billboards popping under camera transitions.

### 5) `ProceduralSky` — **1.5 days**
- 0.8: URP sky shader port
- 0.5: exposure/atmosphere gradient controls
- 0.2: fog/lighting hooks parity and default-state validation
- Risk: global sky mismatch affects all look-dev.

### 6) Ancillary legacy fallback shader A (terrain/detail) — **0.5 day**
- Minimal compatibility rewrite to avoid built-in-only pipeline dependencies.

### 7) Ancillary legacy fallback shader B (decal/overlay path) — **0.75 day**
- Ensure decals and post-process layering can coexist with URP camera stack.

### Camera setup + URP volume setup (not per-shader) — **1.0 day**
- URP pipeline assets + camera stack migration
- Post-process volumes (Color Grading mandatory)
- Decal layer setup and RT-light path enabling
- DLSS compatibility checks
- Risk: integration issues if existing camera graph expects built-in hooks.

### Cross-cutting migration work (asset wiring, validation, cleanup) — **0.75 day**
- Material remap script/audit pass
- Quality gates and runtime fallback guardrails
- Build pipeline checks + docs updates

## Estimated total
- Shader rewrites: ~6.75 days (7 assets)
- Non-shader runtime/renderscape setup: 1.75 days
- **Total: ~8.5 days** (range 8–10 depending on parity passes and toolchain behavior)

## Suggested execution sequence
1. Establish URP baseline
   - Add URP asset/profile, verify game launch path supports it, keep feature toggle OFF.
2. Migrate high-risk geometry first
   - `WaterGerstner`, `ProceduralSky`.
3. Convert vegetation/placeholder materials
   - `FoliageWind`, placeholder material, `ImpostorBillboard`.
4. Add remaining fallback shaders + shared material remaps
5. Rewire camera/stack + post-processing
6. Smoke test rendering parity
7. Flip phase flag only after in-game pass, then ship.

## Acceptance criteria
- All listed assets render correctly in URP-compatible pipeline with no hard dependency on built-in-only shader syntax.
- Stratum PBR and URP post-processing volumes are active without built-in fallback.
- Decal path and RT-lighting toggles are functional in at least one target profile.
- DLSS path can be exercised via existing runtime feature flags.
- ColorGrading and volume stack are applied via URP global/local volume profiles.
- No user-facing material regression in voxel placeholders, impostors, and foliage billboards.

## Exit decision checklist
- [ ] Shader compile passes for all converted materials
- [ ] Camera renders correctly with URP volume stack
- [ ] Visual parity approved for at least 3 representative biomes/views
- [ ] No regressions in LOD/impostor transitions
- [ ] Rollback path documented in docs and branch switch plan ready