# Upstream Divergence Audit — WorldSphereMod3D

**Date:** 2026-05-30
**Scope:** Both fork pairs against MelvinShwuaner upstreams.
**Fork pairs:**

| Fork (ours) | Branch | Upstream | Status |
|---|---|---|---|
| `KooshaPari/WorldSphereMod` (`E:/Dev/WorldSphereMod`) | `fix/shader-standard-fallback` | `MelvinShwuaner/WorldSphereMod` | merge-base `2188cb7a` (May 18); 10 ahead / **682 behind** |
| `KooshaPari/Compound-Spheres-3D` (submodule `External/Compound-Spheres`) | `wsm3d/main` | `MelvinShwuaner/Compound-Spheres` | 5 ahead / **3 behind** |

**Remote fix applied:** the WSM repo had **no `upstream` remote at all** (not a wrong URL — it was missing, hence `git fetch upstream` exit 128). Added
`upstream = https://github.com/MelvinShwuaner/WorldSphereMod.git` and fetched successfully. The correct upstream repo is `MelvinShwuaner/WorldSphereMod` (confirmed via `gh repo view`; default branch `main`, description "The 3D Worldbox Mod"). Compound-Spheres `upstream` was already correct and fetched.

---

## TASK 1 — Every Melvin commit since 2026-05-15

### WorldSphereMod (upstream/main)

| Hash | Date | Message | Files | Plain-English summary |
|---|---|---|---|---|
| `79bed66e` | 05-29 | update shader | CompoundSpheres.dll/.pdb, AssetBundles (win/osx/linux), CompoundSphereScripts.cs, Core.cs, Tools.cs | Rebuilds the vendored `CompoundSpheres.dll` against the new compute-shader backend (see CS `5b87277`) and re-bakes the 3 platform AssetBundles. Tiny C# tweaks (5 lines) to wire the new backend. |
| `19e73d6c` | 05-29 | update compound spheres | CompoundSphere.shader, dll/.pdb, 3DCamera.cs, CompoundSphereScripts.cs (+45), Core.cs (+26), Mod.cs, SavedSettings.cs, TileMapToSphere.cs (+29), Tools.cs, WorldSphereTab.cs | The big "yesterday" integration. Pulls the rewritten Compound-Spheres backend into the mod: new sphere-manager wiring in `CompoundSphereScripts`, camera tweak, tile→sphere mapping changes, a new saved setting, and shader update. |
| `170faf30` | 05-26 | fixed walls! | Constants.cs, Core.cs, QuantumSprites.cs (+46), Tools.cs (+32) | Adds a `drawWallType` Harmony prefix in `QuantumSprites` that, when `Core.IsWorld3D`, draws wall tile-types with a per-tile Z range / building scale instead of the flat 2D path. Fixes walls not rendering in 3D. |
| `e0d321f3` | 05-26 | improvement | Core.cs, Effects.cs, General.cs, Tools.cs, WorldSphereAPI.cs | Small cross-file cleanup/perf pass; minor API signature touch. |
| `8bc68154` | 05-26 | Update CompoundSphereScripts.cs | CompoundSphereScripts.cs | One-line fix. |
| `6f657cf4` | 05-25 | what the fuck | CompoundSphereScripts.cs, Core.cs, General.cs, Tools.cs, WorldSphereTab.cs, en.json (+72/-72) | Locale re-key (en.json fully rewritten keys) plus backend wiring churn. |
| `aab82167` | 05-25 | cube time | CompoundSphereScripts.cs (+54), Core.cs, General.cs, SavedSettings.cs, Tools.cs (+154) | Introduces a **cube mesh** option for tiles (the upstream equivalent of "voxel-ish" blocky terrain). Large `Tools.cs` growth for mesh/geometry helpers + a saved setting. |
| `71458b69` | 05-25 | yay!!!!!!!!! | 3DCamera.cs, CompoundSphereScripts.cs, Core.cs, General.cs (+20), Tools.cs | Camera + general fixes making the above land visibly. |
| `1544e5e7` | 05-25 | weuhdshujs | 3DCamera.cs, CompoundSphereScripts.cs (+72), Core.cs, General.cs, TileMapToSphere.cs, Tools.cs | Sphere-script refactor / tile-map plumbing. |
| `45297140` | 05-25 | efw | 3DCamera.cs (−42), CompoundSphereScripts.cs (+45), Core.cs | Moves logic out of `3DCamera` into `CompoundSphereScripts`; net code move. |

Also in-window but pre-May-15-relevant baseline (May 18, these ARE our merge-base ancestors): `2188cb7a` merge, `2b0f8da1` Update CompoundSphere.shader, README updates `1ca023bf`/`c36d7e05`/`c4c10ce9`, `909ac08f` Create CompoundSphere.shader.

### Compound-Spheres (upstream/main)

