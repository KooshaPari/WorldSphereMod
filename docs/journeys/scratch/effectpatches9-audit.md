# EffectPatches9 Audit

Scope: `WorldSphereMod/Code/Fx/EffectPatches9.cs`, `WorldSphereMod/Code/Effects.cs`, `WorldSphereMod/Code/Fx/ParticleEffectLibrary.cs`, `WorldSphereMod/Code/Fx/DecalPool.cs`

## Findings

1. The 3D effect path is routed correctly, but only for the particle/decal branch; it does not replace the upstream sprite effect path globally. `EffectPatches9.OnBegin` just boots the Fx runtime (`EffectPatches9.cs:16-33`). The actual spawn fork is in `Effects.cs:184-214`: `BaseEffectController.GetObject` keeps the original sprite path unless `Core.IsWorld3D` and `ParticleEffectLibrary.Fire(...)` succeeds, and then it disables `sprite_renderer` (`Effects.cs:202-213`). The particle pool itself is explicitly mesh-based, not sprite-based: `ParticleEffectLibrary.BuildPool()` sets `ParticleSystemRenderer.renderMode = Mesh` and assigns `_voxelCubeMesh` (`ParticleEffectLibrary.cs:292-300`). So: yes, 3D particles render as meshes, but upstream sprites still exist as the fallback path and are only hidden per-instance after a successful fire.

2. There is a teardown leak. `ParticleEffectLibrary.Clear()` destroys `_poolRoot` and clears collections, but it only nulls `_voxelCubeMesh`; it never calls `Object.Destroy(_voxelCubeMesh)` (`ParticleEffectLibrary.cs:184-195`). Since `BuildVoxelCubeMesh()` allocates a new `Mesh` each init (`ParticleEffectLibrary.cs:225-252`), re-init leaks the previous mesh object. `DecalPool` also creates a fresh `new Material(shader)` per quad (`DecalPool.cs:130-137`) and `Clear()` destroys only the pool GameObjects, not those material instances (`DecalPool.cs:95-108`), so that is a second likely leak path.

3. The Begin/Finish patches are mostly idempotent, but re-init behavior is asymmetric. `OnBegin` guards on `Core.IsWorld3D` and `_root == null`, then calls `ParticleEffectLibrary.Init()` and `DecalPool.Init(_root)` (`EffectPatches9.cs:20-33`). `ParticleEffectLibrary.Init()` is one-shot because `_initialized` short-circuits (`ParticleEffectLibrary.cs:83-96`); `DecalPool.Init()` is self-healing because it calls `Clear()` when already initialized (`DecalPool.cs:45-53`). `OnFinish` is safe to repeat: both `Clear()` methods tolerate null/empty state and `_root` is nulled after destroy (`EffectPatches9.cs:40-45`, `ParticleEffectLibrary.cs:184-195`, `DecalPool.cs:95-108`). On a normal re-init after Finish, everything comes back up; on a second Begin without Finish, decals are torn down and recreated, but the particle library is left untouched.

## Verdict

No routing bug in the mesh path itself. The main issues are cleanup: leaked `_voxelCubeMesh`, likely leaked decal materials, and asymmetric re-init between particle and decal pools.
