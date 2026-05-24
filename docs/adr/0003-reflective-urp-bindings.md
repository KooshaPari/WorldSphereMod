# ADR 0003 — Reflective URP bindings

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-05-17 |
| Deciders | KooshaPari |

## Context

`ShadowCascadeConfig` (Phase 5) writes shadow cascade properties on the active `UniversalRenderPipelineAsset`. `PostFxController` (Phase 9) creates a global `Volume` with `Bloom`, `ColorAdjustments`, and `Vignette` overrides. Both touch URP runtime types.

WorldBox's `worldbox_Data/Managed/` ships every Unity `UnityEngine.*Module.dll` but **no URP runtime DLLs**. A direct `<Reference Include="Unity.RenderPipelines.Universal.Runtime"/>` in `WorldSphereMod.csproj` fails to resolve.

Three options:

1. **Add the URP DLLs to `Assemblies/`** and ship them. Distributes Unity binaries we don't author; licensing unclear; ties us to a specific URP version.
2. **Probe `RuntimeType.GetType("UnityEngine.Rendering.Universal.*")` at runtime.** Compiles without URP at build time. Falls back gracefully if WorldBox is ever updated to a different SRP. Property names change across URP versions — we can fall back per-property.
3. **Skip URP-specific features.** Phase 5 shadow cascades and Phase 9 post-FX disappear from the fork. Visually weaker.

## Decision

Option 2. Both `ShadowCascadeConfig` and `PostFxController` use `Type.GetType` + `AppDomain.CurrentDomain.GetAssemblies()` to locate URP types, then write each property through reflection wrapped in `try/catch` + `Debug.LogWarning` so a single missing field can't crash a session.

## Consequences

- **Positive.** Build doesn't depend on a URP package reference. The fork tolerates URP version drift across WorldBox updates — only fully renamed APIs break a given feature, and they fail closed (log + no-op).
- **Negative.** Slower than direct property access (~µs per call) — but each call is once-per-world or once-per-frame on the main thread, well below noise.
- **Negative.** Loses type safety. If WorldBox switches away from URP entirely (HDRP, custom SRP), both controllers silently no-op and Phase 5 / 9 visual features disappear. Documented in the warning logs.

## References

- `WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs`
- `WorldSphereMod/Code/Fx/PostFxController.cs`
- ADR 0002 — same Unity-version uncertainty motivates both decisions
