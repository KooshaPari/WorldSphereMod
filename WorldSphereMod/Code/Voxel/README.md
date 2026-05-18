# Voxel pipeline (Phase 1)

Replaces the upstream `QuantumSprite` billboard path for actors, items, drops,
projectiles, talk bubbles, and status icons with real 3D voxel meshes built on
the fly from each sprite.

- `SpriteVoxelizer.cs` — opaque texel → unit cube, hidden-face culling, pivot
  preserved at `sprite.pivot`. One axis-aligned cube per opaque pixel; greedy
  meshing comes in a follow-up commit (each cube is currently a separate quad).
- `VoxelMeshCache.cs` — LRU cache keyed by sprite instance ID.
- `MeshInstanceBatcher.cs` — `Graphics.DrawMeshInstanced` wrapper, 1023 per
  batch, per-instance tint via `_InstanceColor` shader property.

Wiring into the render pipeline (`QuantumSprites.SourcePatches.calculateactordata3D`
and `calculatebuildindata3D`) is the next commit in the phase. The harness:

```csharp
VoxelMeshCache.Tick();
foreach (Actor a in visible) {
    Mesh m = VoxelMeshCache.Get(a.calculateMainSprite());
    MeshInstanceBatcher.Submit(m, Core.VoxelMaterial, BuildMatrix(a), a.tint);
}
MeshInstanceBatcher.Flush();
```

Requires a lit material that respects `_InstanceColor`; see
`Resources/Shaders/VoxelLit.shader` (Phase 5).
