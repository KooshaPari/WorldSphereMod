# WSM3D governance gaps survey

Ranked by impact on review quality, supply-chain risk, and long-term maintainability. This is a governance survey, not an implementation plan.

| Rank | Item | Current state | Gap | Recommended next move |
|---|---|---|---|---|
| 1 | ADR enforcement | ADRs exist under `docs/adr/` and the PR checklist mentions them, but there is no hard gate. | Architectural changes can still land without a recorded decision, and the repo relies on reviewer memory. | Require an ADR for architecture-affecting PRs via a PR bot check or a Roslyn/analyzer-style policy gate that blocks merge when the ADR link is missing. |
| 2 | API stability promise (`WorldSphereAPI v2`) | The code and README already describe a v2 surface and v1 compatibility fallback. | The promise is informal: there is no semver policy, deprecation window, or breaking-change RFC process for external mods. | Publish a compatibility policy for `WorldSphereAPI` with explicit semver rules and a breaking-change RFC workflow. |
| 3 | Release process | `RELEASING.md` exists and covers tagging, version bumps, and artifact packaging. | The process does not yet cover signing, checksum publication, or a clearly owned distribution checklist for mod releases. | Expand release governance to include signed artifacts, published hashes, and a release owner checklist tied to the GitHub Release. |
| 4 | License audit | `LICENSE` is MIT, but the repo also ships `Assemblies/CompoundSpheres.dll`, AssetBundles, and third-party dependencies. | There is no consolidated bill of materials or license provenance audit for bundled binaries/assets. | Add a dependency and asset license inventory that names every vendored binary, bundle, and external package. |
| 5 | Security policy (`SECURITY.md`) | Present and reasonably complete for reporting and supported versions. | The policy exists, but it is not yet tied to a formal vulnerability response runbook or release cadence. | Keep the file, then add an internal response checklist for triage, acknowledgement, fix, and advisory publication. |
| 6 | CODEOWNERS | No `.github/CODEOWNERS` file exists. | Review routing is manual, so phase owners and subsystem experts are not automatically requested. | Add per-dir ownership for core systems, docs, tests, tooling, and release files. |
| 7 | PR templates | `.github/pull_request_template.md` exists and already checks branch, build, API, docs, and smoke-test items. | It does not force security review, performance budget acknowledgement, or ADR linkage. | Add template checkboxes for security review, perf budget impact, and “ADR required” when architecture changes. |
| 8 | CONTRIBUTING guide | Root `CONTRIBUTING.md` and `docs/CONTRIBUTING.md` already give a usable contributor path. | The guide is helpful but not yet a governance boundary above `CLAUDE.md`; it does not fully define escalation, review routing, or release responsibilities. | Promote it to the canonical contributor guide and add links to review, release, and escalation rules. |
| 9 | Maintainer rotation / bus factor mitigation | No explicit maintainer-rotation policy is documented. | Single-maintainer knowledge concentration remains a risk, especially for releases and architecture approvals. | Document backup maintainers, release delegates, and periodic review ownership rotation. |
| 10 | Code of Conduct | `CODE_OF_CONDUCT.md` already exists. | This is not a gap; the main issue is discoverability and enforcement path, not absence. | Link it from contributor and support docs, and keep enforcement contacts current. |

## Priority summary

The highest-leverage fixes are the ones that prevent silent drift: ADR enforcement, API compatibility policy, and a stronger release process. Those three reduce the chance that a technically correct change still creates an unreviewed contract break or an unverifiable release.

The next tier is supply-chain and review hygiene: license provenance, security response, and CODEOWNERS. Those do not block feature work, but they materially reduce risk and review friction.

The lowest-priority items are already partially present or mostly organizational: PR template expansion, contributor-guide consolidation, maintainer rotation, and the Code of Conduct.
