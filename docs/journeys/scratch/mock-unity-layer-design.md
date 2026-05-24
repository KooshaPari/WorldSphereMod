# Mock Unity Layer Design

Goal: make the hot logic paths testable in `tests/WorldSphereMod.Tests.Unit/` with sub-second iteration, without launching WorldBox or referencing the real Unity DLLs.

The current coverage gap is real: the unit suite is still source-text/reflection-only ([tests/WorldSphereMod.Tests.Unit/WorldSphereMod.Tests.Unit.csproj:1-24](../../../tests/WorldSphereMod.Tests.Unit/WorldSphereMod.Tests.Unit.csproj#L1-L24)), while the bug-prone render/cache paths live in Unity-coupled code such as `VoxelRender` ([WorldSphereMod/Code/Voxel/VoxelRender.cs:16-20](../../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L16-L20), [WorldSphereMod/Code/Voxel/VoxelRender.cs:306-399](../../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L306-L399)), `BuildingProcRender` ([WorldSphereMod/Code/ProcGen/BuildingProcRender.cs:19-139](../../../WorldSphereMod/Code/ProcGen/BuildingProcRender.cs#L19-L139)), `LodSelector` ([WorldSphereMod/Code/LOD/LodSelector.cs:24-40](../../../WorldSphereMod/Code/LOD/LodSelector.cs#L24-L40)), `ImpostorBillboard` ([WorldSphereMod/Code/LOD/ImpostorBillboard.cs:22-25](../../../WorldSphereMod/Code/LOD/ImpostorBillboard.cs#L22-L25), [WorldSphereMod/Code/LOD/ImpostorBillboard.cs:100-186](../../../WorldSphereMod/Code/LOD/ImpostorBillboard.cs#L100-L186)), `MeshSmoother` ([WorldSphereMod/Code/Voxel/MeshSmoother.cs:11-89](../../../WorldSphereMod/Code/Voxel/MeshSmoother.cs#L11-L89)), and `MeshInstanceBatcher` ([WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:46-133](../../../WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs#L46-L133)).

## 1) Fake surface to provide

Keep the fakes minimal and shaped to the code we actually exercise.

Unity types:
- `Vector3`, `Vector2`, `Vector2Int`, `Quaternion`, `Matrix4x4`, `Color`, `Color32`, `Rect`, `RectInt`, `Bounds`, `Ray`
- `Material` and `Mesh` stubs with just the properties/methods used by the tests: names, vertex/triangle data, `RecalculateBounds`, `RecalculateNormals`, `Set*`, `Get*`
- `Graphics` as a no-op draw sink that records draw calls
- `Sprite` with `texture`, `rect`, `textureRect`, `uv`, `pixelsPerUnit`, `name`, `GetInstanceID()`
- `Camera` with a simple frustum stub: `fieldOfView`, `transform.position`, `enabled`, and an overridable `ViewportPointToRay`/`ScreenToViewportPoint`
- `MaterialPropertyBlock` as a dictionary-backed property bag
- `Shader`, `Object`, `Debug`, `RenderSettings`, `Time` only if a test touches those code paths

WorldBox types:
- `Actor`, `Building`, `BuildingManager`, `ActorManager`
- `render_data` containers with the exact fields that matter to the hot paths: `positions`, `scales`, `rotations`, `colors`, `main_sprites`, `flip_x_states`, and for actors `has_normal_render`
- `Constants.ZDisplacement` and any other constants used by cull-lift logic
- `Tools.To3DTileHeight`, `Tools.To3D`, `Tools.RotateToCamera`, and any `World`/tile helpers needed by the tested code path
- Minimal `Core.savedSettings` shape so the code can read flags like `LODScale`, `VoxelMeshSmoothing`, `SmoothingIterations`, `ProfilerDump`, `VoxelScaleMultiplier`

The render-data map already shows the important asymmetry: actor data has `has_normal_render`, building data does not ([docs/render-data-fields.md:8-30](../../../docs/render-data-fields.md#L8-L30), [docs/render-data-fields.md:36-78](../../../docs/render-data-fields.md#L36-L78)). That is the main reason the fake `render_data` should be data-shaped, not behavior-heavy.

## 2) Compilation strategy

Do not try to redirect the main mod onto fake Unity assemblies. The real mod project should keep its existing Unity references ([WorldSphereMod.csproj:29-116](../../../WorldSphereMod.csproj#L29-L116)). The test project should stay Unity-free and compile against a test-support layer instead ([tests/WorldSphereMod.Tests.Unit/WorldSphereMod.Tests.Unit.csproj:18-23](../../../tests/WorldSphereMod.Tests.Unit/WorldSphereMod.Tests.Unit.csproj#L18-L23)).

Recommended wiring:
- Add a `WorldSphereMod.TestSupport` project, net8.0, referenced only by the unit test project.
- Put fake Unity/WorldBox namespaces there under a repo-defined symbol such as `WSM3D_TESTS`.
- Keep mod files on real Unity symbols by default; use `#if UNITY_2019_4_OR_NEWER` only where the same source file needs a Unity-specific branch.
- For shared logic, split into `partial` classes or small helper classes so the “core” file is pure C# and the Unity binding stays thin.

Practical rule:
- `UNITY_2019_4_OR_NEWER` is for the mod build.
- `WSM3D_TESTS` is for the fake environment.
- Avoid assembly-reference redirection; it is more brittle than a deliberate shim project and makes CI failure modes harder to read.

## 3) First extraction targets

1. `CullLift` helper: a pure method that turns a raw `Vector3` into the lifted position used by both cull and TRS.
2. `LodSelectorCore`: take camera state, `LODScale`, and world position as inputs, return a `LodTier`.
3. `BuildingRulesRegistryCore`: separate routing/memoization from `ProcGenCache` invalidation.
4. `MeshSmootherCore`: expose the smoothing kernel so the `savedSettings` gate can be tested without Unity instantiation.
5. `ImpostorBillboardCore` / `MeshInstanceBatcherCore`: keep atlas and queue behavior testable without `Graphics.DrawMesh*`.

## 4) Sample cull-lift test

This test should prove the bug class without launching the game:

```csharp
[Fact]
public void Building_cull_and_trs_use_the_lifted_position()
{
    var raw = new Vector3(12, 18, 0);
    var lifted = new Vector3(12, 18, 5);
    var seenCullPos = default(Vector3);
    var seenTrsPos = default(Vector3);

    var submitted = CullLift.Apply(
        raw,
        lift: p => lifted,
        isVisible: p =>
        {
            seenCullPos = p;
            return p.z >= 5;
        },
        buildTrs: p =>
        {
            seenTrsPos = p;
            return Matrix4x4.TRS(p, Quaternion.identity, Vector3.one);
        });

    Assert.True(submitted);
    Assert.Equal(lifted, seenCullPos);
    Assert.Equal(lifted, seenTrsPos);
}
```

The production equivalent should be asserted against `BuildingProcRender.EmitMeshes` ([WorldSphereMod/Code/ProcGen/BuildingProcRender.cs:42-49](../../../WorldSphereMod/Code/ProcGen/BuildingProcRender.cs#L42-L49), [WorldSphereMod/Code/ProcGen/BuildingProcRender.cs:95-109](../../../WorldSphereMod/Code/ProcGen/BuildingProcRender.cs#L95-L109), [WorldSphereMod/Code/ProcGen/BuildingProcRender.cs:123-130](../../../WorldSphereMod/Code/ProcGen/BuildingProcRender.cs#L123-L130)) and the same pattern in `VoxelRender` ([WorldSphereMod/Code/Voxel/VoxelRender.cs:309-394](../../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L309-L394), [WorldSphereMod/Code/Voxel/VoxelRender.cs:461-518](../../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L461-L518)).

## 5) First 5 tests to write

1. `BuildingProcRender` cull-lift: raw `rd.positions[i]` is lifted before both `FrustumCuller.IsVisible` and `Matrix4x4.TRS`.
2. `VoxelRender` actor path: lifted cull position and final TRS position match, and `has_normal_render[i]` is cleared only after a successful submit.
3. `LodSelector`: camera FOV + `LODScale` + `_entityHeight` produce the expected tier cutoffs, and hysteresis flips only after 3 stable frames.
4. `BuildingRulesRegistry`: `tree_` / `rock_` prefixes memoize exactly once, and `Register` invalidates the cached route.
5. `MeshInstanceBatcher`: background-thread `Submit` enqueues to the concurrent queue, `Flush` drains on the main-thread path, and the pending counter returns to zero.

Follow-up after those five: `ImpostorBillboard` LRU eviction + hit/miss counters, then `MeshSmoother` smoothing on/off and non-destructive copy semantics.
