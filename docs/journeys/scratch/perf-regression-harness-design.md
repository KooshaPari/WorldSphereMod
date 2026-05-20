# Perf Regression Harness Design

Goal: a reproducible, low-noise benchmark for WSM3D that can fail a PR when the mod regresses beyond a threshold.

## 1. Workload

Use a canned save plus a fixed camera pose as the benchmark fixture.

- Save file: committed under `tests/fixtures/perf/<scenario>/worldbox-save.*` or equivalent repo fixture path.
- Scenario metadata: a small JSON beside the save with `actors`, `buildings`, `camera`, `zoom`, `time_of_day`, and `warmup_seconds`.
- Repro rule: load the save, pause normal input, teleport camera to the recorded pose, wait for warmup, then benchmark a fixed window of frames.

This keeps the workload deterministic without depending on live map generation or user interaction.

## 2. Data Collection

Prefer a dedicated benchmark collector that reuses existing telemetry instead of scraping screenshots or free-form log text.

- Keep `FrameProfiler` as the main timing source for per-system CPU buckets.
- Add a thin Harmony patch or game-loop driver that marks benchmark start/stop and captures per-frame counters.
- Read draw-call and instance counters from `MeshInstanceBatcher.FrameDrawCalls` / `.FrameInstances`.
- Read `VoxelMeshCache.HitCount` / `.MissCount` and `MeshInstanceBatcher` submit counts as deltas over the sample window.
- Read GC alloc rate via Unity/Profiler counters if available; otherwise record `GC.GetAllocatedBytesForCurrentThread()` deltas as a fallback and label it clearly.

`RuntimeStatsOverlay` is a good existing sanity check, but it should stay human-facing. The regression harness should emit a structured record, not parse UI text.

Suggested output format: one JSON blob per run plus an optional per-frame CSV for deep dives.

## 3. Baseline Storage

Use two layers:

- Committed baseline JSON for the canonical benchmark target, e.g. `docs/journeys/perf-baselines/<scenario>.json`.
- GitHub artifact for the raw run payload, attached to the workflow so regressions can be inspected without rerunning locally.

Why committed JSON:

- It is reviewable in PRs.
- It makes threshold changes explicit.
- It avoids coupling pass/fail to an artifact retention window.

Keep the artifact as the diagnostic trail, not the source of truth.

## 4. Comparison Logic

Compare the measured summary against the committed baseline using a simple tolerance model:

- Frame time: fail if mean or p95 regresses by more than 10%.
- Draw calls and instances: fail if either exceeds baseline by more than a smaller cap, e.g. 5%, unless the scenario intentionally changed content density.
- VoxelMeshCache hit rate: fail if it drops by more than an absolute floor, e.g. 5 percentage points.
- MeshInstanceBatcher submission rate: fail if submissions per second rise materially while instance count stays flat.
- GC alloc/sec: fail on any sustained increase above a tiny absolute threshold, because allocation regressions tend to snowball.

To reduce noise, compare against a trimmed mean or median over the measurement window, not a single frame. Also require a minimum number of steady-state samples after warmup.

On failure, emit:

- the metric deltas,
- the scenario metadata,
- the raw benchmark JSON artifact,
- a short explanation of which threshold tripped.

## 5. CI Integration

Add a dedicated workflow, e.g. `.github/workflows/perf-regression.yml`, that runs on:

- `pull_request` for code or fixture changes,
- `workflow_dispatch` for manual runs,
- optionally a nightly schedule on a self-hosted Windows runner.

Important constraint: this benchmark needs a machine with WorldBox installed, so CI should use a tagged self-hosted runner rather than GitHub-hosted Linux.

Workflow shape:

1. Check out the repo.
2. Build the mod.
3. Launch WorldBox with the benchmark harness and the canned save.
4. Collect the structured metrics.
5. Compare against the committed baseline JSON.
6. Upload the raw artifact.
7. Post a PR comment if any regression threshold trips, with a compact table of deltas and a link to the artifact.

If the run passes, the workflow can still post a short status comment or rely on the check summary.

## 6. Recommendation

Implement this as a small benchmark runner plus a JSON comparator, reusing `FrameProfiler` for phase timing and `RuntimeStatsOverlay` only as a manual visual aid. That gives one deterministic workload, one baseline file, and one CI gate.
