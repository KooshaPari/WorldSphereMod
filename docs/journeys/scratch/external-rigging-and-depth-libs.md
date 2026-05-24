# External Libraries & Techniques: Voxelization, Rigging, Depth Inference

Research scope: techniques and OSS we can pull from for true 3D voxelization of orthographic 3/4-view WorldBox sprites and skeletal-animation deformation of the resulting voxel meshes. WSM3D is Harmony-patched into WorldBox (Unity 2019.4-era runtime, IL2CPP off, Mono backend) — anything we link must be compatible with that constraint (pure C#, no shaders that require URP/HDRP, no editor-only APIs at runtime).

---

## 1. Distance-Transform Inflation / Sprite → Voxel

### Direct references
- **VoxeloramaExtension** (Pixelorama, GDScript) — https://github.com/Orama-Interactive/VoxeloramaExtension. MIT. 1-pixel = 1-voxel extrusion of layers. **Not** distance-transform inflation; it's a flat extrude. Useful as a sanity baseline for what *not* to do (gives slab-like depth).
- **pixtovox** (threejs/JS) — https://github.com/DiezRichard/pixtovox. MIT. Per-pixel cube placement. Same flat-extrude problem.
- **pixel3d** (Love2D / Lua) — https://github.com/challacade/pixel3d. MIT. Renders pixel-art as voxels with rotation; flat extrude under the hood.
- **euclidean-distance-transform-3d** (seung-lab, Python/C++) — https://github.com/seung-lab/euclidean-distance-transform-3d. BSD-3. Multi-label EDT, marching-parabolas. The reference correct EDT impl; we'd reimplement the kernel in C# (it's ~150 LOC for a 3D Saito-Toriwaki / Felzenszwalb pass).
- **Voxify3D** paper (arXiv 2512.07834) — neural pixel-art→volumetric. Too heavy (model inference at runtime), but the controllable-abstraction loss they describe is conceptually adjacent to inflation depth control.

### The technique we actually want
"Balloon inflation" = treat the silhouette as a binary mask, run **2D EDT** on the mask, then use `depth(x,y) = sqrt(maxDist^2 - dist_to_edge(x,y)^2)` to lift each pixel into a hemispheroid. Three-quarter view variants add a forward-shear before lifting and a back-face EDT pass for the occluded side. No paper canonicalises this — it's folk knowledge from Teddy (Igarashi 1999) and the SDF/SDF-extrusion community ([weesals SDF post](https://weesals.wordpress.com/2021/05/11/generating-sdf-signed-distance-fields-in-unity/), [Bronson Zgeb voxelization](https://bronsonzgeb.com/index.php/2021/05/15/simple-mesh-voxelization-in-unity/)).

**Integration cost:** Low. ~200 LOC C# for 2D EDT (Felzenszwalb 1D-passes) + ~100 LOC for inflation. Already partially scoped in our Phase 2 plan; this confirms there's nothing better off-the-shelf.

---

## 2. Skeletal Animation Runtimes

| Runtime | License | Cost | Runtime DLL size | WSM3D fit |
|---|---|---|---|---|
| **DragonBonesCSharp** | MIT | Free | ~200 KB | Good — pure C#, Unity bindings exist but core is engine-agnostic |
| **Spine** (esoteric) | Spine Runtimes License (requires editor purchase) | Essential $69 / Pro $349 / Ent ~$2.2k per seat | ~250 KB | Legally hostile for a free mod — every contributor would need an editor license. Hard pass. |
| **Unity 2D Animation pkg** | Unity Companion License | Free w/ Unity | n/a (package) | Editor-only rig authoring; runtime is `SpriteSkin` MonoBehaviour. Mostly 2D-in-plane; no 2D→3D path beyond extruding the rigged sprite mesh. |
| **UnitySpritesAndBones** (Banbury) | MIT | Free | n/a | Abandoned (last commit ~2017). Useful for reading bone-weight code only. |
| **Anima2D** | Unity Companion | Free | n/a | Deprecated, folded into 2D Animation pkg. |

### Recommendation
**DragonBones C#** for the animation graph + bone hierarchy (MIT, embeddable, no shader deps), driving **our own 3D skin deformer** that maps DragonBones bones onto our voxel mesh. We do *not* need DragonBones' Unity renderer — only the `Armature` / `Bone` / `AnimationState` classes. Strip the Unity-binding folder, link `src/DragonBones/Scripts/`.

Repo: https://github.com/DragonBones/DragonBonesCSharp

---

## 3. Procedural Rigging of Procedural Voxel Meshes

This is the hardest piece. References:

- **Baran & Popović "Pinocchio" (SIGGRAPH 2007)** — https://www.cs.toronto.edu/~jacobson/seminar/baran-and-popovic-2007.pdf. Canonical auto-rig: fit a template skeleton into a contracted mesh, then **bone heat-equilibrium** weighting. Original C++ source mirrored at https://github.com/elrnv/pinocchio. License: MIT-ish (research). Port effort: nontrivial (~3k LOC) but solid.
- **Volumetric Heat Diffusion Skinning** (Rosen / Wolfire) — https://www.gamedeveloper.com/programming/volumetric-heat-diffusion-skinning. The voxel-aware variant of Pinocchio's heat solver: shoot heat through *connected voxels* instead of euclidean space, which fixes the "weights bleeding through limbs" problem. Perfect fit for us because **we already have the voxel grid** from step 1 — no extra voxelization pass needed.
- **Bronson Zgeb's Unity port** of volumetric heat diffusion — https://bronsonzgeb.com/index.php/2021/06/26/volumetric-heat-diffusion-for-automatic-mesh-skinning/. C#/Unity, full repo. MIT-style on the blog. **This is the closest existing impl to what we need.** ~500 LOC, runs on CPU, no compute shader requirement.
- **sketchpunklabs/autoskinning** — https://github.com/sketchpunklabs/autoskinning. MIT. JS/WebGL prototypes using compute shaders for voxel-bone intersection + shortest-path weighting. Algorithmically reusable, code not directly portable.
- **mattatz/unity-voxel** — https://github.com/mattatz/unity-voxel. MIT. CPU+GPU mesh voxelizers. Useful if we want to feed in non-sprite (e.g. building) meshes for the same pipeline.
- **Dreaming381/Kinemation** — https://github.com/Dreaming381/Kinemation-Skinning-Prototype. DOTS skinning. Overkill / wrong runtime model for WorldBox.
- **HumanRig** (arXiv 2412.02317) & **ASMR** (arXiv 2503.13579) — ML-based humanoid rigging. Out of scope (model weights + GPU inference).

### Recommendation
Adapt Bronson Zgeb's volumetric-heat-diffusion C# directly. Replace its input (a Unity Mesh + voxelizer pass) with our already-computed inflation voxel grid. Bones come from DragonBones armature. Output: `BoneWeight[]` we assign to `Mesh.boneWeights`. Integration cost: **medium** (~1-2 days), pure-CPU, deterministic, cache-friendly per actor archetype.

---

## 4. Single-Image / Ortho-View Depth Inference

Everything modern (MiDaS, Marigold, Depth-Anything-v2, Lotus) is a 200MB+ neural net — won't ship in a WorldBox mod. Classical options:

- **Shape-from-Shading (Lambertian, vertical light)** — arXiv 1502.05197 gives the eikonal formulation. The math is straightforward; for clean orthographic pixel art with flat-lit cells it actually works because the ill-posedness is dominated by the cell colour palette (each colour ≈ a normal cluster).
- **Photometric stereo** — requires multiple lighting directions, not applicable to single existing sprites.
- **Depth from silhouette** (= our inflation approach) — empirically best for WorldBox-style art. The sprites are silhouette-heavy, not shading-heavy.

### Recommendation
**Skip ML.** Use silhouette-inflation as the primary; add a tiny secondary signal by clustering palette luminance into 3-5 depth bins and biasing the inflation max-height per cluster (gives us "the brighter cyan = closer" for free). This is a ~30 LOC heuristic, no library.

---

## 5. WorldBox / NML Community Resources

- **NeoModLoader (NML)** — primary fork is now at https://github.com/WorldBoxOpenMods/ModLoader (older Tuxxego/NeoModLoader is the original; WorldBoxOpenMods is the active maintained line). MIT-style. Docs: https://worldboxopenmods.gitbook.io/mod-tutorial-en/.
- **Official Worldbox Wiki (Fandom)** — https://the-official-worldbox-wiki.fandom.com/wiki/Modding. Mostly gameplay, thin on modding internals.
- **worldbox.wiki** — https://worldbox.wiki/w/Modding. Slightly better modding section.
- **ButterBox** (open-source content mod, good Harmony-patch reference) — https://github.com/WorldBoxOpenMods/ButterBox. MIT.
- **Pholith/Worldbox-Mods** — https://github.com/Pholith/Worldbox-Mods. MIT. Good for examples of patching `ActorBase` and rendering.
- **WorldBoxOpenMods org** — https://github.com/orgs/WorldBoxOpenMods/repositories. Central hub.
- **GameBanana** — https://gamebanana.com/games/11196. Mostly distribution, some tooling.
- **Official Discord** — referenced from wiki; #modding-talk channel. No public archive.
- **ModernBox** — no public source found. Likely closed.

### Recommendation
Anchor on **WorldBoxOpenMods/ModLoader** as the canonical NML and **ButterBox** as the canonical "non-trivial mod that ships" reference. Update our existing `/reference/` notes if we currently point at Tuxxego.

---

## Ranked Integration Plan

1. **Implement 2D EDT + silhouette inflation in pure C#** (no dep) — Phase 2 core.
2. **Adopt Bronson Zgeb's volumetric heat-diffusion skinner** (port the blog repo, MIT) — Phase 3 rigging.
3. **Embed DragonBones C# core** (strip the Unity renderer, keep `Armature`/`Bone`/`AnimationState`) — Phase 3 animation playback. MIT, ~3 files of glue.
4. **Palette-luminance depth-bias heuristic** (~30 LOC) — Phase 2 polish.
5. Reference **mattatz/unity-voxel** EDT/voxelization internals as a sanity check, do not link.
6. Audit our existing NML pin → point at **WorldBoxOpenMods/ModLoader** if not already.

**Total third-party LOC pulled in:** ~3-4k (DragonBones core ~2.5k, heat-diffusion ~500, EDT we write ourselves). All MIT. No license contamination, no editor-only deps, no shader requirements, no neural-net weights.

## Sources

- https://github.com/Orama-Interactive/VoxeloramaExtension
- https://github.com/DiezRichard/pixtovox
- https://github.com/challacade/pixel3d
- https://github.com/seung-lab/euclidean-distance-transform-3d
- https://arxiv.org/pdf/2512.07834 (Voxify3D)
- https://github.com/DragonBones/DragonBonesCSharp
- https://esotericsoftware.com/spine-purchase
- https://esotericsoftware.com/spine-runtimes-license
- https://docs.unity3d.com/Packages/com.unity.2d.animation@3.0/manual/CharacterRig.html
- https://github.com/Banbury/UnitySpritesAndBones
- https://www.cs.toronto.edu/~jacobson/seminar/baran-and-popovic-2007.pdf (Pinocchio)
- https://www.gamedeveloper.com/programming/volumetric-heat-diffusion-skinning
- https://bronsonzgeb.com/index.php/2021/06/26/volumetric-heat-diffusion-for-automatic-mesh-skinning/
- https://bronsonzgeb.com/index.php/2021/05/15/simple-mesh-voxelization-in-unity/
- https://github.com/sketchpunklabs/autoskinning
- https://github.com/mattatz/unity-voxel
- https://arxiv.org/pdf/1502.05197 (SfS non-Lambertian)
- https://github.com/WorldBoxOpenMods/ModLoader
- https://worldboxopenmods.gitbook.io/mod-tutorial-en/
- https://github.com/WorldBoxOpenMods/ButterBox
- https://github.com/Pholith/Worldbox-Mods
- https://the-official-worldbox-wiki.fandom.com/wiki/Modding
- https://worldbox.wiki/w/Modding
