# wsm3d.ps1 CLI Audit

Scope: `Tools/wsm3d.ps1`, `Tools/wsm3d.completion.ps1`, and the journey docs that describe expected CLI usage.

## Verdict

- All wired top-level commands are documented in `Show-Help`.
- One implemented journey path is not wired into the dispatcher/help: `journey capture`.
- User-facing flags are mostly consistent: single-dash, PascalCase (`-Key`, `-Json`, `-DryRun`, etc.).
- I found no user-facing `--key` or `-key` usage in `wsm3d.ps1`.
- The only casing drift is internal, where `phenotype-journey` is invoked with lowercase `-id`.

## Findings

- Historical note: the dispatcher/help references below reflected the older CLI shape at the time of this audit. The current canonical journey contract is `journey verify -Id <id>` or `journey verify <manifest-path>`, with `capture` still supported as a separate path.
- Undocumented / orphaned subcommand: `journey capture -Id <id> [-NonInteractive]`.
  - `Invoke-JourneyCapture` exists in `Tools/wsm3d.ps1:791` and ends with a success message at `Tools/wsm3d.ps1:868`.
  - The dispatcher/help snapshot from the audit era reflected an older journey enumeration/run split; the current contract is `journey verify` plus `journey capture`.
  - `Show-Help` documents only `journey verify` and `journey capture` in the current completion/help path.
  - `Tools/wsm3d.completion.ps1` still offers `capture`, and `docs/journeys/CONTRIBUTING.md:72` tells users to run it.
- Flag casing consistency:
  - External CLI flags are consistently PascalCase with a single leading dash: `-Key`, `-Value`, `-Force`, `-Json`, `-DryRun`, `-Launch`, `-NoBuild`, `-Tail`, `-Follow`, `-Grep`, `-Path`, `-WindowOnly`, `-Filter`, `-Phase`, `-Id`, `-Configuration`.
  - No `--key` or `-key` appears in the script.
  - Lowercase `-id` is used only in the forwarded `phenotype-journey` calls at `Tools/wsm3d.ps1:774` and `Tools/wsm3d.ps1:788`.

## Recommended Additions

- `phases enable-all`: one command to turn every phase flag on for smoke tests and full-stack repros.
- `phases preset safe-min`: one named preset for a minimal, stable baseline instead of toggling individual flags.
- `log tail-perf` or `logs tail-perf`: one profiler-oriented tail mode that combines `-Follow`, `-Grep`, and the render-budget/log parsing use cases.
- If you want strict naming consistency, prefer `log tail-perf` because the current command is singular (`log`), and add `logs` only as an alias if you really want the plural family.
