# Infra + Tooling Gaps Survey

Scope: what WSM3D still lacks beyond the infrastructure already in the repo, ranked by impact per effort.

Current partials worth noting:
- `WorldSphereMod/Code/Bridge/BridgeServer.cs` already exposes `/health`, `/telemetry`, and `/settings`, so telemetry work here is about exporting and standardizing, not inventing the first endpoint.
- `.github/workflows/release.yml` already extracts notes from `CHANGELOG.md`, so release automation is partially present, but still hand-driven.
- `.github/workflows/lint-gate.yml`, `.github/workflows/test-gate.yml`, and `.github/workflows/docs-build-gate.yml` exist, but there is no WSM3D-specific analyzer/lint/perf budget layer yet.

## Ranked Gaps

1. **SemVer + CHANGELOG automation**
   - Impact: high.
   - Effort: low.
   - Gap: release notes are still manually curated, and the current release workflow only extracts `[Unreleased]` text. There is no Conventional Commits -> changelog pipeline, version bump automation, or release-note synthesis.
   - Why it ranks high: this is the cheapest way to reduce release friction and make every phase/patch easier to ship cleanly.

2. **WSM3D-specific lint rules**
   - Impact: high.
   - Effort: medium.
   - Gap: there is no analyzer/gate for repo-specific rules like “no `Vector3` boxing in hot paths,” “[Phase] attribute required on phase toggles,” or “forbid allocations in critical render/update loops.”
   - Why it ranks high: it prevents regressions before they land, and it scales better than code review for repeated mistakes.

3. **AST diff tooling for upstream Postfixes**
   - Impact: high.
   - Effort: medium.
   - Gap: the repo has many Harmony Postfixes, but no syntax-aware tool that compares WSM3D patches against upstream code and flags drift, missed hooks, or signature changes.
   - Why it ranks high: this is the best way to keep the mod aligned with upstream churn without re-auditing every patch by hand.

4. **API doc generation**
   - Impact: medium-high.
   - Effort: medium-high.
   - Gap: there is no automated docs pipeline for the bridge API or the C# surface. Swashbuckle does not fit the current raw `HttpListener` bridge without a transport change, and DocFX is not wired for code docs.
   - Why it ranks here: useful for maintainability and onboarding, but it does not unblock runtime behavior.

5. **Crash reporter integration**
   - Impact: medium-high.
   - Effort: medium.
   - Gap: there is no Sentry SDK wiring, no exception capture path, and no release/symbol upload flow.
   - Why it ranks here: crash visibility is valuable, but it is only useful once the reporting payloads, release tags, and symbol handling are reliable.

6. **Performance budget CI**
   - Impact: high.
   - Effort: medium-high.
   - Gap: current CI validates build/test/docs, but it does not fail PRs on frame-time regressions or budget overruns.
   - Why it ranks here: this becomes critical once the mod is performance-sensitive in gameplay, but it needs a stable benchmark harness first.

7. **Telemetry pipeline**
   - Impact: medium.
   - Effort: medium.
   - Gap: the bridge currently emits ad hoc telemetry JSON, but it does not export OpenTelemetry spans/metrics/logs or provide a collector-compatible stream.
   - Why it ranks here: good for observability and perf triage, but less urgent than blocking correctness or release automation.

8. **Roslyn generators / analyzers for boilerplate Postfix patterns**
   - Impact: high.
   - Effort: high.
   - Gap: there is no source generator to stamp out repeated Harmony/Postfix scaffolding, no analyzer to enforce phase attributes, and no compile-time help for common patch shapes.
   - Why it ranks here: it can remove a lot of repetitive code, but the initial design surface is large and easy to over-engineer.

9. **Dependency injection container**
   - Impact: medium.
   - Effort: medium-high.
   - Gap: the mod still looks mostly static/singleton-driven; there is no container for service registration, lifetime control, or test replacement.
   - Why it ranks here: DI can improve testability and composition, but it is not obviously the next bottleneck in a Unity mod that already has a narrow runtime surface.

10. **Hot-reload**
    - Impact: very high for iteration speed.
    - Effort: very high.
    - Gap: there is no file watcher, incremental recompilation loop, assembly swap, or safe state handoff for reloading code without restarting WorldBox.
    - Why it ranks last: the developer-experience payoff is large, but the runtime and loader complexity is also the highest in the list.

## Practical Order

If the goal is maximum leverage with minimal churn, the best sequence is:

1. `CHANGELOG.md` automation
2. WSM3D-specific lint rules
3. AST diff tooling
4. Performance budget CI
5. Telemetry + crash reporting

The generator/analyzer layer and hot-reload are both valuable, but they are larger platform bets. I would only start them after the repo has stronger guardrails and diffing in place.
