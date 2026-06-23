# Foresight: Melvin's Trajectory + Our Gap Analysis (WorldSphereMod3D)

Date: 2026-05-30. Author: research agent (investigation only, no code changes).
Scope: MelvinShwuaner/WorldSphereMod (`upstream`) + MelvinShwuaner/Compound-Spheres (`upstream` in `External/Compound-Spheres`). Both fetched.

---

## TL;DR (read this first)

- **Melvin is building a generic, GPU-driven instanced rendering engine** (Compound-Spheres) and using WorldSphereMod as its first consumer. He is not "porting WorldBox to 3D" the way we are — he is building the *substrate* that makes a 3D WorldBox cheap, then re-deriving terrain/walls/tiles on top of it.
- **He has out-shipped us on the core because he moved the work down one layer** (into the engine: compute kernels + GPU buffers + indirect draw + per-row GPU frustum culling), while **we have been fighting symptoms one layer up** (main-mod overlays, shader-variant rebakes, billboard suppression).
- **His GPU-compute path very likely obviates our entire magenta / instanced-shader-variant fight.** Matrices and colors live in `StructuredBuffer`s and are produced by compute kernels; draws go through `DrawMeshInstancedIndirect`. There is no `Graphics.DrawMeshInstanced` per-instance `_Color` cbuffer to read uninitialized garbage, and no `INSTANCING_ON` keyword variant to fail cross-Unity-version. **The 60f1 rebake is treating a wound we should stop inflicting on ourselves by adopting his buffer-driven shader.**
- **We are genuinely ahead in breadth/gameplay-visual layers he hasn't touched**: voxelized actors/items, anatomical rigging, water/liquid surfaces, foliage, worldspace UI, and (critically) the **headless bridge + typed diagnostics + screenshot/CI verify loop**. Those are real differentiators worth keeping — but they should sit *on top of his engine*, not on top of our divergent fork.
- **We are 688 commits ahead / 10 behind**, and the merge-base is `2188cb7a` — i.e. **we forked off right before his cube-terrain, fixed-walls, and compute work landed.** Every day we add overlays on the pre-cube base, the eventual merge gets more painful.

---

## 1. Melvin's inferred roadmap / trajectory (with evidence)

### 1a. Compound-Spheres — the engine

Recent commit arc (`upstream/main`): `... -> SphereManager refactors -> CompoundSphere.shader (GPU buffers) -> "new shit" -> "dynamic management" (bbf302c) -> "the compute shader update" (5b87277)`.

What the code now is (read from `upstream/main`):

- **`BufferUtils.cs`** — a full generic GPU buffer abstraction layer:
  - `BufferBase<T>` with a **dirty-range coalescing `Refresh()`** (walks the dirty flags, batches contiguous dirty spans into minimal `SetData(start,count)` calls). This is real perf engineering — partial buffer uploads, not full re-uploads.
  - `GraphicsBufferBase<T>`, `ComputeBuffer<T>`, `MultiBuffer<T>` (interleaved item arrays), `Buffer<T>`.
  - **`Enlarge(newSize)`** on every buffer type → dynamic growth without teardown. This is the BufferBase/Enlarge the brief mentions.
  - `ComputeGraphicsBuffer<T>` — **the keystone**: a `GraphicsBuffer` written by a compute kernel, with its own GPU `Dirty` buffer; `Refresh()` uploads dirty flags then `Shader.Dispatch(kernel, threadCount,1,1)`. The CPU never computes a matrix or a color — it just flips dirty bits.
  - `Wrapped*Buffer` + `IBuffer` → **user-extensible custom per-tile GPU attributes** via `AddCustomBuffer`. He is deliberately making the engine pluggable.
- **`ManagerBase<T>` / `ManagerRoot`** — abstract base holding `Positions` (ComputeBuffer), `Matrixes` and `Colors` (`ComputeGraphicsBuffer`), `Scales` (Buffer), a `CustomBuffers` dict, `MatrixKernel`/`ColorKernel`. `Begin()` seeds positions then dispatches the matrix + color kernels. **Tiles are pure data (`TileBase`: index/X/Y/scale/color); all transform + color math is on the GPU.**
- **`DynamicManager` / `DynamicRow` / `DynamicTile`** (added in `bbf302c`, refined in `5b87277`):
  - Rows of **varying, growable length** (`Enlarge`), tiles can be toggled/added/removed at runtime.
  - **Per-row GPU frustum culling**: `DynamicRow` runs a `Culler` compute shader (`CSMain`) over an `Append`-type `VisibleIndices` buffer, `CopyCount` into an indirect-args buffer, then `DrawMeshInstancedIndirect`. Visibility is decided on the GPU; the CPU issues one indirect draw per visible row.
  - Texture atlas + per-tile texture index (`Textures` buffer, `UVs`/`Atlas`), `ShouldRenderTextures` display modes baked into the shader.
