# Concurrent collections research for WSM3D

Scope: `MeshInstanceBatcher`, `VoxelMeshCache`, `ImpostorBillboard`, `BuildingRulesRegistry`.

## Bottom line

The best “less locking” win is `BuildingRulesRegistry`. The best “less overhead” win is `MeshInstanceBatcher` if we simplify its producer path. `VoxelMeshCache` is already dominated by Unity work, and `ImpostorBillboard` is effectively single-threaded.

## Findings

| Site | Current shape | Best fit | Why |
|---|---|---|---|
| `MeshInstanceBatcher` | `ConcurrentQueue<SubmitRecord>` plus main-thread bucket append | Plain `List<T>`/`Dictionary` behind a dedicated lock, or direct main-thread fast path | The hot path is submission, not the drain. `ConcurrentQueue` is fine for MPSC, but if most calls are on the main thread we pay thread-safe-queue overhead for little benefit. `ConcurrentBag` is the wrong shape because flush order matters. |
| `VoxelMeshCache` | Coarse `lock` around `Dictionary` + warm queue + eviction | `System.Threading.Lock` only after a `net9`/C# 13 upgrade; otherwise keep the lock | The lock protects multiple related structures and the expensive Unity build already runs outside the lock. This is not a good lock-free candidate. `Interlocked.CompareExchange` would get ugly fast because cache, frame stamp, eviction, and destroy queues must stay consistent. |
| `ImpostorBillboard` | Plain `Dictionary` on a mostly main-thread path | No change needed now | There is no real contention story here. If this ever becomes multi-threaded, a simple lock is still more appropriate than a lock-free design. |
| `BuildingRulesRegistry` | `ConcurrentDictionary<string, BuildingRules>` with hot `Resolve` reads and rare writes | `ImmutableDictionary` root + `Interlocked.CompareExchange` copy-on-write | `Resolve` is the hot path and `Register`/`Invalidate` are rare. Reads can become a plain dictionary lookup on an immutable snapshot, which avoids per-call concurrent-dictionary overhead. This is the clearest read-mostly case. |

## Primitive-by-primitive

- `System.Threading.Lock`:
  - Good future replacement for narrow critical sections on .NET 9 / C# 13.
  - Not usable in this repo today because `WorldSphereMod.csproj` targets `net48`.
  - Most useful where we still need locking but want a lower-overhead dedicated lock object.
- `ImmutableDictionary` copy-on-write:
  - Best for `BuildingRulesRegistry`.
  - Poor fit for `VoxelMeshCache` because that cache mutates, evicts, and drains queues frequently.
- `ConcurrentBag`:
  - Not a fit for these sites. It is optimized for same-thread produce/consume and unordered access.
  - `MeshInstanceBatcher` needs ordering, not bag semantics.
- Custom `Interlocked.CompareExchange`:
  - Best used as the update mechanism for an immutable snapshot, not as a hand-rolled lock-free hash table.
  - Good for `BuildingRulesRegistry`; risky and overcomplicated for `VoxelMeshCache`.

## Recommendation

1. Replace `BuildingRulesRegistry` with immutable snapshot + CAS if profiling confirms `Resolve` is hot.
2. Simplify `MeshInstanceBatcher.Submit` so main-thread submissions bypass thread-safe queueing.
3. Leave `VoxelMeshCache` as-is unless the repo moves to `net9` and profiling shows the lock itself is measurable.
4. Do not use `ConcurrentBag` for any of these sites.
