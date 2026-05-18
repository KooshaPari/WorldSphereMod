# Phenotype org conventions

Reference map of recurring patterns across `KooshaPari/Dino`, `KooshaPari/PhenoSpecs`,
`KooshaPari/phenodocs`, `KooshaPari/TestingKit`, and `KooshaPari/phenotype-dep-guard`.
Use this to align `WorldSphereMod3D` with the rest of the org.

---

## 1. Required top-level files

Every Phenotype repo carries the same governance bundle at root. Spec roots are
deliberately at root (not in `docs/`) so agents can find them without exploring.

| File | What it is | Notes |
|---|---|---|
| `README.md` | Public entry point: stack badges, quick start, key files | Always opens with one-line description + status badge for any quality gate |
| `CLAUDE.md` | Claude-specific operating rules (build/test/lint commands, agent constraints, file-governance) | Authoritative for Claude. In phenodocs/TestingKit it's a thin pointer: "see AGENTS.md" |
| `AGENTS.md` | Canonical agent contract: stack, branch/commit conventions, guardrails, work-delegation tools (gt_sling, etc. for Kilo rigs) | Used by all non-Claude agents (Gemini, Codex). Single source of truth |
| `GEMINI.md` | Gemini-specific shim, usually thin pointer to AGENTS.md | Present in Dino + WorldSphereMod |
| `CONTRIBUTING.md` | Build/test commands, branch naming (`feat/`, `fix/`, `chore/`, `docs/`, `refactor/`), Conventional Commits, AgilePlus spec mandate, squash-merge default | TestingKit/PhenoSpecs share a near-identical template (see snippet below) |
| `CODE_OF_CONDUCT.md` | Contributor Covenant–style; DINOForge has a custom but conventional version | Standard org boilerplate works |
| `SECURITY.md` | Private security advisory + email `kooshapari@gmail.com`; SLAs (3 day ack / 7 day triage / 30-day high-sev fix) | DINOForge version is the canonical template |
| `CHANGELOG.md` | Keep-a-Changelog 1.1.0 + SemVer; `[Unreleased]` section with Added/Changed/Deprecated/Removed/Fixed/Security | See section 7 |
| `SUPPORT.md` | Where to ask for help; not in all repos but present in Dino & WSM |
| `RELEASING.md` | Release workflow + version-bump steps | DINOForge + WSM use this |
| `LICENSE` (+ `LICENSE-APACHE`, `LICENSE-MIT`) | Dual MIT/Apache-2.0 is org default; PhenoSpecs ships both | TestingKit not yet licensed — flag |
| `VERSION` | Single-line version string, parsed by release tooling | All repos |
| `Taskfile.yml` / `Justfile` | Polyglot task runner (go-task or Just). Both target the same verbs (`check`, `test`, `lint`, `release:prep`) | WSM uses both — keep |
| `CHARTER.md` | Mission + tenets ("Tests should be fast", "Determinism is non-negotiable", …) | TestingKit/PhenoSpecs/phenodocs. Optional for a mod fork |
| `PRD.md`, `SPEC.md`, `ADR.md`, `PLAN.md`, `FUNCTIONAL_REQUIREMENTS.md`, `RESEARCH.md`, `SOTA.md` | **Spec roots live at repo root**, not under `docs/`. ADR.md is the index; per-decision ADRs live in `adrs/` or `docs/adr/` | This is the *defining* Phenotype convention |
| `catalog-info.yaml` | Backstage-style catalog metadata, used by federation | PhenoSpecs ships it; useful for phenodocs aggregation |
| `CODEOWNERS` (under `.github/`) | `* @KooshaPari` + per-directory overrides for `/docs/`, `/src/`, `/schemas/` | One-line is fine for a solo-author fork |
| `.editorconfig`, `.gitattributes`, `.pre-commit-config.yaml`, `.markdownlint.json` | Standard hygiene | All Phenotype repos |

---

## 2. `.github/workflows/` conventions

Workflows are split into two families:

**a) Core lifecycle** (every repo)