- **`CompoundSphere.shader`** — reads `Matrixes`, `Scales`, `Colors` (packed uint), `Textures` from `StructuredBuffer`s, indexes by `SV_InstanceID + Row + Col`, samples a `Tex2DArray` atlas. **Color is unpacked from a uint buffer in the fragment shader — there is no per-instance material cbuffer at all.**

**Trajectory for Compound-Spheres:** a reusable **GPU-resident, indirect-drawn, dynamically-streaming tile engine** with GPU culling, dynamic LOD-ish row streaming, atlas texturing, and pluggable per-tile GPU buffers. The architecture is trending toward: *everything per-tile lives in a GPU buffer; the CPU only marks dirty and issues indirect draws; the engine is generic enough to render any grid-of-instanced-meshes world (spheres, cubes, walls, and plausibly actors/items later).* This is exactly the substrate needed for **big maps at high FPS** — which is the implicit goal.

### 1b. WorldSphereMod — the consumer

Recent arc: `efw -> weuhdshujs -> "yay!!!" -> "cube time" (aab82167) -> "what the fuck" -> CompoundSphereScripts -> improvement -> "fixed walls!" (170faf30) -> update compound spheres -> update shader`.

- **`cube time` (aab82167)**: ~220 lines across `Tools.cs`, `Core.cs`, `CompoundSphereScripts.cs` — switched the tile primitive to **cubes** (cube terrain) wired through the Compound-Spheres manager. This is the "real" 3D terrain block, done *in-engine via the instanced mesh*, not via our per-zone slope-mesh / HeightFieldRenderer overlay.
- **`fixed walls!` (170faf30)**: introduced `getVariation(WorldTile)` and made `DisplayedType` wall-aware (`!pTile.Type.wall`) plus `QuantumSprites` additions. He fixed 3D walls **inside WorldBox's own tile-type/sprite pipeline** (main_type vs Type vs wall, edge variation frames) — i.e. at the data-model source layer, not by drawing wall geometry overlays.

**Trajectory for WSM:** keep the mod thin — it feeds WorldBox tile/sprite/type data into the Compound-Spheres engine and lets the engine render. As the engine gains atlas texturing, dynamic rows, and GPU culling, WSM inherits big-map performance, texture-mapped tiles, and dynamic terrain edits "for free." Expect next: **texture atlas wired to WorldBox sprites, more tile/wall variants on the GPU, height/elevation as a per-tile buffer, and streaming for large worlds.**

---

## 2. Gap analysis — where we are missing / wrong / divergent

### 2a. We are 10 commits behind the work that matters, and forked before it
- Merge-base `2188cb7a`; the 10 commits we lack are *exactly* `cube time`, `fixed walls!`, and the CS `dynamic management` + `compute shader update`. We are **688 ahead on a pre-cube base.** Our entire overlay stack sits on top of a WSM that predates his real 3D terrain and wall solutions.

### 2b. We reinvented his engine instead of adopting it
- Our CS submodule is pinned to `1c6c068` (our own `feat(gpu): author CompoundSphereCompute.compute + P1 legacy-API shim seam`). **We hand-wrote a `.compute` to satisfy a contract, but we are NOT on his `5b87277` compute rewrite** (`git merge-base --is-ancestor 5b87277 HEAD` → MISSING). We built a *shim* toward his old API surface rather than pulling his finished `ManagerBase` + `DynamicManager` + `ComputeGraphicsBuffer`.
- He already solved, better, several things we are independently building: dirty-range coalesced buffer uploads, dynamic buffer growth, GPU frustum culling + indirect draw, atlas texturing. Our `MeshInstanceBatcher` / `MeshInstanceBatcherBRG` / `VoxelMeshCache` / `ProxyMeshCache` partially overlap this — and we're doing it the hard way (CPU-side batching, shader-variant wrangling) on Unity's `DrawMeshInstanced`, which is the exact API that gave us the magenta bug.

### 2c. We are fighting symptoms at the wrong layer
Our recent commit log is a parade of symptom-fixes: `kill neon-magenta+green actors`, `enforce voxel-or-invisible — never billboards`, `clamp oversized 3D nametag scale`, `stop actor LOD wave, shrink oversized voxels`, `revert SafeShaders`, `wire ResolveShader Standard-only fallback`. These are real bugs — but most are **artifacts of the path we chose** (bundled instanced shader variant + cross-Unity-version bake + CPU instancing), not intrinsic to 3D WorldBox. Melvin doesn't have these bugs because his color/matrix path never touches a per-instance material cbuffer or an `INSTANCING_ON` variant.

