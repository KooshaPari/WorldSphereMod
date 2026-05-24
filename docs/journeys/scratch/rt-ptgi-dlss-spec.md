# WSM3D RT/PTGI/DLSS Integration Spec

## Goal

Define a practical path for advanced lighting in **built-in** Unity (current mod baseline) without forcing a full pipeline rewrite immediately.

## Decision target

1. **DXR Ray Tracing (HDRP only)**
2. **PTGI via screen-space approximation**
3. **DLSS / FSR3 / XeSS**
4. **SSAO + SSR + SSGI screen-space stack (built-in alternatives)**

---

## 1) Option analysis in WSM3D context

### 1.1 DXR Ray Tracing (HDRP-only)

- **Pipeline**: Unity **HDRP only**.
- **Compatibility**: **Does not work in built-in**.
- **Cost**: Very high migration cost because this requires moving render architecture away from built-in, replacing major camera/post-process/lighting paths, and revalidating world-material assumptions.
- **Engineering impact**:
  - Large render-code churn.
  - New asset and shader compatibility effort.
  - Long latency for gameplay-level value.
- **When to use**: only if the project is committed to HDRP and can absorb a broad migration.

### 1.2 PTGI via screen-space approximation

- **Pipeline**: Implementable in built-in using depth/normal buffers and sampling pass structure.
- **Compatibility**: **Works in built-in** and can run today.
- **Cost**: Moderate (new screen-space pass + cache tuning + denoise/occlusion heuristics).
- **Benefit**: Adds global-lit feel where DXR-equivalent quality is impossible, with lower engineering and platform risk.
- **Tradeoff**: Not physically accurate GI; still frame-costly at high resolutions and view distances, needs tuning.

### 1.3 DLSS / FSR3 / XeSS

- **DLSS**: requires NVIDIA Streamline plugin path in Unity and is generally tied to SRP usage (HDRP/URP), not a built-in baseline solution.
- **FSR3**: mostly URP/HDRP story in modern tooling; practical built-in path is weak/undefined for this target.
- **XeSS**: similarly plugin-driven and SRP-biased; not a built-in-first solution.
- **Cost**: Medium-to-high due to platform gating + vendor plugin/licensing/legal integration and SRP requirements.
- **Benefit**: Strongest upside is upscaling perf margin, not direct GI/indirect-light quality.

### 1.4 SSAO + SSR + SSGI in built-in

- **Pipeline**: Built-in-friendly.
- **Scope**:
  - SSAO via existing post stack conventions.
  - SSR as screen-probe reflection path.
  - SSGI as screen-space bounce approximation for local cavities and contact light.
- **Cost**: Lowest of four options, especially for incremental rollout.
- **Benefit**: Immediate visual lift for depth, contact occlusion, and ambient bounce realism at manageable risk.
- **Tradeoff**: Same screen-space limits (occlusion bleed, edge artifacts, no true multi-bounce physics); still good “shipping first win.”

---

## Recommended conclusion

**Ship option 4 first** and defer option 3 to **Phase 11 only if/when URP migration is underway**. Keep option 2 as the next progressive enhancement inside built-in.

- **Immediate target (now):** `SSAO + SSR + SSGI` in built-in.
- **Follow-up (if quality/perf budget allows):** evaluate lightweight PTGI approximation in built-in only for selected high-cost scenes.
- **Strategic gate:** full RT/DLSS stack is **not a built-in target**; it is a Phase 11 migration outcome.

---

## 2) Phased plan

### Phase 0: Baseline and guardrails (current branch scope)

1. Capture baseline render-time and memory baselines (same maps, same camera presets, same chunk visibility).
2. Add a feature toggle in `SavedSettings` for the new stack (`BuiltInLightingScreenSpace`), default `false`.
3. Add profile-level profiling markers around post chain (`CameraDepthNormals`, `SSAO`, `SSR`, `SSGI`), with in-game toggles.

### Phase 1: Built-in SSAO + SSR + SSGI implementation

1. Add SSAO pass in a conservative budget mode with scene-scale radius/strength defaults.
2. Add SSR pass with horizon/ray-step bounds to avoid runaway cost in foliage-heavy scenes.
3. Add SSGI pass with temporal/blur denoise step and distance caps.
4. Add integration order policy:
   - `Depth/Normals -> SSAO -> SSR -> SSGI -> tone map`.
