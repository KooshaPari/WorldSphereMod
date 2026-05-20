# Parallel Test Orchestrator Design

Goal: run `N` WorldBox + WSM3D instances in parallel, assign each a distinct bridge port, execute different scenarios, and aggregate the results into a single CI-friendly report.

## Shape

Use Rust for the coordinator and worker runtime.

Proposed layout:

```text
Tools/wsm3d-orchestrator/
  Cargo.toml
  src/
    main.rs
    cli.rs
    ports.rs
    instance.rs
    scenario.rs
    runner.rs
    aggregate.rs
    telemetry.rs
    ci.rs
  scenarios/
    phase-1.toml
    phase-2.toml
    ...
  .github/workflows/orchestrator.yml
```

## Public CLI Surface

```text
wsm3d-orchestrator run --scenarios scenarios/*.toml --parallel 4
wsm3d-orchestrator run --scenario phase-1.toml --repeat 20
wsm3d-orchestrator list
wsm3d-orchestrator validate scenarios/*.toml
wsm3d-orchestrator explain phase-5.toml
```

Core flags:
- `--parallel <N>`: max concurrent game instances.
- `--port-base 8765`: first bridge port; instance `i` binds `8765 + i`.
- `--timeout-seconds`: per-step and per-instance watchdog.
- `--retry-hang <count>`: relaunch on no-heartbeat or stalled step.
- `--output <dir>`: writes JSON, JUnit, logs, screenshots, and telemetry diffs.

## Instance Lifecycle

1. Allocate a free port and a working directory.
2. Spawn WorldBox with WSM3D configured for that port.
3. Health-check the bridge before scenario start.
4. Execute the scenario step sequence.
5. Emit per-step telemetry snapshots and assertions.
6. Shutdown cleanly, or kill and retry if the process hangs.

The instance manager should track PID, port, scenario id, retry count, and last heartbeat. Retry policy must be bounded and deterministic so CI does not loop forever.

## Scenario DSL

Use TOML as the primary authoring format, with YAML accepted for interchange. The DSL should stay declarative: bridge calls, waits, assertions, and telemetry captures.

```toml
id = "phase-1-voxel-actors"
world_seed = 12345
bridge_port = 8765

[[steps]]
call = "settings.set"
args = { key = "VoxelEntities", value = true }

[[steps]]
call = "world.regenerate"

[[steps]]
assert = "telemetry.render.voxel_actors > 0"
```

Phase examples, one per shipped phase:
- Phase 1: toggle `VoxelEntities`, regenerate, assert voxel actor count increases.
- Phase 2: toggle `ProceduralBuildings`, spawn a village, assert building mesh batches exist.
- Phase 3: toggle crossed foliage and wall overlays, assert foliage and wall draw calls.
- Phase 4: toggle `MeshWater`, load shoreline, assert water surface telemetry.
- Phase 5: toggle `HighShadows`, advance time, assert shadow cascade activity.
- Phase 6: toggle `SkeletalAnimation`, spawn humanoids, assert rig update counts.
- Phase 7: toggle `WorldspaceUI`, select an actor, assert nameplate and HP telemetry.
- Phase 8: toggle `DayNightCycle`, advance clock, assert sun angle and fog changes.
- Phase 9: toggle `PostFX`, trigger damage and decals, assert effect dispatch.
- Phase 10: toggle `ProfilerDump`, force low-tier LOD, assert impostor fallback.

## Result Aggregation

Each scenario should produce:
- pass/fail status
- step timeline
- bridge command log
- telemetry snapshot diff against baseline
- artifact paths

Write a machine-readable summary such as `summary.json` plus `junit.xml` for GitHub Actions. Telemetry diffs should focus on counters, timings, and state transitions, not raw frame dumps unless a scenario explicitly requests screenshots.

## CI Integration

Use a GitHub Actions matrix over scenarios or scenario groups:

```yaml
strategy:
  matrix:
    scenario: [phase-1, phase-2, phase-3, phase-4, phase-5]
```

Each matrix job runs one scenario bundle with its own port range and workspace. The orchestrator can also do in-job parallelism for local development, but CI should prefer matrix parallelism for clearer logs and smaller failure domains.

## Phenotype-Journeys Integration

The orchestrator should not replace `phenotype-journey`; it should drive it.

- `phenotype-journey` remains the manifest verifier and screenshot/OCR authority.
- `wsm3d-orchestrator` adds parallel WorldBox instance management and bridge-driven scenario execution.
- A scenario can finish by handing control to a journey manifest step, so visual assertions still use the existing journey harness.
- The existing `Tools/wsm3d.ps1 journey *` and `.github/workflows/journeys-gate.yml` remain the compatibility layer; the orchestrator becomes the higher-throughput runner beneath them.

Suggested contract:
1. orchestrator executes bridge calls and collects telemetry
2. journey harness verifies screenshots and OCR where needed
3. CI merges both into one release gate

## Output Contract

Treat the orchestrator as a local test daemon with a narrow, stable surface. If the CLI, scenario DSL, and JSON summary stay stable, the harness can evolve internally without changing the existing journey authoring model.
