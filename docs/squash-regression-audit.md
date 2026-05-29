# Squash-Merge Regression Audit — PR #7

**Date:** 2026-05-28
**Auditor:** automated tree comparison
**Question:** Did the squash-merge of PR #7 (`claude/research-ultraplan-fork-DdgI5`, 232 commits) into `main` (squash commit `4efa128`) drop or regress any critical fixes that landed on the dev branch?

## Refs compared

| Ref | SHA |
| --- | --- |
| Current tree (`feat/phase-7-ui-kickoff`) | `713338ec9d94125fad33b18b60227c5efdf816af` |
| Dev branch (`origin/claude/research-ultraplan-fork-DdgI5`) | `3bbc7bef821a6d824f83501802c456a5b6e8fc32` |

`git merge-base --is-ancestor` confirms dev is **NOT** an ancestor of HEAD — consistent with a squash merge, so a line-by-line comparison is the correct audit method.

## Verdict: NO REGRESSION IN THE 11 AUDITED FIXES

All 11 critical fixes are **PRESENT** in the current `feat/phase-7-ui-kickoff` tree. The **MISSING list is empty.**

## Per-fix results

| # | Fix | Status | Evidence (current tree) |
| --- | --- | --- | --- |
| 1 | `SafeShaders` = only `OpaqueVertexColor` (Core.cs) — CRASH FIX | **PRESENT** | `Core.cs` array contains only `"OpaqueVertexColor"`; all 7 other names trimmed. Core.cs differs from dev only in comment wording (the array bytes are identical). |
| 2 | `ActorVoxelEmit.EmitVoxels` has `[HarmonyPriority(Priority.First)]` | **PRESENT** | `VoxelRender.cs:534` on actor `EmitVoxels`; also `:830` on building `EmitVoxels`. Includes a "REGRESSION GUARD: must stay Priority.First" comment. |
| 3 | `VoxelFrameDriver.LateUpdate` calls `Flush` UNCONDITIONALLY | **PRESENT** | `VoxelRender.cs:1653` calls `VoxelRender.Flush()` with no gate; `HasPendingSubmissions` is read at `:1651` only as an observability snapshot (`hadPending`). Explicit BUG-FIX comment documents the un-gating. |
| 4 | `CubemapLighting` uses `AmbientMode.Trilight` not `Skybox` | **PRESENT** | `CubemapLighting.cs:178` `RenderSettings.ambientMode = AmbientMode.Trilight;` with pale-blue-fix comment at `:175-176`. |
| 5 | `SunRig.Drive` writes Trilight bands not `ambientLight` | **PRESENT** | `SunRig.cs:30-32` writes `ambientSkyColor` / `ambientEquatorColor` / `ambientGroundColor`; forces Trilight at `:26-28`; comment notes `ambientLight` is ignored. |
| 6 | `WaterMaskBuffer` SeaLevel = `TrueHeight(17) - 0.5`, IsWater requires `liquid|ocean && !sand` | **PRESENT** | `WaterMaskBuffer.cs:28` `SeaLevel = Tools.TrueHeight(17) - 0.5f;`; `:48-49` `(tt.liquid || tt.ocean) && !tt.sand`. |
| 7 | `LodSelector` `_entityHeight` tracks `VoxelScaleMultiplier` at runtime | **PRESENT** | `LodSelector.cs:51` reads `Core.savedSettings.VoxelScaleMultiplier` each call; `:56` `entityHeight = _baseEntityHeight * voxelScale`. |
| 8 | `MeshInstanceBatcher` `_bakeEmission` is NOT black | **PRESENT** | `MeshInstanceBatcher.cs:68` `_bakeEmission = new Color(0.15f, 0.15f, 0.15f, 1f)`. |
| 9 | `VoxelMeshCache` per-sprite placeholders + flat-sprite fallback for BuildFailed | **PRESENT** | `VoxelMeshCache.cs:97` `_spritePlaceholders` dict; `:526` `BuildFlatSpriteMesh` fallback on `BuildFailed` (guarded at `:521`). |
| 10 | `LoadAllAssets` diagnostic block REMOVED from Core.cs | **PRESENT (removed)** | No `LoadAllAssets` call remains; `Core.cs:1247` logs that the enumeration is "intentionally skipped (ADR-0013)". Identical to dev. |
| 11 | NML compat: `tiles_list.Length` fix (no method-group error) | **PRESENT** | `tiles_list` is consumed as a `WorldTile[]` and `.Length` is read off the array local in every site (`WaterMaskBuffer.cs:30-31`, `Core.cs:768-769`, `WaterSurface.cs:125-126`, `VoxelMeshPrewarmPass.cs`, `TerrainSmoothing.cs`). No method-group invocation. |

## MISSING list (ranked by severity)

**None.** No audited fix is missing from the current tree.

## Other divergences from dev (NOT in the audited set — informational)

The full `*.cs` diff between dev and the current tree touches only 6 files. None drop an audited fix. Two are behavioral and worth noting:

1. **`Effects.cs` — double-shadow suppression block absent in current tree (dev has it).**
   Dev contains, current tree lacks:
   ```csharp
   // Phase 5: when the real sun is casting cascaded shadows AND voxel entities
   // are rendered, the legacy flat sprite shadow produces a double-shadow artifact.
   if (WorldSphereMod.Lighting.SunDriver.Active && Core.savedSettings.VoxelEntities)
   {
       return false;
   }
   ```
   This block was already absent at squash commit `4efa128` (no removal commit exists on the current branch history for the marker string). It is a Phase-5 cosmetic shadow suppression, outside the 11 critical fixes. **Possible minor visual regression (double shadows when sun shadows + voxel entities are both on); flag for the fix agent to confirm whether the squash dropped it or it was intentionally superseded.**

2. **`Core.cs` and `WaterSurface.cs` — comment-only differences.** No functional change.

3. **Test files** (`MeshWaterInvariantsTests.cs`, `ToolsInvariantsTests.cs`, `VoxelPipelineRegressionTests.cs`) differ; these are current-tree test alignments (e.g. commit `aad45f7` "align SafeShaders and Tools invariants"), not dropped product fixes.

## Method

For each fix, the function/region in the current working tree was inspected and, where the file differed from dev, diffed via `git diff origin/claude/research-ultraplan-fork-DdgI5 -- <path>`. Of the audited files, only `Core.cs` differs from dev, and that difference is comment text — the `SafeShaders` array itself is byte-identical.
