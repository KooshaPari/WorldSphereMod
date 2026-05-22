# WSM3D — Dependency Inventory + SOTA Build-vs-Buy Status

Comprehensive answer to the "what happened to CompoundSpheres fork +
SOTA/wrap-vs-buy research" follow-ups.

## 1. CompoundSpheres

**Status:** SHIPPED AS-IS, no fork, no complement library.

| Fact | Value |
|---|---|
| Origin | Melvin Shwuaner (upstream WorldSphereMod author) |
| File | `WorldSphereMod/Assemblies/CompoundSpheres.dll` (23 KB) |
| What it does | 2D grid → 3D sphere mapping at high efficiency |
| Source available? | **YES** — https://github.com/MelvinShwuaner/Compound-Spheres (7 C# files: BufferUtils, CompoundSpheres.csproj, DefaultSettings, SphereManager+Settings, SphereRow, SphereTile) |
| Used by | `Mod.cs`, `Tools.cs`, `Core.cs`, `WaterRender.cs`, `TileMapToSphere.cs`, `CompoundSphereScripts.cs` (6 files, ~60 `using CompoundSpheres;` references) |
| Fork decision | **Fork CANDIDATE** — source now confirmed available at MelvinShwuaner/Compound-Spheres. We can clone it as a submodule under WorldSphereMod/External/Compound-Spheres/, contribute fixes upstream, or fork to our own Phenotype-org for divergent helpers. Action: spawn agent to clone + integrate as build-time dep. |
| Risk | If Melvin's DLL has bugs / breaks on a WB update, we're stuck without source. CLAUDE.md flags: "CompoundSpheres.dll is a runtime dep, not stale. Must stay shipped" |

**Complement library plan (not built):** considered building a thin
`WorldSphereExtras` wrapper to ADD missing helpers without touching the
DLL. Deferred — concrete need hasn't materialized; we can call the DLL's
public surface directly from `Tools.cs`.

## 2. SOTA / wrap-vs-buy research outcomes

10 research docs in `docs/journeys/scratch/replace-*.md` + 1 in `headless-`:

| Component | Research file | Decision | Reasoning |
|---|---|---|---|
| Mesh instancing | replace-mesh-instance-batcher-research.md | **HAND-ROLL** (kept) | Unity's BatchRendererGroup needs URP; SRP-batcher needs `UnityPerMaterial` constants we don't control on Standard. Current `MeshInstanceBatcher` works. |
| Mesh smoothing | replace-mesh-smoother-research.md | **REMOVE** (just done, c714604) | Laplacian breaks disconnected islands → triangle-soup regression. Libigl / OpenMesh too heavy for runtime. Disable. |
| Sprite voxelizer | replace-sprite-voxelizer-research.md | **HAND-ROLL** (kept) | Magicavoxel/MeshOps need offline pipelines. Our runtime distance-transform balloon + lathe is the only viable option. |
| Rig driver / skel anim | replace-rig-driver-research.md | **HAND-ROLL** (kept) | DragonBones C# MIT considered; pulls in 800 KB + JSON parser. Our `HumanoidRig` static-ctor approach is leaner. |
| Frustum LOD | replace-frustum-lod-research.md | **HAND-ROLL** (kept) | Unity's `OcclusionPortal` infrastructure incompatible with sphere-wrapped world; we use 2D screen-space culling with sphere-wrap correction. |
| Decal particles | replace-decal-particle-research.md | **HAND-ROLL** (kept) | Unity 2022 BRP doesn't expose deferred decal renderer; Sirenix Decal package is paid. Our `DecalPool.Emit` is sufficient. |
| Sky | replace-sky-research.md | **HAND-ROLL** + asset bundle | URP HDRP Sky volume unavailable in BRP. Our ProceduralSky.shader (just shipped df45a97) is the route. |
| Journeys (test harness) | replace-journeys-research.md | **PHENOTYPE-JOURNEYS** (buy) | Existing Phenotype-org tool. Used for capture/verify of phase milestones. |
| Concurrent collections | concurrent-collections-research.md | **System.Collections.Concurrent** (buy/stdlib) | ConcurrentQueue + ConcurrentDictionary already in .NET Standard. No need for custom MPMC queue. |
| Headless rendering | headless-rendering-research.md | **DEFER** | Unity headless mode loses graphics pipeline; tested via Docker without practical render output. Real test harness pending Unity Editor batch mode. |

### Wrap-over-handroll wins (EP-1 in PRD)

| Buy | Library | Reason |
|---|---|---|
| ✅ HarmonyLib (existing) | Patching framework | Industry standard for Unity mods |
| ✅ NeoModLoader (existing) | Mod loader | WorldBox-specific; no alternative |
| ✅ Newtonsoft.Json (existing) | JSON serialization | Already in WB runtime |
| ✅ FluentAssertions (test deps) | xUnit assertions | Industry standard |
| ✅ Phenotype-journeys | Test-orchestrator | Org-internal, already shipping |

### Hand-roll decisions (with rationale)

| Hand-roll | Reason |
|---|---|
| `SpriteVoxelizer` | Sprite-to-voxel is bespoke to WB's PPU=1 sprite system |
| `MeshInstanceBatcher` | Unity instancing requires shader keyword chain we control |
| `LodSelector` | Sphere-wrapped world breaks built-in frustum culling |
| `AssetShapeRegistry` | Asset-id → shape-hint is WB-specific |
| `BridgeServer` (HTTP RPC) | Lightweight + Unity-friendly; full HTTP framework would be 5 MB+ |
| `VoxelMeshCache` | Need Mesh + Snapshot + LRU + thread-safe; off-the-shelf only solves 2 of 4 |
| Custom shaders (`OpaqueVertexColor`, etc) | Forced by Unity BRP shipping only `Standard` + `Sprites/Default` at runtime |

## 3. Build-vs-buy meta-strategy

Decision tree (from EP-1):

```
Library exists?
   ├─ No  → Hand-roll
   └─ Yes
        Will it run on Unity 2022 Mono?
           ├─ No  → Hand-roll (or wrap via reflection if WB has it)
           └─ Yes
                Heavier than the surface we use?
                   ├─ Yes (>10x LOC) → Hand-roll
                   └─ No  → Buy / wrap
```

Applied across the 11 components → 9 hand-roll, 2 buy. Unity 2022 BRP
mod constraints push almost everything to hand-roll because most modern
graphics libraries (URP plugins, HDRP-only features, DOTS-based renderers)
either don't run in our environment or carry constraints we can't satisfy.

## 4. What's still open

- **Forward+ renderer** (`docs/specs/forward-plus-renderer-spec.md`,
  shipped df45a97) — 1-2 week effort; hand-roll Forward+ via CommandBuffer
- **OnRenderImage PostFX stack** (`docs/specs/onrenderimage-postfx-spec.md`,
  shipped df45a97) — 1-2 day effort
- **AssetBundle shader bake** — half-day Unity Editor session;
  7 .shader sources ready (df45a97 WorldSphereMod/AssetBundles/Shaders/)
- **CompoundSpheres source recovery** — if Melvin publishes source, fork
  + add the helpers we need rather than wrapping reflectively

## TL;DR

- **CompoundSpheres**: ship as-is, no fork (source unavailable), no
  complement lib (no concrete need yet).
- **SOTA build-vs-buy**: 9 hand-rolls (mostly because Unity 2022 BRP
  graphics stack is constrained) + 2 buys (Phenotype-journeys, std-lib
  ConcurrentCollections). All 10+ research docs in
  `docs/journeys/scratch/replace-*.md`.
