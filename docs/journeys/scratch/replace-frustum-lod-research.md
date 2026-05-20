# Replace Frustum/Lod Research

Scope: `ActorVoxelEmit` / `BuildingProcRender` Postfixes that consume WorldBox `render_data[]` arrays, not a GameObject hierarchy.

## Findings

1. **Unity `CullingGroup` is the closest built-in, but still awkward here.**
   It is designed for per-object frustum and distance culling with callbacks, using bounding spheres and a camera-driven group. That maps to the problem shape, but not to our ownership model: we do not own one stable GameObject per actor/building, and our render work happens after WorldBox fills `render_data` in a Postfix. To use `CullingGroup` cleanly, we would need a persistent proxy table from `render_data` indices to spheres/callback state, plus update/reconcile logic every frame. Good for a future adapter, not a drop-in replacement. Sources: [CullingGroup docs](https://docs.unity3d.com/ScriptReference/CullingGroup.html).

2. **Unity `LODGroup` is a poor fit for this pipeline.**
   `LODGroup` is a component-driven, renderer-by-renderer system for GameObjects. That is the opposite of our current model: we pick tiers inside a Postfix while iterating `render_data.positions/scales/rotations/main_sprites`. Adopting `LODGroup` would require spawning proxy GameObjects or reifying the WorldBox render arrays into a hierarchy we do not control, which is high integration cost and fights the Postfix design. Sources: [LODGroup docs](https://docs.unity3d.com/Manual/class-LODGroup.html).

3. **Job System + Burst are useful only as an implementation detail, not as a replacement API.**
   Burst can accelerate the math in a culling/LOD kernel, and jobs are a good fit for bulk per-instance distance/frustum evaluation. But the output still has to come back to the main thread before we mutate `render_data`, submit meshes, or suppress sprites. In this mod, the main cost is not the math kernel; it is the bridge from WorldBox arrays to Unity rendering calls. A Burst job could replace the inner loop later, but it does not solve the architecture mismatch. Sources: [Job System manual](https://docs.unity3d.com/Manual/JobSystem.html), [Burst package docs](https://docs.unity3d.com/Packages/com.unity.burst@latest).

4. **GPU-driven culling is the highest-performance path, but it is a rewrite.**
   Compute-shader culling plus indirect drawing is the right answer when you own the full render pipeline and can feed GPU buffers directly. We do not: current rendering is batch-submit from Harmony Postfixes after WorldBox precalculates arrays. Moving to GPU culling would mean new buffer management, GPU-visible instance packing, and a draw path that bypasses the current `MeshInstanceBatcher`/`VoxelRender` submission flow. That is suitable only if we deliberately abandon the Postfix-first model. Sources: [Compute shaders manual](https://docs.unity3d.com/Manual/ComputeShaders.html).

## Recommendation

Keep the current custom `FrustumCuller` + `LodSelector` path as the production design, but refactor it into a data-only core that can later be jobified/Burst-compiled.

Why:
- It matches the actual integration surface: `render_data[]` in `ActorVoxelEmit` and `BuildingProcRender`, not GameObject ownership.
- It preserves the existing suppression hooks (`has_normal_render` for actors, `scales[i]=0` for buildings).
- It avoids proxy hierarchy churn and callback bookkeeping that `CullingGroup`/`LODGroup` would force.

If we ever need a Unity-native bridge, `CullingGroup` is the only candidate worth trialing, and only behind a stable proxy layer for a subset of entities. The other two options are better treated as implementation techniques, not replacements.
