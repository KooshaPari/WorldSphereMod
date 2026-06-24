# WSM Parallel Execution Matrix (2026-06)

Date: 2026-06-18

## Source of truth
- JSON source: [execution-matrix-2026-06.json](execution-matrix-2026-06.json)
- Current state: [recovery-roadmap-2026-06.md](recovery-roadmap-2026-06.md)

## Status table

| ID | Owner | Task | Status | Target Date | Evidence |
| --- | --- | --- | --- | --- | --- |
| WSM-R0 | orchestrator | Lane freeze and evidence lock | done | 2026-06-18 | artifacts/2026-06-18/WSM-R0-branch-lock.txt |
| WSM-R1 | qa-engineer | Truth gates: test and Harmony contradiction | in_progress | 2026-06-19 | artifacts/2026-06-18/WSM-R1-branch-commit.txt, artifacts/2026-06-18/WSM-R1-cache-archive.txt, artifacts/2026-06-18/WSM-R1-startup-lock.txt, artifacts/2026-06-18/WSM-R1-smoke-startup.txt, artifacts/2026-06-18/WSM-R1-exception-baseline.txt, artifacts/2026-06-18/WSM-R1-phase-flags.txt, artifacts/2026-06-18/WSM-R1-source-fail-map.txt, artifacts/2026-06-18/WSM-R1-harmony-resolve.txt, artifacts/2026-06-18/WSM-R1-coverage-decision.md, artifacts/2026-06-18/WSM-R1-batch-1-notes.md, artifacts/2026-06-18/WSM-R1-batch-2-notes.md |
|  |  |  |  |  | WSM-R1 runtime gates remain blocked; `WSM-R1-phase-flags.txt` is a completed static snapshot only. The 2026-06-21 `/health` refused receipt is a blocker artifact, not live startup proof. |
| WSM-R2 | runtime-specialist | Phase ladder smoke execution | blocked_live_runtime | 2026-06-22 | static prep only: artifacts/2026-06-18/WSM-R2-evidence-template.md, artifacts/2026-06-18/WSM-R2-blocker-map.txt, artifacts/2026-06-18/WSM-R2-proof-directory-schema.md |
|  |  |  |  |  | WSM-R2 phase-0 proof is not complete; startup, Player.log, and screenshot evidence require a live WSM runtime session. |
| WSM-R3 | architecture | Substrate hardening and shader policy | ready | 2026-06-24 | pending |
| WSM-R4 | docs-curator | Ship readiness and merge gate | ready | 2026-06-25 | pending |

## Evidence link format (required)
- `status` transitions must include evidence entries: build logs, screenshot hashes, Player.log snippets, and/or explicit static-snapshot notes.
- Recommended payload format:
  - `artifacts/2026-06-18/WSM-R0-build-install.txt`
  - `artifacts/2026-06-19/WSM-R1-harmony-verified.txt`
  - `artifacts/2026-06-18/WSM-R1-runtime-runbook.txt`
  - `artifacts/2026-06-18/WSM-R1-operator-checklist.md`

## Parallel execution rule
- WSM-R0 and Dino-D0 can run independently.
- WSM-R1 and Dino-D1 can run once initial artifacts exist and owners are aligned.
- Current WSM-R1 blocker: runtime smoke/exceptions tasks remain blocked until a live runtime session is available; this pass did not attach one, and static snapshot items are complete without runtime proof.
- Current WSM-R2 blocker: phase-0 proof cannot start until WSM-R1 live startup, Player.log exception baseline, and screenshot baseline are captured.
