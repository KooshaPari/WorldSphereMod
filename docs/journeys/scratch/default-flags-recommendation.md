# Default Flags Recommendation

Recommendation for a true “Phase 0 minimum” first run:

- Keep `Is3D` on. [WorldSphereMod/Code/SavedSettings.cs:8-18](../../WorldSphereMod/Code/SavedSettings.cs#L8-L18)
- Turn `VoxelEntities` on for fresh installs / new configs.
- Keep `CrossedQuadFoliage`, `WorldspaceUI`, and `ParticleEffects` on.
- Turn `DebugVoxelOutline` off.
- Leave `ProceduralBuildings`, `MeshWater`, `HighShadows`, `SkeletalAnimation`, `DayNightCycle`, `PostFX`, `VoxelMeshSmoothing`, `AutoTest`, and `ProfilerDump` off.

Why this is the right minimum:

- `VoxelEntities` is the actual gate for voxel actors. When it is off, the mod immediately falls back to vanilla sprite rendering; the smoke test explicitly says toggling it off reverts to vanilla without restart. That makes the risk visible, but also easy to contain. [WorldSphereMod/Code/SavedSettings.cs:21-27](../../WorldSphereMod/Code/SavedSettings.cs#L21-L27) [WorldSphereMod/Code/Voxel/VoxelRender.cs:22-24](../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L22-L24) [docs/smoke-test-phase1.md:16-24](../../docs/smoke-test-phase1.md#L16-L24) [docs/smoke-test-phase1.md:71-75](../../docs/smoke-test-phase1.md#L71-L75)
- The LodSelector change already removes the “too small to see” problem by baking an 8x entity height into the distance math, so `VoxelEntities` no longer needs a manual `LODScale` tweak to be visible at normal camera distances. [WorldSphereMod/Code/LOD/LodSelector.cs:34-40](../../WorldSphereMod/Code/LOD/LodSelector.cs#L34-L40) [WorldSphereMod/Code/LOD/LodSelector.cs:54-58](../../WorldSphereMod/Code/LOD/LodSelector.cs#L54-L58)
- `CrossedQuadFoliage`, `WorldspaceUI`, and `ParticleEffects` are already default-on and are visually additive rather than disruptive, so they fit a smooth first-run bundle. [WorldSphereMod/Code/SavedSettings.cs:38-53](../../WorldSphereMod/Code/SavedSettings.cs#L38-L53) [docs/HANDOFF.md:64-69](../../docs/HANDOFF.md#L64-L69)
- `DebugVoxelOutline` is the one default that is still actively noisy; the audit calls it out as a potentially noisy default, so it should not ship as part of the out-of-box experience. [WorldSphereMod/Code/SavedSettings.cs:31-33](../../WorldSphereMod/Code/SavedSettings.cs#L31-L33) [docs/journeys/scratch/savedsettings-roundtrip-audit.md:34-35](../../docs/journeys/scratch/savedsettings-roundtrip-audit.md#L34-L35)

Regression risk for vanilla fans:

- Do **not** force a migration rewrite for existing settings files. `Core.LoadSettings()` preserves loaded JSON values on success, so old installs keep their explicit `false` values unless the user resets defaults. That gives you a safe path: default `VoxelEntities = true` only for fresh configs, while existing “I liked vanilla mode” users stay on their chosen setup. [WorldSphereMod/Code/Core.cs:35-61](../../WorldSphereMod/Code/Core.cs#L35-L61)

Net: the smallest zero-ceremony bundle is `Is3D + VoxelEntities + CrossedQuadFoliage + WorldspaceUI + ParticleEffects`, with the rest left off. The only additional default I would flip immediately is `DebugVoxelOutline` to `false`.
