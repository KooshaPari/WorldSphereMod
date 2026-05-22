# ADR-0020 — Wave-26 follow-up: docs posture + rig inventory

## Status

Accepted

## Context

Wave-26 in `v2.0.0-alpha.15` was intended as a light hardening pass:

- align docs and deployment workflow posture for Vercel-hosted docs preview/build verification, and
- make rig support state explicit through an auditable asset registry for skeletal mapping.

This keeps future rig/runtime work grounded in a documented contract instead of implicit
inference from code-only dictionaries.

## Decision

Document Wave-26 outputs as a tracked architecture follow-up record and record rig coverage using a canonical inventory so downstream work and release notes can cite exact asset/rig expectations.

### Implementation

- Track the Wave-26 commit that introduced:
  - Vercel deploy documentation/support updates, and
  - a formal rig asset inventory listing and defaults.
- Keep inventory semantics discoverable in repo docs as the canonical source for rig type coverage and fallback behavior.

## Consequences

### Positive

- Faster onboarding for doc/deploy and animation follow-up work.
- Reduced ambiguity about which actor IDs currently map to rig types and what falls back to default.

### Negative

- Adds ongoing documentation debt unless inventory and deployment notes are kept in sync with code changes.

### Neutral

- No runtime behavior changes; no runtime perf impact.

## Wave-26 commit SHAs

- `db9bcc5855fa7f862ad1a6565388847a6de51895` — Vercel deploy doc + rig asset inventory.

## References

- `docs/release-notes/v2.0.0-alpha.15.md`
- `docs/journeys/scratch/rig-asset-inventory.md`
- `vercel.json`
- `WorldSphereMod/Code/Constants.cs`