- `ci.yml` — build + test + lint (matrixed per language toolchain)
- `codeql.yml` — code scanning
- `scorecard.yml` — OSSF scorecard
- `release.yml` + `release-drafter.yml` (config in `.github/release-drafter.yml`)
- `changelog-lint.yml` — fails on duplicate version headers / non-Keep-a-Changelog sections / missing tag rows. Pattern #92 in DINOForge
- `dependency-review.yml` (PR-time) and `cargo-deny.yml` / `dependabot.yml`
- `trufflehog.yml` — secret scanning
- `sbom.yml` — software bill of materials on tag push

**b) "Gate" pattern** — `*-gate.yml` (very dense in DINOForge, lighter elsewhere)

A gate workflow is a single-purpose detector with this shape:

1. A Python script under `scripts/ci/detect_<pattern>.py` that scans `src/` for a code smell
2. A text allowlist under `docs/qa/<pattern>-allowlist.txt` for accepted/known-tolerated occurrences
3. A workflow that runs the detector, fails if HIGH > threshold, and uploads `findings.json` artifact on failure

Naming: kebab-case, ends in `-gate.yml`. Examples to mirror:
`sync-over-async-gate.yml`, `blocking-poll-gate.yml`, `silent-catch-gate.yml`,
`open-ended-count-gate.yml`, `json-deserialize-gate.yml`,
`event-lifecycle-gate.yml`, `httpclient-gate.yml`, `string-dict-gate.yml`,
`stringbuilder-capacity-gate.yml`, `di-validation-gate.yml`, `policy-gate.yml`,
`proof-gate.yml`, `journey-quality-gates.yml`, `test-isolation.yml`,
`mutation-test.yml`, `fuzz.yml`, `framework-version.yml`.

**Pin actions by SHA, not tag** — every workflow uses `actions/checkout@<sha> # v6.0.2` form.

`continue-on-error: true` is acceptable for placeholder gates (TestingKit uses this for quality-gate.yml while phenotype-tooling is pending).

---

## 3. `docs/` layout

VitePress is the org standard (`phenodocs` is the federation hub).
phenodocs/docs is the canonical layout — mirror it:

```
docs/
  index.md                # VitePress landing
  guide/                  # User-facing how-to
  reference/              # API reference
  governance/             # CODEOWNERS, charter pointers
  adr/                    # ADR-NNN-<kebab>.md + index.md (table)
  specs/                  # SPEC-NNN-<kebab>.md (or per-domain)
  journeys/               # See section 6
    manifests/            #   per-journey manifest.json
  research/               # SOTA notes, prior-art
  operations/             # Runbooks, deploy/, sessions/
  roadmap/                # ROADMAP_vX.md
  reviews/, plans/, qa/, security/, sessions/, milestones/
  public/                 # VitePress static assets (logo.svg, favicon.svg)
  .vitepress/config.mts   # uses createPhenotypeConfig() from @phenotype/docs
  package.json            # bun + vitepress
```

Key rule from TestingKit AGENTS.md: *"all docs except spec roots live under
`docs/<category>/`. Spec roots (PRD, ADR, FUNCTIONAL_REQUIREMENTS, PLAN,
USER_JOURNEYS) live at root."*

Build via `bun run build` (phenodocs) or `cd docs && npm run docs:build` (PhenoSpecs).

---

## 4. Testing conventions

