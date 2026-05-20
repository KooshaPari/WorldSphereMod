# ADR 0016 - Thread-safe MeshInstanceBatcher.Submit via deferred queue

## Status

Accepted (2026-05-19, commit `0deb8b9`).

## Context

`MeshInstanceBatcher` is the shared render submission point for voxel and procgen meshes. Its callers include Harmony postfixes that can run on worker threads, not only on the main thread. Before this change, `Submit()` wrote directly into the shared `_buckets` dictionary, which is not safe to mutate concurrently.

That made the batching path vulnerable to races, corrupted bucket state, and intermittent render failures under parallel render-data generation. The fix also had to preserve Unity's main-thread expectations: `Graphics.DrawMeshInstanced` and the bucket flush loop still belong on the main thread.

## Decision

`MeshInstanceBatcher.Submit()` now does only one thing on producer threads: enqueue an immutable `SubmitRecord` into a `ConcurrentQueue`, then increment a pending-submission counter. `Flush()` drains that queue on the main thread before any draw work, and `HasPendingSubmissions` reads the counter with `Interlocked` instead of scanning buckets.

## Alternatives Considered

| Alternative | Pros | Cons | Why not chosen |
|---|---|---|---|
| Lock `_buckets` inside `Submit()` | Minimal code churn | Heavy contention on hot worker-thread paths; still couples producer threads to mutable render buckets | Too expensive and still awkward under parallel postfix fan-out |
| Replace `_buckets` with a fully concurrent collection | Safer at the storage layer | More complex draw-time batching, no real benefit because drawing still happens on the main thread | Solves the wrong problem |
| Deferred queue + main-thread drain | Thread-safe, simple, preserves current batching model | Adds one extra queue and counter | Chosen |

## Consequences

### Positive

- Worker-thread postfixes can submit render instances without touching shared mutable batch state.
- Main-thread flush remains the only place that mutates bucket lists and issues Unity draw calls.
- `HasPendingSubmissions` becomes O(1) and lock-free.

### Negative

- `Flush()` now has to drain a queue before drawing, so submission is no longer immediately reflected in `_buckets`.
- Debugging the submission path is slightly more indirect because the producer and consumer are separated.

### Neutral

- Batching semantics do not change: instances are still grouped by `(mesh, material)` before draw.

## References

- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:46-123`
- `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs:121-180`
- Commit `0deb8b9` (`fix(phase-1): thread-safe MeshInstanceBatcher.Submit via deferred queue`)
