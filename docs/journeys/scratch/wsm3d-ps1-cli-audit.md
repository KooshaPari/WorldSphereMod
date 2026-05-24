# wsm3d.ps1 CLI Audit

Scope: `Tools/wsm3d.ps1`, `Tools/wsm3d.completion.ps1`, and the journey docs that describe expected CLI usage.

## Verdict

- All wired top-level commands are documented in `Show-Help`.
- **Resolved (2026-05-23):** `journey capture` is wired in `Show-Help`, the dispatcher, and tab completion; guarded by E2E invariants in `Wsm3dCliInvariantsTests`.
- User-facing flags are mostly consistent: single-dash, PascalCase (`-Key`, `-Json`, `-DryRun`, etc.).
- I found no user-facing `--key` or `-key` usage in `wsm3d.ps1`.

## Findings

- Canonical journey contract: `journey verify -Id <id>` or `journey verify <manifest-path> [-Live]`, plus `journey capture -Id <id> [-NonInteractive]` for step screenshots.
- `Invoke-JourneyCapture` walks manifest steps and calls `Invoke-Screenshot`; `Invoke-JourneyVerify` shells to `phenotype-journey verify` with `--mock` or `--live`.
- `Show-Help`, the dispatcher, and `Tools/wsm3d.completion.ps1` all expose `capture` and `verify`; `docs/journeys/CONTRIBUTING.md` documents capture for authors.
- Flag casing consistency:
  - External CLI flags are consistently PascalCase with a single leading dash: `-Key`, `-Value`, `-Force`, `-Json`, `-DryRun`, `-Launch`, `-NoBuild`, `-Tail`, `-Follow`, `-Grep`, `-Path`, `-WindowOnly`, `-Filter`, `-Phase`, `-Id`, `-Configuration`.
  - No `--key` or `-key` appears in the script.
  - Lowercase `-id` is used only in the forwarded `phenotype-journey` calls at `Tools/wsm3d.ps1:774` and `Tools/wsm3d.ps1:788`.

## Recommended Additions

- `phases enable-all`: one command to turn every phase flag on for smoke tests and full-stack repros.
- `phases preset safe-min`: one named preset for a minimal, stable baseline instead of toggling individual flags.
- `log tail-perf` or `logs tail-perf`: one profiler-oriented tail mode that combines `-Follow`, `-Grep`, and the render-budget/log parsing use cases.
- If you want strict naming consistency, prefer `log tail-perf` because the current command is singular (`log`), and add `logs` only as an alias if you really want the plural family.
