# Scratch Docs Reading Order

For a cold start, read these first:

## Roadmap / Orientation
1. [`consolidated-audit-summary.md`](./consolidated-audit-summary.md) - best single overview of what overlaps, what is real, and what is already a non-issue.
2. [`docs-vs-code-drift.md`](./docs-vs-code-drift.md) - reconciles the docs with current `SavedSettings`/runtime gates so you know what is actually shipped vs opt-in.
3. [`highest-leverage-fix-recommendation.md`](./highest-leverage-fix-recommendation.md) - gives the strongest “first fix” candidate and shows how multiple audits connect.

## Correctness
4. [`integration-risks-top5.md`](./integration-risks-top5.md) - the highest-blast-radius failures: unload teardown, lighting bleed, cache poisoning, and rig leaks.
5. [`error-handling-audit.md`](./error-handling-audit.md) - useful for understanding the repo’s failure modes, logging discipline, and where bugs can be swallowed.

## Perf
6. [`perf-roadmap-2026-05-19.md`](./perf-roadmap-2026-05-19.md) - the current performance priorities, split into already-shipped fixes and the next evidence-backed wins.

## Test
7. [`test-coverage-gaps.md`](./test-coverage-gaps.md) - shows which branches and cache paths are still untested, and what to add first.

If you only read three: start with `consolidated-audit-summary.md`, `docs-vs-code-drift.md`, and `integration-risks-top5.md`.
