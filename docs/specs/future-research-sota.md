# Future Research: SOTA Rendering Techniques

Scope: evaluate five current rendering techniques against a Unity 2022 Built-in Render Pipeline (BRP) mod in the WorldSphereMod codebase.

Decision rule: treat each item as a research candidate, not a commitment. The default bar for adoption in this repo is "can be implemented without swapping the entire rendering stack or introducing an engine fork."

## 1. Nanite virtualized geometry

### What it is
Nanite-style rendering replaces traditional author-authored LOD chains with a virtualized, cluster-based geometry pipeline that streams only visible detail and performs aggressive culling and rasterization work on the GPU.

### Why it is SOTA
Recent industry talks continue to treat Nanite and adjacent "mega geometry" pipelines as the reference point for dense scene rendering on current hardware. That makes it the right baseline for any future large-world geometry discussion.

### Applicability to Unity 2022 BRP mod
Low, near-term.

Unity 2022 BRP does not expose an engine-level Nanite equivalent. A mod can approximate some of the benefits with:
- mesh chunking and hierarchical LOD
- GPU-driven visibility and draw submission
- streamed proxy meshes and impostors

It cannot realistically replicate the full Nanite pipeline without replacing the renderer or introducing deep engine-side changes that are outside a normal mod boundary.

### Complexity
Very high.

The hard parts are not just mesh compression and streaming. The real cost is building or emulating:
- cluster hierarchy generation
- continuous visibility culling
- GPU submission compaction
- fallback material handling
- streaming, memory residency, and crack-free transitions

### Expected perf gain
High in the right workload, but only if the scene is geometry-heavy and CPU submission or overdraw is currently the bottleneck.

For this mod, the likely upside is indirect:
- fewer draw calls
- less CPU spent on submission
- better scalability for dense props, cliffs, foliage, and city detail

The gain is much smaller if the current content is already coarse or if BRP state changes dominate.

### Blockers
- No native Unity 2022 BRP Nanite equivalent
- Requires major asset-pipeline changes
- Requires GPU-driven rendering infrastructure that the mod does not currently have
- Risky memory and streaming complexity on a modding surface

### Research verdict
Do not target full Nanite parity. Research the underlying ideas for selective use: hierarchical meshes, runtime LOD clustering, and GPU-resident visibility buffers.

### Sources
- GDC 2026 session search result for Nanite / virtualized geometry on the GDC schedule: https://schedule.gdconf.com/
- Unreal Engine Nanite documentation: https://dev.epicgames.com/documentation/en-us/unreal-engine/nanite-virtualized-geometry-in-unreal-engine

## 2. Mesh shaders

### What it is
Mesh shaders move part of the traditional vertex/geometry pipeline into programmable GPU workgroups that can generate, cull, and emit primitives more flexibly than a classic vertex shader path.

### Why it is SOTA
Mesh shading is still a live topic in high-end rendering research because it enables GPU-native amplification, culling, and better fit for clustered or procedural geometry. It is often paired with virtualized geometry and GPU-driven rendering systems.

### Applicability to Unity 2022 BRP mod
Low to medium.

Unity 2022 BRP is not a mesh-shader-first pipeline. A mod can only use mesh shaders if the active graphics backend, platform, and Unity runtime expose the necessary low-level access. In practice, that makes this a platform-specific optimization path, not a general feature.

### Complexity
High.

The main cost is not the shader itself. It is:
- backend compatibility
- platform gating
- fallback path maintenance
- pipeline integration with BRP materials and transparency
- content conversion to benefit from amplification/culling

### Expected perf gain
Medium to high for geometry-heavy scenes with many small objects, especially when paired with GPU culling and cluster-based assets.

For a WorldBox mod, the likely win would be:
- less CPU draw submission
- better handling of dense city/terrain detail
- more scalable visibility processing

### Blockers
- No clean BRP abstraction for mesh-shader pipelines
- Platform support is fragmented
- Needs a low-level Unity graphics path that may not be available or stable in the target build
- Benefits depend on a broader GPU-driven renderer

### Research verdict
Worth tracking, but only as an advanced backend option. Treat it as a follow-on to GPU-driven culling, not as an isolated feature.

### Sources
- SIGGRAPH 2024 program/search results for mesh-shader-related rendering talks: https://www.siggraph.org/siggraph2024/program/
- Microsoft DirectX mesh shader overview: https://learn.microsoft.com/en-us/windows/win32/direct3d12/mesh-shaders

## 3. GPU-driven culling with HiZ

### What it is
HiZ culling uses a hierarchical depth pyramid to reject objects or clusters that are fully occluded before they reach expensive shading or rasterization work. In a GPU-driven version, culling, compaction, and draw generation happen on the GPU rather than the CPU.

### Why it is SOTA
This is one of the most practical "modern rendering" techniques because it bridges research and shipping code. It is also a common building block for large-scene engines, virtualized geometry systems, and clustered renderers.

### Applicability to Unity 2022 BRP mod
High.

This is the most immediately actionable technique on the list for a Unity 2022 BRP mod.

Reason:
- it can be implemented as a renderer-side optimization without replacing BRP
- it fits compute shaders and command buffer workflows
- it can be layered onto existing mesh/render data
- it can provide value even before any mesh-shader or Nanite-like work

