# Polyglot Architecture Survey

WSM3D should stay C#-first for mod behavior, but polyglot pays off at hard
boundaries: native hot paths, cross-process orchestration, developer tooling,
browser UX, and offline analysis. Use extra languages only when they reduce
latency, improve ergonomics, or isolate risk.

## 1. Native hot paths

Best fit: Rust first, C++ only when an existing library forces it.

- Use case: voxelize large atlases, mesh smoothing/decimation, asset export
  prep, other CPU-heavy transforms that should not block the Unity main thread.
- Integration: ship a native DLL and load it from the mod via `DllImport` /
  P/Invoke. Keep the boundary C ABI-safe: blittable structs, pinned buffers,
  explicit init/shutdown, versioned entrypoints.
- Cost: high. You take on native build tooling, crash debugging, per-platform
  packaging, ABI drift, and extra release validation.
- Governance: require a benchmark and a profiler trace before adoption. Keep
  the native surface small, deterministic, and side-effect free outside its
  inputs/outputs. No direct game-state mutation from native code.

## 2. Test harness

Best fit: Rust orchestrator for process control, xUnit for unit tests on
Unity-free assemblies, optional Python helpers for data-heavy fixtures.

- Use case: plate-20 style parallel scenario execution, bridge-driven smoke
  tests, regression replay, and fast unit coverage for `WorldSphereAPI`.
- Integration: RPC for live game control, file IPC for artifacts and baselines,
  and test manifests as the source of truth for scenarios.
- Cost: medium. More moving parts, but the payoff is better parallelism and
  clearer separation between unit, integration, and end-to-end scopes.
- Governance: keep one canonical manifest per scenario, make the harness
  deterministic, and separate CI-safe tests from live-game checks. Tests should
  trace to phase or journey intent, not duplicate mod logic.

## 3. Build automation

Best fit: Justfile or Taskfile as thin runners.

- Use case: common verbs like `build`, `test`, `lint`, `docs`, `capture`, and
  `install` across C#, Rust, Python, and PowerShell tooling.
- Integration: wrapper targets that shell out to `dotnet`, `pwsh`, `cargo`,
  and `npm`. The runner is orchestration only, not business logic.
- Cost: low. The main risk is command drift if logic gets copied into multiple
  places.
- Governance: define one verb map and mirror it in both runners. Keep complex
  work in dedicated scripts or project files, and document each target in the
  repo docs.

## 4. Web companion app

Best fit: TanStack JS for a local telemetry dashboard.

- Use case: live render counters, bridge status, phase flags, and capture
  controls for developers watching the game.
- Integration: browser app reads BridgeRPC on `127.0.0.1:8766` over RPC
  transport, typically HTTP/SSE or WebSocket. It should consume telemetry and
  status endpoints only.
- Cost: medium-high. You need schema discipline, UI state management, and a
  clear trust boundary.
- Governance: keep the dashboard read-only by default. Version telemetry
  payloads, tolerate reconnects, and never let the browser become gameplay
  authority.

## 5. Python notebooks

Best fit: perf analysis and forensic exploration.

- Use case: analyze logs, CSVs, screenshots, benchmark dumps, and bridge
  telemetry to identify frame-time regressions or cache behavior.
- Integration: file IPC only. Notebooks read exported artifacts and do not sit
  in the runtime path.
- Cost: low. Fast to prototype, easy to discard, easy to rerun.
- Governance: keep notebooks out of release gates. Treat them as analysis
  artifacts, pin their input files, and promote stable conclusions into docs,
  tests, or code.

## Bottom Line

Polyglot helps WSM3D when it preserves a narrow contract at a seam. If the
work can stay in C#, keep it there. If it needs native speed, parallel
orchestration, browser visualization, or offline analysis, move only that
boundary to the best tool and keep the contract explicit.
