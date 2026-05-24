# WorldSphereMod tests

Three test tiers, each with one sample test to keep CI honest until real
coverage is added.

## Tiers

### `WorldSphereMod.Tests.Unit`

Pure unit tests. Only references the `WorldSphereAPI` assembly
(`netstandard2.0`, Unity-free), so this project compiles and runs on a
clean CI runner with no WorldBox install.

The current sample (`PublicApiSurfaceTests`) calls
`WorldSphereAPI.Connect(out var api)` to confirm the static entry point is
callable without a host present and returns `(false, null)`.

### `WorldSphereMod.Tests.Integration`

Reserved for tests that exercise the main `WorldSphereMod3D` assembly
against a stubbed or in-process WorldBox harness. **Currently a
placeholder** — the main mod cannot be loaded without UnityEngine plus
WorldBox's `Assembly-CSharp*.dll` reference set, so real integration tests
are deferred until a harness exists.

### `WorldSphereMod.Tests.E2E`

End-to-end / repo-shape checks that don't launch the game. Includes install/
`mod.json` validation, Taskfile/build-contract checks, and
`CiWorkflowInvariantsTests` (CI stub manifest ↔ `WorldSphereMod.csproj`,
shared `Tools/ci-stub-worldbox-refs.sh` usage). See `docs/ci-mod-compile-gap.md`
for why the main mod is not a hosted-runner compile gate.

## Why most of the codebase isn't unit-testable today

`WorldSphereMod` is a Harmony mod hosted inside the WorldBox Unity
runtime. Almost every type either:

- inherits from `UnityEngine.MonoBehaviour`, or
- patches a WorldBox method via `[HarmonyPatch]`, or
- touches `UnityEngine.{Mesh,Texture,Vector3,…}` directly.

None of those types load outside a Unity runtime, and the WorldBox
reference DLLs aren't redistributable. The pragmatic split is:

1. Keep the Unity-free public surface in `WorldSphereAPI/` so it stays
   testable with vanilla xUnit (this is what `Tests.Unit` covers).
2. Defer in-process Unity tests to the integration tier, gated on a
   harness that doesn't exist yet.
3. Use the E2E tier for everything we *can* verify without Unity —
   manifest files, install scripts, asset paths, JSON schemas.

## Running locally

```bash
dotnet build tests/WorldSphereMod.Tests.Unit/WorldSphereMod.Tests.Unit.csproj -c Release
dotnet test  tests/WorldSphereMod.Tests.Unit/WorldSphereMod.Tests.Unit.csproj
```

Integration and E2E projects build and run with the same commands; only
the Unit tier currently has a meaningful assertion.