**Folder layout** (Dino is canonical for C#):

```
src/Tests/                          # Unit (xUnit + FluentAssertions)
src/Tests/Integration/              # Integration tests
docs/qa/                            # Allowlists for gate detectors
```

For Rust/polyglot repos (TestingKit):
```
rust/<crate>/tests/                 # Cargo convention
python/<pkg>/tests/                 # uv/pytest convention
tests/                              # Cross-language
```

**TestingKit provides**: `phenotype-testing` (helpers: `timeout`, `block_on`,
`test_id`, `random_port`, `wait_for`); `phenotype-mock` (`CallRecord`,
`MockContext`); `phenotype-test-fixtures`; `phenotype-test-infra` (process
orchestration); `phenotype-compliance-scanner` (governance scans).
Consume via git dep until crates.io publish.

**CI invocation pattern**:
- One step per test project: `Test (Unit)`, `Test (Integration)`, `Test (Python MCP)`
- `--logger "trx;LogFileName=<suite>-test-results.trx"` for parsing
- Inline Python step parses TRX and writes `docs/test-results.json` for the docs site
- Coverage target: **95%+** (PR template checkbox)
- Tests trace to FRs: `// Traces to: FR-<REPO>-NNN` in test files

---

## 5. ADRs & specs

**ADRs** under `docs/adr/` (Dino) or `adrs/` (PhenoSpecs):
- Filename: `ADR-NNN-<kebab-title>.md` (PhenoSpecs uses `NNN-<kebab>.md` — both seen)
- Header: `**Status**: Proposed | Accepted | Deprecated | Superseded`, `**Date**`, `**Deciders**`
- Sections: Context → Decision → Consequences (Positive/Negative) → Mitigations → References
- `docs/adr/index.md` is a table linking every ADR with status
- A single root-level `ADR.md` summarizes architectural posture; per-decision files in `adr/`

**Specs** under `docs/specs/` or `specs/<domain>/`:
- Filename: `SPEC-NNN-<kebab>.md`
- Metadata table at top (Document ID, Version, Status, Author, Created, Last Updated, Target Release)
- Followed by Overview → Scope → Requirements → … → Appendices
- Functional requirements numbered `FR-<REPO>-NNN` and linked from code via traceability macros (`#[trace_fr]` in Rust, `// FR: …` in Go, comments in C#)

`PhenoSpecs/registry.yaml` is the cross-repo index — register new specs there.

---

## 6. User journey docs

Yes, this is a first-class convention. From DINOForge + TestingKit:

- Narrative lives in `docs/user-journeys.md` (+ `user-journeys-expanded.md`, `user-journeys-tier2.md`)
- Per-journey manifests under `docs/journeys/manifests/<journey-id>/manifest.json`
- Journey ID format: `us-f<feature>-<n>-<kebab-slug>` (e.g. `us-f1-1-game-launch`)
- Manifest schema (validated by `journey-quality-gates.yml`):
  ```json
  {
    "id": "us-f1-1-game-launch",
    "intent": "User launches game and verifies mod loaded",
    "keyframe_count": 3,
    "passed": false,
    "steps": [{"index": 0, "slug": "...", "intent": "...",
               "screenshot_path": "...", "assertions": {"must_contain": [...],
               "must_not_contain": [...]}}],
    "verification": {"mode": "pending", "timestamp": "..."}
  }
  ```
- `journey-gate.yml` (TestingKit) runs `phenotype-journey` CLI + tesseract OCR, fails if any manifest fails validation or strict-mode assertion

---

## 7. CHANGELOG convention

**Keep-a-Changelog 1.1.0 + SemVer** — no Conventional Commits auto-generation; entries are hand-written. Verified by `changelog-lint.yml` (Pattern #92).

Required header:
```markdown
# Changelog

All notable changes to <project> will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
### Changed
### Deprecated
### Removed
### Fixed
### Security
```

Linter checks: no duplicate version headers, no missing tag rows, no
non-Keep-a-Changelog `### X` headers, no duplicate sections within a version.
`release-drafter.yml` populates the next release, but CHANGELOG.md remains the
human-curated source of truth.

---

## 8. Code-of-conduct + contributing templates

**CODE_OF_CONDUCT.md** — Dino's version is the canonical one (custom Contributor
Covenant with sections: Our Commitment / Expected Behavior / Unacceptable Behavior /
Scope / Enforcement / Reporting). Copy verbatim, swap project name.

**CONTRIBUTING.md** — TestingKit's is the canonical lean template:

```markdown
# Contributing to <Project>

Thanks for your interest in contributing to **<Project>**, part of the
[Phenotype](https://github.com/KooshaPari) ecosystem.

## AgilePlus spec mandate
All non-trivial work is tracked in AgilePlus. Before PR: check registry, create
spec if missing (`agileplus specify --title "..." --description "..."`), link from PR.

## Build & test
<polyglot bash block per language>

## Branch naming
- `feat/<scope>-<short-desc>`
- `fix/<scope>-<short-desc>`
- `chore/<scope>-<short-desc>`
- `docs/<scope>-<short-desc>`
- `refactor/<scope>-<short-desc>`

## Commit messages
Conventional Commits. `feat(scope): ...`, `fix(scope): ...`, etc.

## Pull request expectations
Focused, small, tests pass locally, links to AgilePlus spec, squash-merge default.

## Quality gates
Zero new lint suppressions without justification, traceability to FR IDs,
0-error CI on Linux runners.
```

Dino has a longer version with .NET-specific build instructions — use that
flavour for WorldSphereMod since it's also .NET.

---

## 9. Other recurring patterns

- **Coverage target: 95%+** (in PR template checklist and ci.yml coverage step)
- **`Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>`** on agent-driven commits
- **Pin GitHub Actions by SHA** in every workflow (audit gate would fail otherwise)
- **`docs/qa/<pattern>-allowlist.txt`** files paired with each gate
- **`scripts/ci/detect_<pattern>.py`** detectors are reusable across repos
- **`Directory.Build.props`** centralizes .NET TFM/version pinning per repo
- **AgilePlus mandate** — every non-trivial change tied to a spec ID; tracked via `agileplus` CLI under `/repos/AgilePlus`
- **Kilo Gastown / Convoy** delegation — `gt_sling`, `gt_done`, `gt_mail_send`, `gt_nudge` for multi-agent rigs (see Dino AGENTS.md). Branches `convoy/<feature>/<id>/head`
- **Worktree discipline** — features in `repos/worktrees/<project>/<category>/<branch>` or `repos/<project>-wtrees/<topic>/`; merge back to `main` only
- **File deletion via Recycle Bin** (Dino CLAUDE.md) — never `rm`/`del`; use `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile/DeleteDirectory` with `SendToRecycleBin`
- **Desktop-contamination prevention** — agents never write to `C:\Users\<user>\Desktop\`; outputs go to `docs/sessions/`, `scripts/`, or `$env:TEMP\<Project>\`
- **Hookify rules** — used to enforce things like "no Desktop writes", "no `rm`" via PreToolUse hooks. Live in `.claude/hookify/` or `.claude/settings.json`
- **PR template** — checklist with build/test/lint, schema validation, coverage maintained at 95%+, XML doc comments on all public members, links to ADR/spec
- **`.github/CODEOWNERS`** — `* @KooshaPari` plus narrower overrides for `/docs/`, `/.github/`, `/CHANGELOG.md`, `/src/`, `/schemas/`

---

## Apply-first priorities for WorldSphereMod3D

Current state: governance bundle is already partially present (`AGENTS.md`,
`CHANGELOG.md`, `CODE_OF_CONDUCT.md`, `RELEASING.md`, `SECURITY.md`,
`SUPPORT.md`, `Taskfile.yaml`, `Justfile`, `VERSION` all at root). The big
gaps versus Phenotype norm are:

1. **Spec roots at root**: add `PRD.md`, `SPEC.md`, `ADR.md`, `PLAN.md`,
   `FUNCTIONAL_REQUIREMENTS.md`, `RESEARCH.md` (lift content from `docs/PLAN.md`)
2. **`docs/adr/` + `docs/adr/index.md`** with the 10 phase decisions as ADRs
3. **`docs/journeys/manifests/`** for the 10 phase verification flows (each phase = a journey)
4. **`changelog-lint.yml`** + verify Keep-a-Changelog format is correct
5. **`.github/CODEOWNERS`**, `dependabot.yml`, `release-drafter.yml` config
6. **VitePress in `docs/`** so it can be federated by phenodocs
7. **At least one gate workflow** (e.g. `harmony-patch-gate.yml` for "no Harmony patches on engine internals", mirroring DINOForge ADR-016)
8. **Pin all action SHAs** in existing workflows
