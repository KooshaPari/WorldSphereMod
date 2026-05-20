# Hexagonal Architecture Proposal for WSM3D

Goal: split the mod into a pure C# core, narrow ports, and replaceable adapters without a rewrite. Keep `Core`, `VoxelRender`, and the Harmony patches as the composition root for now.

## What Is Mixed Today

These files currently blend Unity APIs with domain rules:

- `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs` - sprite sampling, alpha masking, greedy meshing, mesh creation, texture fallback.
- `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs` - cache policy plus `Sprite`, `Mesh`, `Object.Destroy`, and warmup scheduling.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs` - render orchestration, phase gating, batching, diagnostics, and Harmony entry points.
- `WorldSphereMod/Code/LOD/LodSelector.cs` - LOD threshold math and hysteresis, but coupled to `CameraManager` and `SavedSettings`.
- `WorldSphereMod/Code/LOD/FrustumCuller.cs` - cull-test logic wrapped around Unity frustum plane APIs.
- `WorldSphereMod/Code/Rig/HumanoidRig.cs` - bone tables and bone assignment logic mixed with `Matrix4x4`, `Vector2`, and reflection on game frame data.
- `WorldSphereMod/Code/Rig/RigCache.cs` - rigged-mesh cache plus voxelization and Unity mesh lifecycle.
- `WorldSphereMod/Code/Rig/RigDriver.cs` - GPU/CPU skinning adapter logic mixed with bone evaluation.
- `WorldSphereMod/Code/TileMapToSphere.cs` - tile dirty-queue logic mixed with `WorldTile`, `ZoneCamera`, and patching.
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs` - batching policy plus direct Unity draw calls.

## Pure-Domain Candidates

These are the first things worth extracting into `WorldSphereMod/Domain/`:

- `SpriteVoxelizer` internals: alpha mask construction, face culling, greedy rectangle merge, quad emission, texel-to-vertex mapping.
- `MeshSmoother`: adjacency graph build, boundary detection, Laplacian smoothing.
- `LodSelector`: distance-threshold math and 3-frame hysteresis.
- `FrustumCuller`: cull-test math once fed a camera frustum abstraction.
- `HumanoidRig`: bone layout, texel-to-bone segmentation, and animation pose math.
- Small tile-bookkeeping rules from `TileMapToSphere`, once `WorldTile` access is hidden behind a port.

## Proposed Ports

These interfaces live in `WorldSphereMod/Ports/` and only reference domain DTOs:

```csharp
public interface IRenderer
{
    CameraState GetCameraState();
    void Submit(RenderCommand command);
    void Flush();
}

public interface IClock
{
    ulong Frame { get; }
    float DeltaTime { get; }
}

public interface ISavedSettings
{
    bool VoxelEntities { get; }
    bool VoxelMeshSmoothing { get; }
    int VoxelSpriteDepth { get; }
    float VoxelScaleMultiplier { get; }
    float LODScale { get; }
    bool SkeletalAnimation { get; }
}

public interface ISpriteSource
{
    SpriteBitmap Load(string spriteId);
}

public interface ITileMap
{
    int GetHeight(int x, int y);
    bool IsWrapped { get; }
}

public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}

public interface ITelemetry
{
    void Count(string name, int delta = 1);
    IDisposable Measure(string name);
}
```

Use plain DTOs in the domain layer, not `UnityEngine` types. For example: `CameraState`, `RenderCommand`, `SpriteBitmap`, `Vec3`, `Color32`, `MeshData`, and `BonePose`.

## Adapters

- `UnityAdapter` - the real game bridge. Implements the ports using `CameraManager`, `SavedSettings`, `Sprite`, `Mesh`, `Time`, `Debug`, `WorldTile`, and the current batcher.
- `MockAdapter` - the L1 `plate-73` layer. This is the first hex adapter and should drive domain tests without Unity.
- `BridgeAdapter` - HTTP/RPC test harness for remote or replayed inputs.
- `HeadlessAdapter` - no-render runtime for algorithm tests and CI.

## Incremental Migration Plan

1. Add `WorldSphereMod/Domain/` and `WorldSphereMod/Ports/` alongside the current code. Do not move Unity-facing composition yet.
2. Extract `LodSelector` math, `MeshSmoother`, and the greedy meshing core from `SpriteVoxelizer` into domain classes first. Keep current files as thin wrappers.
3. Move `HumanoidRig` data and texel-to-bone logic into domain DTOs. Keep `RigDriver`, `RigCache`, and `VoxelRender` as Unity adapters.
4. Replace direct reads of `Core.savedSettings` inside the extracted code with `ISavedSettings`. Replace camera and clock reads with `IRenderer` and `IClock`.
5. Build `plate-73` as `MockAdapter` for L1 tests. It should provide fake sprites, fake tile heights, deterministic clock ticks, and captured telemetry.
6. After the mock layer is green, add `UnityAdapter` bindings, then `BridgeAdapter`, then `HeadlessAdapter`.
7. Only after the adapters exist should you start shrinking `VoxelRender`, `TileMapToSphere`, and `RigDriver` into orchestration shells.

## Practical Split Order

1. `SpriteVoxelizer` core
2. `LodSelector` and `FrustumCuller`
3. `HumanoidRig` and `RigCache`
4. `MeshSmoother`
5. `TileMapToSphere` dirty-queue logic

This order gives the highest return with the lowest Unity surface area. It also lets `plate-73` validate the domain before any game-specific adapter work lands.