### 2d. Does his GPU-compute path obviate the magenta / instanced-shader fights? **Yes — almost entirely.**
Our own root-cause note (commit `9afca1e8`) says it precisely: the bundled `OpaqueVertexColor` shader's `INSTANCING_ON` variant doesn't survive a 62f3-baked / 60f1-runtime load, so `DrawMeshInstanced` falls to `Hidden/InternalErrorShader` (magenta) and the per-instance `_Color` cbuffer reads garbage (green).
- In Melvin's path: **color is a `StructuredBuffer<uint>` unpacked in the fragment shader** (`GetColor(instanceID)`), and the transform is a `StructuredBuffer<float4x4>` produced by a compute kernel. **There is no `_Color` per-instance cbuffer and no instancing keyword variant to fail.** Draws are `DrawMeshInstancedIndirect`.
- Therefore: the magenta-variant failure mode does not exist on his pipeline. **The 60f1 rebake is the wrong long-term fix** — it keeps us on the fragile bundled-instanced-shader path. The right fix is to render through his buffer-driven shader (or one modeled on it). Rebake is at best a stopgap to keep the current build alive while we migrate.
- Caveat: adopting his shader means our voxel meshes must feed matrices/colors as GPU buffers (we already author per-instance matrices in `MeshInstanceBatcher`), and our atlas/texture needs mapping to his `Textures`/`Atlas` scheme. Non-trivial but strictly less fragile than chasing variant bakes across Unity versions.

### 2e. Generic R&D / engineering steps — how are we doing?
- **Observability / diagnostics:** GOOD and *ahead of upstream* — typed `RenderErrorMarkers` + `RenderErrorRegistry` + `/diag/errors`, `InitProfiler`, `HealthCheck`, gating per-frame logs behind `ProfilerDump`. Melvin has none of this. Keep it.
- **Cheap verify loop:** GOOD — `AutoScreenshotDriver`, `ScreenshotCapture`, `wsm3d-capture` (Rust), `voxel-regression.yml` CI, headless bridge. This is the vision-verify discipline. Keep and double down.
- **Upstream tracking:** WEAK until just now (we added a divergence matrix in `d987309a` / README) but **we still forked pre-cube and haven't merged his core.** The discipline exists on paper; we haven't executed the merge.
- **Fix-at-source-layer:** WEAK — Melvin fixes walls in WorldBox's tile-type model; we tend to overlay. He pushes work down into the engine; we overlay up in the mod.
- **Perf-first:** WEAK relative to him — he coalesces dirty ranges + GPU-culls + indirect-draws from day one. We hit a CPU-bound redraw hang (`NFR-WSM-001`, commit `8d7a0c87`) and chunked it; he avoided the class of problem by design.

---

## 3. Our genuine differentiators (keep / double down)

These are real and Melvin is *not* building them. They are the reason the fork should exist at all:

