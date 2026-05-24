# `Tools.GetTileHeightSmooth` caching analysis

I found 3 direct call sites in `WorldSphereMod/Code/`, and none of them show a clear repeated-query pattern that would justify a shared cache in `Tools.cs` today.

## Call sites

1. `WorldSphereMod/Code/Effects.cs:77-96,99-119`
   - `EffectManager.UpdateEffect` takes one of two exclusive paths:
     - non-separate sprite: inline placement at `Effects.cs:83-86`
     - separate sprite: `UpdateSeperatedSprite` at `Effects.cs:79-81`, which does the lookup at `Effects.cs:94-97`
   - `UpdateEffect(BaseEffect __instance)` is invoked once per `BaseEffect.update` / `Cloud.update` via the Harmony postfix at `Effects.cs:253-269`.
   - (a) Same position queried multiple times in the same frame? Not from this code path. Each update does one smooth-height query for the effect position, and the two branches are mutually exclusive.
   - (b) Can the result be memoized at the caller? Only if a future refactor needs the same height twice inside one branch. Right now there is no duplicate to hoist.

2. `WorldSphereMod/Code/QuantumSprites.cs:411-421`
   - The postfix only adds tile height for non-highlight sprites.
   - (a) Same position queried multiple times in the same frame? No evidence in this method. `pPos` is adjusted once and the result is returned.
   - (b) Can the result be memoized at the caller? Not meaningfully here; there is only one lookup.

3. `WorldSphereMod/Code/Tools.cs:263-269` wrappers
   - `To3DTileHeight(Vector2)` and `To3DTileHeight(Vector3, bool)` each call `GetTileHeightSmooth` once, then immediately convert to 3D.
   - These are not extra independent call sites, but they explain why many higher-level renderers only have to hoist the `Vector2` once and call the wrapper once.

## Recommendation

Do **not** add a thread-local, frame-keyed `Dictionary<Vector2, float>` cache in `Tools.cs` yet.

Reason:
- The traced hot call sites are single-use lookups, not repeated same-frame re-queries.
- A global cache would add key quantization, invalidation, and lookup overhead on every call, but the current code does not show reuse strong enough to amortize that cost.
- If a future caller does need the same height more than once in a frame, caller-side hoisting is the better first fix: store the `float height` locally and pass it through the rest of the update path.

## Bottom line

Caller-side hoisting is enough for the current codebase. Add a `Tools.cs` cache only if a later profile proves the same `Vector2` is hit multiple times across distinct callers in the same frame.
