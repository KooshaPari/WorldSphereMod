# GEMINI.md — Gemini-specific addendum

Follow `AGENTS.md` for the general agent collaboration rules. This file only
covers the Gemini-specific differences.

## Tool availability

You **do** have full Read/Write/Edit access. Don't echo "I can't write files" —
unlike the orchestrating Claude instance in DINOForge, Gemini in this repo is
treated as a normal subagent and can ship code directly.

## Commit hygiene

When asked to make changes:

1. Read the existing file before modifying it.
2. Match the codebase's existing style (block-scoped namespaces, C# 9 syntax —
   `LangVersion=9.0` is pinned in the csproj, so file-scoped namespaces are a
   compile error).
3. Keep edits minimally surgical. If you find a tangential issue, note it in
   the response, do not fix it inline.

## PR review etiquette

When Gemini code-assist comments on a PR (see PR #1's history), the comments
are typically high-quality. Treat them as a code-review pass — apply the
actionable ones, skip the defensive ones with a one-line note in the response.

## Don't reinvent

- `WorldSphereMod.Tools` has 2D↔3D coord helpers, height lookups, camera-facing
  rotation helpers. Don't write your own.
- `WorldSphereMod.Core.Sphere` has the cylindrical/spherical world transform.
- `WorldSphereMod.Voxel.MeshInstanceBatcher` is the shared batched-draw target
  for every mesh-emitting phase.
- `WorldSphereMod.Voxel.WorldUnloadPatch` is the single Harmony Prefix on
  `Sphere.Finish` that drains every fork-side cache. Add to it; don't add
  a parallel teardown patch.

See `AGENTS.md` "What's where" table for the full folder map.
