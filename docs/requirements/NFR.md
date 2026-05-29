# Non-Functional Requirements — WorldSphereMod3D

> These gate **how well** the visible features in `FR.md` run and how safely the
> mod loads. They are deliberately concrete (numbers, log strings) so the vision
> loop or a `dotnet test` / `Player.log` grep can return PASS/FAIL without a
> human eye. Status is tracked in `traceability.md`.

---

## NFR-PERF — Performance

### NFR-PERF-1 — Frame rate at strategy view
**Requirement:** With the default-ON phase set enabled on a populated world
(target **N = 200 visible actors** on a 256×256 map at strategy zoom),
steady-state frame time is **≤ 33 ms (≥ 30 FPS)**.

- **Verify:** `/telemetry.frameTimeMs` averaged over 100 frames ≤ 33; or runtime
  smoke-test FPS line in `Player.log`.
- **Current baseline:** 32 ms / 30 FPS at 46 visible actors (runtime smoke
  2026-05-28). Must be re-measured at N=200.

### NFR-PERF-2 — Voxel mesh cache hit rate
**Requirement:** After warmup, voxel mesh cache hit rate **> 99%**.
- **Verify:** `/telemetry.voxelCacheHit > 0.99`.

### NFR-PERF-3 — Precalc / build spike budget
**Requirement:** One-time synchronous world-build spike (sprite voxelize +
building build) **< 1500 ms**; no single frame stalls > 200 ms after warmup.
- **Verify:** `Player.log` load-spike line; `/telemetry` max frame time post-warmup.
- **Known gap:** a ~6.2 s synchronous building-voxelize spike was observed; this
  NFR is **BROKEN** until that build is chunked/async.

### NFR-PERF-4 — Draw-call efficiency (instancing)
**Requirement:** Draw calls **≪** instance count (instancing active, not the
per-instance `DrawMesh` fallback).
- **Verify:** `/telemetry.drawCalls` ≪ `visible_units`; `Player.log` shows
  `material.enableInstancing == true` read-back (guards the silent-fail pitfall).

---

## NFR-STAB — Stability

### NFR-STAB-1 — No native crash / no per-frame exception storm
**Requirement:** A full session (load → 5 min strategy play → unload) produces
**zero** `Uploading Crash Report`, zero native `ManagedStream` errors, and
**no per-frame** `NullReferenceException` / `KeyNotFoundException`.
- **Verify:** `Player.log` grep: 0 crash-report lines; per-frame exception count
  not growing with frame count.
- **Status:** NEEDS RE-EVALUATION (issue-triage §3 flags a historical exception
  storm not yet re-confirmed clean).

### NFR-STAB-2 — No debug spam / no on-screen console overlay in shipping
**Requirement:** Shipping build emits **no per-frame `Debug.Log`** and renders
**no on-screen console / profiler overlay** unless `ProfilerDump` is explicitly on.
- **Verify:** `Player.log` WSM3D-tagged lines do not scale with frame count;
  screenshot shows no overlay text by default.

### NFR-STAB-3 — Clean init, no NRE on load
**Requirement:** `Mod.OnLoad` completes with **zero WSM3D-tagged NRE** between
`[WSM3D] Init Mod` and `World Loaded`, on cold install and after AssetBundle
conflict. (Vanilla WorldBox PowerButton NRE excluded — upstream.)
- **Verify:** `ModLoadSmokeTests` (E2E) + `Player.log` scan.

### NFR-STAB-4 — Settings survive kill + launch
**Requirement:** Every phase flag returns the same value after kill+relaunch
(NML `PlayerConfig.dict` must not silently revert the SavedSettings JSON).
- **Verify:** compare each `/phase/<X>.enabled` before/after relaunch;
  `[WSM3D] Settings sanity:` log lines confirm loaded == intended.

---

## NFR-LOAD — Load & Compile

### NFR-LOAD-1 — Mod compiles under NML (Mono Roslyn)
**Requirement:** NML compiles `Code/*.cs` with **zero `error CS`** and no
`Failed to compile mod WorldSphereMod3D` (silent-skip guard).
- **Verify:** `Player.log` grep after every launch; `NmlCompileCompatTests` (E2E).

### NFR-LOAD-2 — `isWorld3D` active within bound
**Requirement:** After world generation, `isWorld3D=true` is set within **Y = 10 s**
of "Loading finished" (no infinite loading-screen stall).
- **Verify:** `Player.log` `finishMakingWorld` Postfix line + timestamp;
  `BridgeLoadSaveHooks` must patch `loadWorld(string,bool)` explicitly.

### NFR-LOAD-3 — OnLoad time
**Requirement:** NML `OnLoad` → `[WSM3D] Init Mod` in **< 5 s**.
- **Verify:** `Player.log` timestamps. Current ~2.3 s — MEETS.

---

## NFR-COMPAT — Compatibility

### NFR-COMPAT-1 — Unity 2022.3.60f1 AssetBundle baseline
**Requirement:** Shipped `wsm3d-shaders` bundle is baked against the WorldBox
Unity version (2022.3.60f1) and loads on the target platform without native error.
- **Verify:** `Player.log` `LoadedShaders[count=N]` with non-empty resolved names;
  `SafeShadersGuardTests` keeps the runtime load list == proven-safe set.
- **Status:** bundle lists 10 shaders; runtime loads **3** via `SafeShaders`. The
  other 7 are **gated/UNVERIFIED** (FoliageWind, ProceduralSky, Impostor, SSAO,
  SSGI, Bloom, ACES) — each must pass the human SafeShaders gate before its
  dependent FR can be PROVEN.

### NFR-COMPAT-2 — Co-installable with upstream
**Requirement:** GUID `worldsphere3d.fork` stays distinct; mod does not crash
when upstream `WorldSphereMod` is present (enable one at a time).
- **Verify:** load with both installed; `Player.log` clean init.

### NFR-COMPAT-3 — Mono target (net48), no C# 9+ in mod core
**Requirement:** Mod core uses only Mono 6.12 / net48-compatible C#; no
`.Length` on non-array types (NML Roslyn strictness).
- **Verify:** `NmlCompileCompatTests`; clean NML compile.

---

## NFR-VERIFY — Validation Coverage (meta)

### NFR-VERIFY-1 — Every FR has a screenshot acceptance check
**Requirement:** 100% of FRs in `FR.md` have a corresponding entry in
`acceptance-checklist.md` the vision loop can run.
- **Verify:** `acceptance-checklist.md` entry count == FR count (21).

### NFR-VERIFY-2 — Every visual FR PASS requires a from-game screenshot
**Requirement:** No FR is marked PROVEN-PASS on telemetry/`/phase` echo alone; a
`capture_target: worldbox_window` screenshot is required for any visual FR.
- **Verify:** traceability status + screenshot provenance field
  (`capture_target` in PlayCUA step JSON).

---

**Total: 16 Non-Functional Requirements across 5 categories
(PERF 4, STAB 4, LOAD 3, COMPAT 3, VERIFY 2).**
