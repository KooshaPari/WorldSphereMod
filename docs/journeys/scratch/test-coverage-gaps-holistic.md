# WSM3D holistic test coverage gaps

Plate-08/13/14/15/16 already cover the main layers we expected: unit, integration, e2e, visual, and perf. The remaining gaps are mostly about input space, branch sensitivity, and compatibility guarantees.

## Missing test types

- Property-based tests (`FsCheck`) for invariants
  - Good fit for geometry and settings invariants that should hold across many inputs, not just a fixed fixture.
  - Example: every voxelized sprite mesh should have a positive vertex count; caches should not produce negative/empty geometry; round-trip settings should preserve stable booleans and numeric bounds.
  - This is the best match for regressions in voxel generation, render suppression, and cache behavior where a single handcrafted case can miss edge cases.

- Fuzz testing on `BridgeRPC` + `SavedSettings` JSON parsing
  - Good fit for malformed JSON, partial payloads, version skew, and adversarial local input.
  - We already know the settings loader is permissive and falls back on parse failure, so fuzzing is the easiest way to prove it stays safe instead of silently accepting bad shapes.
  - This is the highest-value gap for the bridge/config surface because it exercises crashy parser edges, not just the happy path.

- Mutation testing (`Stryker.NET`)
  - Good fit for finding assertions that are too weak and branches that are only superficially covered.
  - In this repo, a lot of tests validate static text, reflection shape, or one “golden” behavior. Mutation testing would tell us where those checks fail to detect logic flips in phase gating, fallback paths, and cache-hit logic.
  - This is the best meta-gap: it does not add new behavior coverage directly, but it tells us where the current suite is lying by omission.

## Top 3 gaps for bugs we have actually been exposed to

1. **Fuzz testing on `BridgeRPC` + `SavedSettings` JSON**
   - Most likely to catch the parser/config failures and malformed-input paths we have been circling.
   - It directly targets the bridge and settings boundary, which is where bad local input, version mismatch, and recovery behavior live.

2. **Property-based tests for mesh and state invariants**
   - Most likely to catch off-by-one, empty-output, and invalid-state regressions in voxelization and render/cache code.
   - These are the bugs that slip through fixed fixtures because they only appear for specific generated shapes or rare state combinations.

3. **Mutation testing**
   - Most likely to expose “covered but not really covered” code, especially fallback branches and phase-gate logic.
   - This matters here because several existing tests are structural rather than behavioral, so they can pass while important branches remain effectively unverified.

## Lower-priority next gaps

- Snapshot/golden testing for shader bake output
  - Valuable once the bake pipeline stabilizes, but it is more of a regression lock than a bug-discovery tool.

- Contract testing for `WorldSphereAPI` v2 compatibility
  - Important for release discipline and downstream consumers, but narrower than the parser/invariant gaps above.

- Load testing with 10k actors and GC assertions
  - Important for perf budgeting, but plate-14 already covers the perf axis; this is a scale-up of that concern, not a new category.

- Chaos testing for kill/restart mid-postfix
  - Useful for teardown/leak detection, but it is harder to automate and less directly tied to the bugs surfaced in this session than the three above.

## Bottom line

If we only add three new categories next, make them:

1. `BridgeRPC` + `SavedSettings` fuzzing.
2. FsCheck property tests for mesh/state invariants.
3. Stryker mutation testing to prove the current suite is actually sensitive.

