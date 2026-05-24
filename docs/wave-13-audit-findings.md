# Wave 13 — 12 codex-spark phase audit findings

**Dispatched:** 2026-05-19, 18:19 UTC. **Model:** gpt-5.3-codex-spark, medium reasoning. **Cycle:** 12 agents in parallel.

## Real bugs (newly surfaced)

### Phase 5 — sun-horizon clamp (wave-03)

`SunDriver.TimeOfDayToEuler(float hours)` at `WorldSphereMod/Code/Lighting/SunDriver.cs:85` clamps rotation to `[-90°, 90°]`. The directional light never goes below the horizon after midday — afternoon and evening hours map to the same `+90°` zenith. The 24h cycle is visibly correct only in the morning half. Severity: medium (visual correctness when Phase 8 DayNightCycle is on).

**Tracked as task #141.**

### Phase 3 — empty-mesh Submit (wave-01)

`CrossedQuadMesher.Build` at `WorldSphereMod/Code/Foliage/CrossedQuadMesher.cs:19-24, 127` returns a name-tagged empty `Mesh` when the source sprite texture is non-readable. `FoliageTileRender.Prefix:87-95` submits the empty mesh without checking `vertexCount > 0` — the draw call lands but produces no geometry. Wastes CPU/GPU per tile. Fix: guard `Submit(mesh, mat, trs, color)` on `mesh.vertexCount > 0` before the call.

**Tracked as task #142.**

## Phase health verdicts

| Phase | Verdict | Notes |
|---|---|---|
| 3 (Foliage) | ✅ healthy + minor waste | Prefix correctly wired; null-sprite guards present; empty-mesh issue tracked as #142. |
| 4 (Water) | ✅ wired, ⚠️ shader fallback | `WaterRender` + `WaterSurface` paths complete. `Resources.Load<Shader>("Shaders/WaterGerstner")` with built-in shader fallback if not present. Full fidelity blocked on the Unity 2022.3 AssetBundle bake per ADR-0002. |
| 5 (Lighting) | 🐛 horizon clamp | See #141. |
| 6 (Skeletal) | ⚠️ GPU bypassed | `RigDriver.SubmitSkinnedActor` runs when `SkeletalAnimation` flag + non-Impostor tier, but `_gpuOK = false` hardcoded at `RigDriver.cs:91` — always CPU bind-pose fallback. Pre-existing per ADR-0006. |
| 7 (Worldspace UI) | ✅ healthy | `WorldspaceUI` is the only gate; sub-components (`NameplateWorld`, `HealthBar`, `DamagePopup`) attach only via `WorldUIRenderer.RegisterActor`. |
| 8 (Day/Night) | ✅ healthy | `TimeOfDay` `EnsureCreated` runs when `DayNightCycle || FogDensity != 0`. `Update()` reads `MapBox.world_time` via reflection with fallback. |

## Doc updates

- **CHANGELOG draft** for v2.0.0-alpha.6 captured in `.codex-wave-11.out` — covers AutoTest peak telemetry, Flush-gate widening, ADR-0013/-0014, tile-refresh, counter reset.
- **README + HANDOFF audits** (waves 09/10) — still running at time of writing; merge into the alpha.6 docs PR when they land.

## Cleanup applied (wave-12)

Removed 9 diag counters from `BuildingProcRender.cs` (commit `1215419`) — all served their purpose finding the 2D-cull bug documented in ADR-0012.

## Linked

- ADR-0011 (Phase 1 visibility postmortem)
- ADR-0012 (Phase 2 procedural diagnosis methodology)
- ADR-0013 (Flush gate silent-drop bug)
- ADR-0014 (AutoTest persist + tile-dirty methodology)
