# Replace Mesh Instance Batcher Research

Current path: `MeshInstanceBatcher` collects submissions in a `ConcurrentQueue`, drains on the main thread, buckets by `(mesh, material)`, then renders with `Graphics.DrawMeshInstanced` and a per-instance `Graphics.DrawMesh` fallback.

## Findings

- **The current batcher is simple and compatible with the rest of WSM3D.** The queue makes cross-thread submission safe, and the flush path is easy to reason about. The cost is that the renderer still owns CPU-side bucket management and the 1023-instance `DrawMeshInstanced` limit, so large buckets are split into multiple draws.

- **`BatchRendererGroup` is the modern Unity-native replacement, but it is not a drop-in swap.** BRG is the right abstraction when you want Unity to own the render loop integration and you are willing to manage instance metadata, transforms, visibility data, and lifetime through a GPU-oriented pipeline. It is a better fit for a renderer rewrite than for a narrow batcher replacement.

- **`DrawMeshInstancedIndirect` solves the >1023 ceiling, but only for a much more explicit GPU pipeline.** It removes the per-call instance cap, but you must provide argument buffers, instance buffers, and your own culling/packing story. That is useful for very large static or semi-static batches, not for the current submit-and-flush architecture.

## Recommendation

Do **not** migrate the whole batcher to BRG now. The current implementation is adequate for the mod’s architecture and much cheaper to maintain.

Recommended path:

1. Keep `MeshInstanceBatcher` as the default path.
2. Consider a **separate optional indirect path** only for genuinely large, stable buckets where the 1023 split is a measurable bottleneck.
3. Revisit BRG only if we are ready to replace the whole submission model with a GPU-driven renderer.

Bottom line: BRG is technically better in the long run, but the migration cost is high and the current queue + instanced-draw design is the right tradeoff until the project has a strong performance case for a full renderer rewrite.