1. **Voxelized actors + items** (`SpriteVoxelizer`, `VoxelRender`, `VoxelMeshCache`, `VoxelDiskCache`, `ProxyMeshCache`) — turning WorldBox sprites into 3D volumetric meshes. Melvin's tiles are flat instanced meshes; he has no actor voxelization.
2. **Anatomical rigging / templates** (`AnatomicalTemplatePipeline`, `Registry`, `Validation`, `Rig/`) — humanoid rig, body parts, skeletal animation toggle. Pure differentiation.
3. **Water / liquid surfaces** (`Water/` + `GerstnerWater.shader`, corner-averaged water sub-mesh in our CS fork's `HeightFieldRenderer`). Melvin has no liquid.
4. **Foliage** (`Foliage/`, `CrossedQuadFoliage`) — 3D trees.
5. **Worldspace UI** (`Worldspace/`, 3D nametags).
6. **Headless bridge + diagnostics + CI verify** (`Bridge/`, `wsm3d-mcp`, `wsm3d-headless`, screenshot capture, typed error registry, voxel-regression workflow). This is *engineering infrastructure* that lets an agent fleet iterate with a real feedback loop. It is the single most valuable thing we have that upstream lacks, and it compounds.
7. **PBR / post-FX stack** (`StratumVoxelPBR`, `ScreenSpaceAO`, `ScreenSpaceGI`, `BrpBloom`, ACES, `ProceduralSky`) — visual fidelity well beyond upstream's unlit atlas shader.

The strategic point: **all seven of these are layers that belong ON TOP of a fast GPU tile engine.** They do not require us to own the tile engine. Today we own (a worse version of) the tile engine *and* the differentiators, and the tile-engine ownership is what's bleeding us.

---

## 4. Recommendations: STOP / ADOPT / DOUBLE-DOWN + sequencing

### STOP (wasted / divergent effort)
- **STOP the 60f1 shader rebake as a strategy.** It perpetuates the fragile bundled-instanced-variant path. Allow at most one more rebake as a stopgap, time-boxed, explicitly labeled temporary.
- **STOP hand-maintaining our own CPU instancing + buffer plumbing** (`MeshInstanceBatcherBRG`, bespoke buffer growth) as long-term infrastructure — Melvin's `BufferBase`/`Enlarge`/`ComputeGraphicsBuffer` is better and is the merge target.
- **STOP adding new overlays on the pre-cube base.** Anything new that touches terrain/walls/tiles is likely to collide with `cube time` + `fixed walls!`.
- **STOP reinventing GPU culling / LOD on the CPU** — his per-row GPU cull + indirect draw supersedes the actor-LOD-wave work that already caused bugs (`0176dec0`).

### ADOPT (from upstream — adopt, don't reinvent)
1. **Merge Compound-Spheres `upstream/main` (through `5b87277`) into our CS fork.** Get `ManagerBase`, `DynamicManager`, `ComputeGraphicsBuffer`, the dirty-range buffers, and his `CompoundSphere.shader`. Reconcile against our `HeightFieldRenderer` + water sub-mesh additions (rebase ours onto his, since his is now the base substrate).
2. **Adopt his buffer-driven shader as the rendering path for tiles** (and evaluate it for voxel actors), retiring the bundled `OpaqueVertexColor` instancing variant. This is the actual fix for magenta/green.
3. **Merge WSM `upstream/main` (the 10 commits): cube terrain + fixed walls.** Adopt his `getVariation` / wall-aware `DisplayedType` instead of any wall overlay we have.
4. **Adopt his "fix at the WorldBox data-model layer" instinct** for tile/wall/sprite issues generally.

### DOUBLE-DOWN (our differentiators)
- Voxel actors/items + anatomical rigging, water, foliage, worldspace UI, PBR/post-FX — **re-platform these to sit on his engine.** Feed voxel-actor matrices/colors through his buffer/shader path so they inherit GPU culling + indirect draw and stop hitting the variant bug.
- **The headless bridge + typed diagnostics + screenshot/CI verify loop is our crown jewel** — invest here. It's how the agent fleet gets outcome-per-token: every change verified by pixels, not logs (per the vision-verify charter).

### SEQUENCING (max outcome-per-effort)
1. **(Unblock) Merge both upstreams now.** CS `5b87277` + WSM 10-commit cube/walls. Rebase our HeightField/water/voxel additions on top. This single act erases the magenta class of bugs and gives us his terrain/walls. Expect merge pain *now* (it only grows).
2. **Re-route tile rendering through his buffer-driven shader**; delete the bundled-instanced-variant dependency. Verify with the screenshot/CI loop (pixels, not telemetry).
3. **Re-platform voxel actors onto `ComputeGraphicsBuffer` + his shader** (matrices/colors as GPU buffers). This kills magenta/green for actors too and gives them GPU culling.
4. **Then resume differentiator work** (water/foliage/rig/PBR/worldspace UI) on the new base.
5. **Keep diagnostics + verify loop running throughout** — it's the safety net that makes the merge survivable.

### The retro lesson, applied
We out-typed Melvin and under-shipped him because we picked a layer (overlays + CPU instancing + shader-variant bakes) where effort doesn't convert to outcome. He picked the engine layer, where one compute kernel replaces a hundred CPU fixes. **Move our effort down to his engine (adopt it), keep our differentiators up top, and let the verify loop convert effort to confirmed pixels.** Outcome > tokens.

---

## Evidence appendix (commits / files read)
- CS `upstream/main`: `5b87277` (compute update), `bbf302c` (dynamic management), `b1b7d0a` (new shit). Files: `BufferUtils.cs`, `ManagerBase.cs`, `DynamicManager.cs`, `DynamicRow.cs`, `DynamicTile.cs`, `Default Assets/CompoundSphere.shader`.
- WSM `upstream/main`: `aab82167` (cube time), `170faf30` (fixed walls), `e0d321f3`, `8bc68154`, `79bed66e`. Wall fix in `WorldSphereMod/Code/Tools.cs` (`getVariation`, wall-aware `DisplayedType`).
- Our fork: 688 ahead / 10 behind, merge-base `2188cb7a`. CS submodule pinned `1c6c068`; `merge-base --is-ancestor 5b87277 HEAD` = MISSING (we do NOT have his compute rewrite). Our magenta root-cause: commit `9afca1e8`. Differentiator dirs: `WorldSphereMod/Code/{Voxel,Water,Foliage,Rig,Worldspace,Bridge,Renderer}`, `Tools/wsm3d-*`, shaders under `Tools/Unity-Bake-Project/Assets/WSM3D/Shaders`.