| Hash | Date | Message | Files | Plain-English summary |
|---|---|---|---|---|
| `5b87277` | 05-30 | the compute shader update | BufferUtils.cs (+301/-…), DefaultSettings.cs, DynamicManager.cs, DynamicRow.cs, DynamicTile.cs, ManagerBase.cs, SphereManager.cs, SphereManagerSettings.cs, SphereRow.cs, SphereTile.cs | **Moves matrix + color generation onto the GPU.** Adds `ComputeShader`, `MatrixKernel`/`ColorKernel`, `ComputeBuffer<T>` and `ComputeGraphicsBuffer<T>` types; `Matrixes`/`Colors` are now compute-shader outputs dispatched (`ComputeShader.Dispatch(kernel, TotalTiles/64,1,1)`) instead of CPU-filled structured buffers. |
| `bbf302c` | 05-29 | dynamic management | BufferUtils.cs (+310), DynamicManager.cs (new), DynamicRow.cs (new), DynamicTile.cs (new), ManagerBase.cs (new +272), SphereManager.cs (−), SphereManagerSettings.cs (+204), SphereRow.cs, SphereTile.cs | **Major architectural rewrite.** Extracts a `ManagerBase` superclass and a `Dynamic*` family (`DynamicManager/Row/Tile`) so tiles can be added/removed at runtime. New `BufferBase<T>` with a `NativeArray<T> Data`, a `bool[] Dirty` dirty-flag array, an `IsDirty` flag, and an `Enlarge(newSize)` that grows the `GraphicsBuffer`. `MultiBuffer<T>` for multi-element-per-tile buffers. |
| `b1b7d0a` | 05-27 | new shit | SphereManager.cs, SphereManagerSettings.cs, CompoundSphere.shader | Adds `SetMesh(Mesh)` and `SetRenderAmount(uint)`, caches the `IndirectDrawIndexedArgs[]` command array as a readonly field, minor `Clamp` cleanup, shader tweak. Prep work for the dynamic + compute rewrites. |

Pre-merge-base baseline (May 18, already in our fork): `73a7b77` merge, `c6fa56c` "finally update the damn shader".

---

## TASK 2 — Merged vs Not-Merged

### Compound-Spheres (we are 5 ahead / 3 behind)

| Melvin commit | In our fork? | Notes |
|---|---|---|
| `c6fa56c` finally update the damn shader (05-18) | ✅ MERGED | Our merge-base. |
| `b1b7d0a` new shit (05-27) | ❌ NOT merged | SetMesh/SetRenderAmount + indirect-args cache. |
| `bbf302c` dynamic management (05-29) | ❌ NOT merged | ManagerBase + Dynamic* rewrite + BufferBase/Dirty/Enlarge. |
| `5b87277` the compute shader update (05-30) | ❌ NOT merged | GPU compute matrix/color path. |

Our 5 ahead (all KooshaPari, NOT upstream): `abb54ff` FrustumCuller (05-25), `6e1cf94` HeightFieldRenderer + chunked UpdateBuffer (05-27), `ebe12a8` gate rebuild on tile-dirty (05-28), `7213176` Perlin micro-displacement (05-28), `ae19e1c` corner-averaged water sub-mesh (05-30).

### WorldSphereMod (we are 10 ahead / 682 behind)

Our fork branched at `2188cb7a` (May 18). **None** of Melvin's May 25–29 WSM commits are merged:

| Melvin commit | In our fork? |
|---|---|
| `2188cb7a` and earlier (≤ 05-18) | ✅ MERGED (merge-base) |
| `45297140`, `1544e5e7`, `71458b69`, `aab82167`, `6f657cf4` (05-25) | ❌ NOT merged |
| `8bc68154`, `e0d321f3`, `170faf30` (05-26) | ❌ NOT merged |
| `19e73d6c`, `79bed66e` (05-29) | ❌ NOT merged |

The "682 behind" count is inflated by our hard-fork rewrite history diverging; the substantive missing upstream work is the 10 commits above.

---

## TASK 3 — Eval of yesterday's WSM changes (~05-29)

Two commits: `19e73d6c` "update compound spheres" and `79bed66e` "update shader".

**What they change**
- **Technical:** `19e73d6c` re-integrates the fully rewritten Compound-Spheres backend (the CS `bbf302c` + `5b87277` dynamic + compute pipeline) into the mod. `CompoundSphereScripts.cs` (+45/-…) is re-plumbed to the new `ManagerBase`/compute API; `TileMapToSphere.cs` (+29) changes how world tiles map to sphere tiles; `Core.cs` (+26) and `Mod.cs`/`SavedSettings.cs` add the new setting + wiring; `3DCamera.cs` 1-line. `79bed66e` is purely the rebuilt `CompoundSpheres.dll`/`.pdb` + the 3 baked AssetBundles + 5 lines of glue.
- **User-facing:** higher tile throughput / smoother terrain at scale once the GPU compute path is active; the new cube-mesh terrain option ("cube time", `aab82167`) and fixed 3D walls (`170faf30`) become visible. No new UI surface beyond one saved setting.

