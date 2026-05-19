# ADR 0009 — Voxel lit material chain for phase-1/5 crossover

**Status:** Proposed

**Date:** 2026-05-18

## Context

Voxel entities now resolve a placeholder material at runtime and submit per-instance color via `MaterialPropertyBlock` (`_InstanceColor`).

Current URP-lit choices are constrained to built-in engine shaders and must avoid shipping a custom shader yet.

## Decision

1. Prefer `Universal Render Pipeline/Simple Lit` when available.
2. Fall back to `Universal Render Pipeline/Lit` only if Simple Lit does not expose instancing.
3. Fallback to `Universal Render Pipeline/Unlit` then `Universal Render Pipeline/Particles/Unlit`.

## Material rules

- Simple Lit or Lit: `_BaseColor = white`, `_Smoothness = 0.2f`, `_Metallic = 0.0f`.
- Lit/Simple Lit: attempt `RenderSettings.skybox.mainTexture as Cubemap` and write to `_Cubemap`.
- Unlit/Particles: only `_BaseColor = white`; no cubemap assignment.
- Keep `Graphics.DrawMeshInstanced` shadows as `ShadowCastingMode.On` and `receiveShadows: true`.

## Vertex-color rationale

URP Lit can ignore the mesh color stream in several built-in variants.
Simple Lit is expected to preserve vertex color flow better with minimal shader work.
Particles/Unlit is a deterministic fallback when Lit paths reject instancing or scene constraints block Lit variants.

## Phase 5 follow-up

Phase 5 should introduce `VoxelLit.shader` in the asset bundle:
- explicit vertex-color multiplication with `_InstanceColor` and fallback `_BaseColor`.
- explicit reflection-probe input wiring for stable static and realtime cubemap behavior.
- tuned BRDF branch with configurable gloss/metal controls and optional ambient fallback.
- keep shader-feature gate behind saved settings ship-gates until smoke-tested in-game.