### Complexity
Medium.

Compared with Nanite or mesh shaders, HiZ culling is simpler because it can be built incrementally:
- build depth pyramid
- test bounds against the pyramid
- compact visible instances
- issue indirect draws

The tricky parts are temporal stability, bounds quality, and handling transparent or alpha-tested objects.

### Expected perf gain
High when scene density and occlusion are significant.

The best-case gains are:
- fewer submitted draws
- reduced overdraw
- lower CPU overhead from skipped render work
- improved scaling for large city/terrain views

### Blockers
- Needs reliable depth prepass or equivalent depth buffer access
- Transparent, masked, and billboard content is harder to cull correctly
- Per-frame GPU readback must be avoided
- BRP integration must preserve compatibility with existing render ordering

### Research verdict
This is the highest-priority technique on the list for implementation research in this repo.

### Sources
- GDC 2026 schedule search results for GPU-driven culling / HiZ topics: https://schedule.gdconf.com/
- Unity compute shader documentation: https://docs.unity3d.com/Manual/ComputeShaders.html

## 4. Lumen-style SSGI cone tracing

### What it is
Lumen-style indirect lighting typically combines surface caches, screen-space traces, and software/hardware ray tracing fallbacks to approximate dynamic global illumination. In a Unity BRP context, the closest practical subset is SSGI with cone tracing against depth and normal buffers, plus limited temporal accumulation.

### Why it is SOTA
The important idea is not "full path tracing." It is the hybrid approach:
- screen-space first
- cache/trace next
- stable temporal reconstruction last

That is the current reference model for real-time indirect lighting in demanding scenes.

### Applicability to Unity 2022 BRP mod
Medium, but with strong quality limits.

A BRP mod can implement a lighter-weight version:
- screen-space indirect diffuse approximation
- cone-like ray marching in depth/normals
- temporal reprojection
- optional fallback probes or baked lighting

It cannot match Lumen quality without engine-level support for surface caches, high-quality history buffers, and robust fallback tracing.

### Complexity
High.

The challenge is mostly temporal stability and artifact management:
- history rejection
- disocclusion handling
- thin-geometry leaks
- noise / blur balance
- interaction with post-processing and tonemapping

### Expected perf gain
Moderate visual value, moderate-to-high GPU cost.

This is usually a quality feature, not a raw performance win. The benefit is better dynamic indirect lighting without fully baked content. The cost can be significant, so the tradeoff must be justified by visible improvement.

### Blockers
- BRP does not provide Lumen-style scene caches
- Screen-space methods miss off-screen bounce
- Good results require careful temporal history management
- Likely expensive on lower-end hardware

### Research verdict
Worth investigating as a constrained quality pass, not as a core rendering dependency.

### Sources
- Unreal Engine Lumen documentation: https://dev.epicgames.com/documentation/en-us/unreal-engine/lumen-global-illumination-and-reflections-in-unreal-engine
- GDC 2026 schedule search results for real-time global illumination / indirect lighting talks: https://schedule.gdconf.com/

## 5. Variable-rate shading for foveated rendering

### What it is
Variable-rate shading reduces shading frequency in less important regions of the screen. In a foveated setup, the center of attention keeps high shading rate while the periphery uses cheaper shading.

### Why it is SOTA
This is a practical optimization for VR and eye-tracked rendering, and it is one of the few advanced shading features that can produce meaningful perf wins without changing scene content.

### Applicability to Unity 2022 BRP mod
Low to medium.

The technique is only attractive if the target runtime, GPU, and Unity graphics backend expose VRS support. For a normal BRP mod, adoption is mostly limited to:
- platform-specific Windows/DX12 paths
- experimental render-feature integration
- optional use only on supported hardware

### Complexity
Medium to high.

The feature itself is straightforward in concept, but the deployment matrix is not:
- platform support varies
- quality tuning is scene-dependent
- eye-tracking integration is non-trivial
- fallback behavior must be preserved

### Expected perf gain
Medium to high for VR or headset-style foveated scenarios.

For a desktop third-person game, the gain is usually smaller and harder to justify unless the scene is already fill-rate bound.

### Blockers
- Requires hardware and API support
- Unity BRP does not make foveated/VRS authoring simple
- Best results depend on eye tracking or a stable gaze proxy
- Visual quality can drop quickly if rate transitions are too visible

### Research verdict
Track it for high-end or VR-adjacent use only. It is not a default fit for a standard desktop mod.

### Sources
- GDC 2026 schedule search results for foveated rendering / VRS-related talks: https://schedule.gdconf.com/
- Microsoft DirectX variable-rate shading documentation: https://learn.microsoft.com/en-us/windows/win32/direct3d12/variable-rate-shading

## Bottom line

Priority order for this mod:
1. GPU-driven culling with HiZ
2. Lumen-style SSGI cone tracing as an optional quality pass
3. Mesh shaders as a backend research track
4. Variable-rate shading for foveated rendering as a platform-specific optimization
5. Nanite-style virtualized geometry as a long-horizon architectural reference, not a near-term implementation target

## Recommendation for next step

Start with a GPU-driven visibility prototype that can:
- build a depth pyramid
- cull clustered instances on the GPU
- submit indirect draws
- preserve a CPU fallback path

That work creates the infrastructure needed for most of the other ideas on this list.
