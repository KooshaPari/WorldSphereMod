# ADR 0009 — Voxel lit material chain for voxels

**Status:** Proposed
**Date:** 2026-05-18

## Context
Voxel entities currently render with placeholder runtime materials and per-instance `_InstanceColor` supplied via `MaterialPropertyBlock`.
Phase 1 still avoids shipping a custom lit shader.

## Decision
1) Prefer `Universal Render Pipeline/Simple Lit` first.
2) Fallback to `Universal Render Pipeline/Lit` when Simple Lit lacks instancing.
3) Then fallback `Universal Render Pipeline/Unlit`, then `Universal Render Pipeline/Particles/Unlit`.

## Material behavior
- Simple Lit/Lit: `_BaseColor = white`, `_Smoothness = 0.2f`, `_Metallic = 0.0f`.
- Lit/Simple Lit: if `RenderSettings.skybox.mainTexture is Cubemap`, write `_Cubemap`.
- Unlit/Particles: keep `_BaseColor = white` only; no cubemap.
- Shadows remain `ShadowCastingMode.On` with receive true on instanced draw.

## Vertex-color rationale
URP Lit may not consistently carry sprite-derived vertex colors in all variants.
Simple Lit is the best low-risk built-in path for tint correctness now.
Particles/Unlit is the deterministic fallback if Lit variants fail capability checks.

## Phase 5 follow-up
Add real `VoxelLit.shader` in AssetBundle with explicit vertex-color and `_InstanceColor` multiply.
Add explicit reflection probe/cubemap sampling in that shader (not skybox-mainTexture cast only).
Add configurable BRDF controls for smoothness/metalness and fallback ambient behavior.
Gate new shader behind SavedSettings ship flag after smoke-test.
