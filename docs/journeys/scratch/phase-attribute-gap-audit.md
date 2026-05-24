# Phase attribute gap — found via /phase/<name> bridge endpoint, no screenshots

`/phase/<name>` returns `{ok, status, enabled, patchedTypes}` for each
SavedSettings phase flag. After loading a world and curling all 10
phases, 4 return `unknown_phase` despite having SavedSettings flags
+ runtime code:

| Phase | Bridge result | Has flag? | Has [Phase] attr? |
|---|---|---|---|
| VoxelEntities | enabled, 2 patches | ✓ | ✓ |
| ProceduralBuildings | disabled, 1 | ✓ | ✓ |
| CrossedQuadFoliage | enabled, 2 | ✓ | ✓ |
| MeshWater | disabled, 5 | ✓ | ✓ |
| **HighShadows** | **unknown_phase** | ✓ | ✗ |
| **SkeletalAnimation** | **unknown_phase** | ✓ | ✗ |
| WorldspaceUI | enabled, 1 | ✓ | ✓ |
| **DayNightCycle** | **unknown_phase** | ✓ | ✗ |
| **PostFX** | **unknown_phase** | ✓ | ✗ |
| ParticleEffects | enabled, 3 | ✓ | ✓ |

The 4 broken phases:
- HighShadows (Lighting/SunDriver, ShadowCascadeConfig)
- SkeletalAnimation (Rig/RigDriver, RigCache)
- DayNightCycle (Lighting/TimeOfDay, ProceduralSky)
- PostFX (Fx/PostFxController, PostFx/*)

These run via `if (Core.savedSettings.X) { ... }` checks at runtime
but lack `[HarmonyPatch] + [Phase(nameof(SavedSettings.X))]` on the
actual Postfix classes that PhasePatchManager scans. Fix: add the
attribute pair to at least one type per phase so the manager
inventories them.

Found in <60s via:
  curl http://127.0.0.1:8766/phase/HighShadows

No screenshot interpretation required.