**Worth merging?** **Not directly / not soon.** It is tightly coupled to the CS backend rewrite, which we have deliberately *not* taken (see Task 4). The vendored `CompoundSpheres.dll` in `79bed66e` is built against `5b87277`'s compute backend, whereas our fork uses the **submodule** `External/Compound-Spheres` on `wsm3d/main` with our own `HeightFieldRenderer`/`FrustumCuller`. Dropping in Melvin's DLL would bypass our submodule entirely.

**Conflicts with our 3D work (high):**
- `CompoundSphereScripts.cs`, `Core.cs`, `TileMapToSphere.cs`, `3DCamera.cs` are all files our voxel/heightfield/foliage phases also touch — direct textual + semantic conflicts.
- Melvin's backend assumes the new `ManagerBase`/compute API; our `SphereManager` has our `FrustumCuller` + `HeightFieldRenderer` integration that upstream doesn't have. Merging his DLL/scripts would regress our culling/terrain.
- Our README "Backend" section already documents that we replaced the vendored DLL with a submodule build emitting per-vertex normals + water-mask buffer — incompatible with a drop-in upstream DLL.

**Risk:** High churn, low immediate payoff. Recommend: **cherry-pick the *ideas*** — specifically the GPU compute matrix/color path (`5b87277`) and the wall fix (`170faf30`, low-conflict, self-contained `QuantumSprites` prefix) — rather than merging the commits. The wall fix is the single safest pickup.

---

## TASK 4 — Inspiration check (did Melvin echo our work?)

**Timeline (who shipped what first):**

| Concept | Ours | Melvin's |
|---|---|---|
| Per-tile dirty-tracking + incremental buffer update | `6e1cf94` chunked `UpdateBuffer` + `Refresh(maxPerFrame)`, **05-27** | `bbf302c` `bool[] Dirty` + `IsDirty` + frame-aware refresh, **05-29** |
| Frustum/visibility culling of rows | `abb54ff` `FrustumCuller` + DrawTiles integration, **05-25** | (no direct equivalent; he kept camera-range row clamping) |
| Buffer growth/resize | (n/a — fixed `TotalTiles`) | `bbf302c` `Enlarge()` for dynamic add/remove |
| Terrain height field / LOD | `6e1cf94` `HeightFieldRenderer`, **05-27**; Perlin displacement `7213176`, **05-28** | (no equivalent) |
| GPU compute for matrices/colors | (n/a — we stayed CPU-side, frame-budgeted) | `5b87277` ComputeShader dispatch, **05-30** |

**Verdict: mostly INDEPENDENT, with one converging theme.**

- **`dynamic management` (bbf302c):** The *dirty-flag + incremental refresh* theme converges with our chunked `UpdateBuffer` (we shipped 2 days earlier, 05-27 vs 05-29). But the **implementation is architecturally different and arguably his own line of work**: he builds a `BufferBase<T>`/`MultiBuffer<T>` class hierarchy over `NativeArray<T>` (Unity.Collections) with `Enlarge()` for *runtime add/remove of tiles* (a `Dynamic*` manager family) — a feature we don't have. Ours is a thin frame-budget guard (`maxPerFrame`, full-rebuild-when->half-dirty) bolted onto his pre-existing `CustomBuffer`. No shared naming, no copied structure. The convergence is the obvious-next-step kind (any 331K-tile buffer needs dirty tracking), not evidence of copying.
- **`compute shader update` (5b87277):** **Independent and more advanced than ours.** He moved matrix/color generation onto the GPU (`ComputeShader.Dispatch`, kernels, `ComputeGraphicsBuffer<T>`). We explicitly did *not* go compute (memory: "determinism not required" but we stayed CPU frame-budgeted). No overlap in approach; if anything this is the path we'd converge toward, not the reverse.
- **`new shit` (b1b7d0a):** Pure plumbing (`SetMesh`/`SetRenderAmount`, cached indirect-args). No relation to our `FrustumCuller`.
- **`FrustumCuller` / instancing:** **No echo.** Melvin kept his original camera-range row clamping; he did not add a frustum culler. Our `FrustumCuller` (05-25) has no upstream analogue.

**Evidence summary:** No copied symbols, no mirrored file structure, no comment/naming overlap. The single thematic convergence (dirty-tracked incremental buffer refresh) is a standard solution both arrived at; ours predates his by ~2 days but the designs are unrelated. **Conclusion: Melvin's recent work appears independent.** Both forks are independently attacking the same 331K-tile throughput problem from different angles — ours CPU-frame-budget + culling + heightfield terrain; his GPU-compute + dynamic tile management.

---

## TASK 5 — Change matrices

Written into both READMEs (`E:/Dev/WorldSphereMod/README.md` and `External/Compound-Spheres/README.md`). See the "Divergence from upstream" section in each.
