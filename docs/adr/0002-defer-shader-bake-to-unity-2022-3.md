# ADR 0002 — Defer lit shader AssetBundle bake to Unity 2022.3

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-05-17 |
| Deciders | KooshaPari |

## Context

Phase 4 (water Gerstner shader) and Phase 5 (`VoxelLit` shader for actor lighting + shadows) ship `.shader` source files under `WorldSphereMod/Resources/Shaders/`. At runtime these need to be compiled into AssetBundle binaries that the mod loads via `AssetBundleUtils.GetAssetBundle("worldsphere").GetObject<Shader>(...)`. The existing AssetBundle was baked in **Unity 2022.3** per upstream's `Compound-Spheres` README; bundles are serialization-version-sensitive across Unity majors.

This developer machine has Unity 2021.3.45f1 and Unity 6 (6000.3.11f1). Neither matches 2022.3.

Three options:

1. **Bake in 2021.3 / Unity 6 anyway.** Likely to produce a bundle WorldBox's 2022.3 runtime can't load. Validation only possible in-game.
2. **Install Unity 2022.3 LTS now.** Multi-GB download + IDE setup. Blocks no other work in the meantime.
3. **Defer shader bake; ship source + runtime fallbacks.** Material resolution walks a `Resources.Load<Shader>` → built-in URP / `Sprites/Default` fallback chain. The lit features (Phase 5b) and water Fresnel (Phase 4 full) are off until the bake lands.

## Decision

Option 3. Phases 4 and 5 ship in **lite** form using built-in shaders. The `.shader` source files are committed and visible in the repo so the bake step is just a Unity 2022.3 project that loads them; no engineering work blocks on this.

Phase 5 is split into **5-lite** (sun rig + cascaded shadow config — works with any URP shader) and **5b** (the actual `VoxelLit.shader` with `ShadowCaster` pass — needs the bake). Phase 4 is similarly split.

## Consequences

- **Positive.** All ten phases ship structurally complete. Visual ceiling is the only thing waiting on the bake; the entire feature flag matrix is wired regardless.
- **Negative.** The fork's "true 3D with real lighting" promise is half-fulfilled until Unity 2022.3 is installed. The placeholder unlit shader gives flat-shaded voxels.
- **Mitigations.** Screen-space derivative normals in the placeholder material give per-face flat shading (Phase 5-lite). Default light intensity is tuned so the unlit look isn't worse than upstream's pure sprite path.

## References

- `docs/phase5-prep.md` — Compound-Spheres-3D rebuild plan
- `docs/phase4-architecture.md` — water shader contract
- `docs/phase5-architecture.md` — lit shader contract
- `WorldSphereMod/Resources/Shaders/WaterGerstner.shader` + `VoxelSkin.compute` + `VoxelLit.shader` (when added)
