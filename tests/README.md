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

Unity-free repo integration tests: journey manifest paths, `mod.json`/install
contracts, bake tooling, visual-regression harness preflight, and
`Tools/wsm3d.ps1` journey trace invariants. Gated in `test-gate.yml` and
`task test-integration`. Does not launch WorldBox.

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

## Live verification

**Entry point:** [`docs/live-verification.md`](../docs/live-verification.md)

| Gate | What runs from this repo |
|------|---------------------------|
| Programmatic | `dotnet test` (all three tiers locally; CI: unit + integration in `test-gate.yml`) + `pwsh Tools/wsm3d.ps1 journey verify -Id <id>` (mock) + optional SSIM vs `docs/journeys/phase-previews/` (threshold 0.95 — see harness design doc) |
| Agentic (local only) | `Tools/wsm3d-playcua` scenarios + OmniRoute vision (`OMNROUTE_BASE_URL`, `OMNROUTE_VISION_COMBO`, dashboard API key) + bridge save/load checklist in `docs/journeys/scratch/bridge-scene-transition-known-issue.md` |

## Running locally

```bash
task test-all
# or per tier:
dotnet test tests/WorldSphereMod.Tests.Unit/
dotnet test tests/WorldSphereMod.Tests.Integration/
dotnet test tests/WorldSphereMod.Tests.E2E/
```

Journey mock preflight (no game):

```powershell
pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase1
```
