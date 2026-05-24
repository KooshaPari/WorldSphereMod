# Agent Team Management Policy (WSM3D Scaling Operations)

This policy governs WorldSphereMod 3D engineering when multiple Codex/Claude agents and human contributors collaborate.

## 1) Branching strategy

### 1.1 Primary branch
- `claude/research-ultraplan-fork-DdgI5` remains the team collaboration branch.
- `main` is reserved for finalized upstream sync only.

### 1.2 Phase work branches
- Every phase uses an isolated branch derived from the collaboration branch:
  - `phase/<N>/agent/<owner>-<topic>`
  - Example: `phase/10/agent/haiku-lod-impostor`
- One open PR per phase.
- Rebase/sync weekly (or before merge) against `claude/...` to avoid drift.

### 1.3 Merge rules
- Prohibit direct commits to the collaboration branch.
- PRs must target `claude/research-ultraplan-fork-DdgI5`.
- Commits remain atomic and labeled `phase N step M: <summary>`.
- Conventional Commit format is mandatory (e.g. `feat:`, `fix:`, `chore:`).

## 2) Architecture governance and ADR review

### 2.1 ADR requirement
- Any architecture-affecting change **must** include/update an ADR before merge:
  - API boundary changes
  - New subsystems / cross-phase coupling changes
  - Rendering/Unity lifecycle shifts
  - Data-schema or save/load format changes
  - New long-lived abstractions in core shared modules

### 2.2 PR template enforcement
- PR template fields must require:
  - `ADR reference` (new or existing ADR ID)
  - `Risk / rollback path`
  - `Test evidence` (unit/integration/visual)
- If `ADR reference` is blank, PR is blocked until filled.

### 2.3 CODEOWNERS gating
- Files under these architecture paths are maintainer-gated:
  - `WorldSphereMod/Code/**`
  - `WorldSphereMod/Code/SavedSettings.cs`
  - `WorldSphereMod/Code/WorldSphereAPI.cs`
  - `docs/adr/**`
- PRs touching gated paths require at least one maintainer approval.
- Non-administrative agents must include explicit maintainer review checklist in PR body.

## 3) Agent dispatch policy

### 3.1 Default role mapping
- **Feature design / architecture synthesis** → `opus` (or designated architecture model).
- **Research, API archaeology, decompile interpretation, tradeoff briefs** → `haiku`.
- **Implementation and large file edits** → `general-purpose` coder agents.
- **Codegen / scaffolding / repetitive transformations** → `spark`.
- **Review / risk scanning** → dedicated reviewer agent before merge.

### 3.2 Task-to-agent routing matrix
- `Docs + ADR + policy`: `opus` first, then `general-purpose` for edits.
- `Decompile + unknown API check`: `haiku` or `general-purpose`.
- `New phase implementation`: `spark` for boilerplate, `general-purpose` for core logic, `opus` for final architecture validation.
- `Merge-readiness audit`: reviewer agent only (no code changes).

### 3.3 Dispatch rules
- Every dispatch must include:
  1) scope boundary,
  2) exact files owned,
  3) output artifact expectation,
  4) explicit handoff format.
- Never spawn duplicate agents on the same file set unless split by non-overlapping ownership.

## 4) Worktree governance and collision prevention

### 4.1 Ownership map
- Each agent is assigned a non-overlapping file set.
- Shared files (`docs/`, `README`, global settings) only by designated integrator unless lock is explicit.

### 4.2 File lock practice
- The branch owner posts file reservations in the PR/thread before dispatch.
- Conflicts are resolved by:
  1) pausing one agent,
  2) splitting diff by module,
  3) rebase + manual merge if needed.
- No single file may be edited by two parallel agents in the same phase without explicit split plan.

### 4.3 Branch hygiene
- Agents should keep short-lived branches and rebase from base branch before final handoff.
- Cherry-pick only scoped commits at PR integration stage.
- Never force-push shared phase branches unless agreed and announced.

## 5) Test budget and merge gate per PR

No PR merges without published evidence of:

1. **Unit tests**
   - `dotnet test tests/WorldSphereMod.Tests.Unit/...`
   - include pass/fail summary in PR body.
2. **Integration tests**
   - `task test` or `dotnet test tests/...Integration...`
   - list environment assumptions clearly (if skipped, explicit reason).
3. **Visual regression evidence**
   - screenshot/video artifact or equivalent frame-diff log in docs comment.
   - include scenario tested and camera/build settings.

### 5.2 Evidence format
- PR description must include links or attachments for each test type.
- “Not run” is acceptable only when environment is blocked and approved by maintainer with mitigation plan.

### 5.3 Merge gate thresholds
- All required artifacts are present
- ADR and review checkboxes complete
- Maintainer approval satisfied for gated paths
- No open high-severity item in reviewer agent notes

## 6) Phase integrity checkpoints

- `Phase 0`: baseline policy + defaults.
- Later phases inherit this policy and keep phase flags `OFF` by default until approved smoke smoke-test.
- After phase gate passes, ship-gate commit enables the phase-specific flag only.
