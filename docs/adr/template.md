# ADR-NNN: Title

<!--
  Filename convention: ADR-NNN-<kebab-title>.md (Phenotype org standard,
  per docs/phenotype-conventions.md §5). The existing ADRs 0001-0005
  predate this convention and ship as NNNN-kebab.md — grandfathered to
  avoid breaking inbound links. New ADRs MUST use the ADR-NNN-<kebab>.md
  form. See ADR.md at repo root for the canonical index.
-->


**Status:** Proposed | Accepted | Rejected | Superseded by ADR-NNN | Deprecated

**Date:** YYYY-MM-DD

**Author:** Name / handle

**Stakeholders:** Roles or repos affected

---

## Context

What is the situation that demands a decision? What problem does this
solve? What forces or constraints are in play? Reference any code paths,
upstream behaviors, or external systems that motivate the decision.

### Problem Statement

One paragraph summary of the specific question being decided.

### Forces

Bullet list of constraints, requirements, and trade-offs at play:

- Constraint A
- Trade-off B vs C
- Compatibility / performance / cost requirement D

### Alternatives Considered

| Alternative | Pros | Cons | Why not chosen |
|---|---|---|---|
| Option 1 | ... | ... | ... |
| Option 2 | ... | ... | ... |

## Decision

The chosen approach, stated as a clear declarative sentence. Include
enough detail that a future reader (human or agent) can implement against
it without re-deriving the reasoning.

### Implementation Notes

- File / module touched by the decision
- Settings / API surface affected
- Roll-out steps if non-trivial

## Consequences

### Positive

- What gets easier / cheaper / more correct
- What capabilities this unlocks

### Negative

- What now costs more (build, runtime, maintenance)
- What is now harder or impossible
- What technical debt this introduces

### Neutral

- Things that change but neither help nor hurt

## References

- Related ADRs: ADR-NNN
- Linked design docs: `docs/phase{N}-architecture.md`
- Code anchors: `path/to/file.cs:LINE`
- External references: upstream commits, vendor docs, papers

---

> Phenotype ADR conventions: keep ADRs short (1–3 screens), one decision
> per ADR, link out to architecture / journey docs rather than restating
> them. Status changes (`Accepted` → `Superseded`) are appended at the top
> with a date; don't delete history.