5. Implement per-biome quality presets (`Low/Standard/Quality`), all defaulting to safe/performance-first.
6. Add compatibility checks for null/low-end GPUs and automatic fallback to SSAO only.
7. Gate all enabled effects behind `SavedSettings.BuiltInLightingScreenSpace` and default to OFF in shipped build.

### Phase 2: Validation and rollout (same phase)

1. Visual passes on representative biomes:
   - open terrain + caves
   - dense tree/foliage zones
   - water-edge and structure-heavy sections
2. Compare FPS/alloc behavior to baseline; define acceptance bounds.
3. Fix regressions before phase close:
   - seam flicker on wrapped worlds
   - alpha-heavy impostors edge artifacts
   - reflection popping on quick camera transitions
4. Mark phase complete once default OFF ships with docs and toggles, and opt-in path is stable.

### Phase 11: URP migration prep + RT/DLSS decision gate

1. Confirm world renderer architecture migration tasks are complete enough for SRP features.
2. In PR for phase 11, re-open RT/DLSS feasibility with concrete constraints:
   - target hardware matrices
   - plugin procurement/support status
   - content compatibility and shader rewrite budget
3. Re-evaluate:
   - true DXR RTGI/PTGI (HDRP)
   - DLSS via Streamline
   - FSR3/XeSS upscaling path
4. Ship only if migration and acceptance tests pass; keep previous built-in screen-space path as fallback option.

---

## 3) Decision matrix (ship priority)

| Option | Built-in immediate? | Dev risk | Visual gain | Perf risk | Shipping recommendation |
|---|---:|---:|---:|---:|---|
| DXR RT / HDRP | No | Very high | Very high | Medium–high (GPU) | Postpone to Phase 11+ |
| PTGI screen-space | Yes | Medium | Medium | Medium | Stage-2 candidate after phase 1 |
| DLSS/FSR3/XeSS | No (built-in) | Medium–high | Medium | Medium | Postpone to Phase 11 |
| SSAO+SSR+SSGI | Yes | Low–medium | High (perceived depth) | Medium | **Ship now** |

---

## 4) Acceptance criteria

- `BuiltInLightingScreenSpace` defaults OFF and can be toggled at runtime.
- Zero gameplay regression for camera controls, chunk updates, and selection logic.
- On target test scenes, built-in stack reaches defined FPS target while reducing visual flatness:
  - flatter, featureless plains become less posterized
  - edges/crevices show coherent occlusion
  - reflective surfaces recover local detail instead of mirror-flat look
- No architectural debt introduced that blocks `Phase 11` migration later.

## 5) Risks

- SSR/SSGI will still be bounded by screen-space assumptions; distant indirect details remain approximate.
- Fog and atmospheric particles can amplify SSAO/SSGI artifacts if not clamped.
- Any future URP migration rework may require recalibration of all effect radii and strength values.
- DLSS/FSR3/XeSS should be considered **enabling tech after migration**, not a built-in-first path.

## 6) Recommendation summary

**Ship now:** built-in **SSAO + SSR + SSGI** (phased, flag-gated, low blast radius).

**Next technical milestone:** keep PTGI approximation as a controlled optional built-in enhancement.

**Strategic milestone:** schedule **URP migration in Phase 11**, and only then make a bounded decision on true RT GI and NVIDIA Streamline/FSR3/XeSS integration.

---

## 7) Implementation status (2026-05-23)

**Overall:** **Research / deferred** — decision record only; no runtime rollout in the current branch.

| Item | Status |
|------|--------|
| Spec + option analysis + phased plan (§1–6) | Done (research) |
| Phase 0 baseline / profiling guardrails | **Deferred** |
| Phase 1 built-in SSAO + SSR + SSGI stack | **Deferred** |
| Phase 2 validation / biome visual passes | **Deferred** |
| PTGI screen-space approximation (Stage-2) | **Deferred** |
| `SavedSettings.BuiltInLightingScreenSpace` toggle | Not started |
| DXR RT / DLSS / FSR3 / XeSS (Phase 11 gate) | **Deferred** — requires URP/HDRP migration |
| E2E spec-on-disk invariant ([`RtPtgiDlssSpecInvariantsTests`](../../../tests/WorldSphereMod.Tests.E2E/RtPtgiDlssSpecInvariantsTests.cs)) | Done |

**Next action when scheduled:** ship Phase 1 per §2 (`BuiltInLightingScreenSpace` flag-gated, default OFF) before revisiting PTGI or Phase 11 upscaling/RT paths.