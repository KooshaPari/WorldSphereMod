# Mesh smoother replacement survey

Current code path: `MeshSmoother.Smooth` is a runtime, main-thread Laplacian pass that copies the mesh, builds adjacency/boundary sets, then recalculates normals/bounds. It is gated by `SavedSettings.VoxelMeshSmoothing` and called from `VoxelMeshCache.Get()`, so the real cost matters at cache-build time, not just editor time.

## Findings

- `Unity ProBuilder` is the easiest dependency to add, but its smoothing is about smoothing groups / soft edges, not a voxel-oriented Laplacian pass. The current package manifest also targets `Unity 6000.0`, so `2022.3` is not a safe compatibility claim without pinning an older release or forking. Integration cost: low for editor tooling, high for runtime voxel meshes. Perf: fine offline, not a per-frame solution for 1000+ meshes. Sources: [ProBuilder package.json](https://raw.githubusercontent.com/Unity-Technologies/com.unity.probuilder/master/package.json), [ProBuilder repo](https://github.com/Unity-Technologies/com.unity.probuilder).

- `MeshDeformer or similar` is closer to the right shape only if the “similar” library already exposes Laplacian smoothing. The best fit I found is `geometry3Sharp`, which is pure C# and includes Laplacian smoothing / remeshing, so `2022.3` is plausibly fine. Integration cost: medium, because it still needs mesh conversion and API adaptation. Perf: acceptable for occasional cache generation, but too allocation-heavy / CPU-heavy for smoothing 1000+ voxel meshes every frame. Sources: [geometry3Sharp](https://github.com/gradientspace/geometry3Sharp).

- `OpenMesh / libmesh ports` are the wrong integration class for this project. The upstream libraries are native C++ ecosystems, so a Unity use would mean a native plugin plus marshaling, not a drop-in C# package. Integration cost: high. `2022.3` compat: only through custom plugin glue. Perf: potentially good, but the maintenance cost is disproportionate for Phase 1 smoothing. Sources: [OpenMesh](https://www.graphics.rwth-aachen.de/software/openmesh/), [libMesh](https://libmesh.github.io/).

- `Burst-compiled subdivision libs` are the best runtime direction, but I did not find a mature, drop-in subdivision/smoothing package for Unity `2022.3`. The closest Unity-native path is Burst + `MeshData`-style processing, which is fast enough to justify a custom job if smoothing must stay runtime. Integration cost: medium if we own the job, low dependency risk. Perf: best chance of staying inside a 1000+ mesh budget. Sources: [MeshApiExamples](https://github.com/Unity-Technologies/MeshApiExamples), [Meshia.MeshSimplification](https://github.com/RamType0/Meshia.MeshSimplification).

## Recommendation

Do not replace Phase 1 smoothing with ProBuilder or a native C++ port. If a replacement is required, the best path is a small custom Burst job over Unity `MeshData`, preserving the current Laplacian behavior. If you want a library now, `geometry3Sharp` is the only plausible C# candidate, but I would treat it as a cache-time helper, not a per-frame runtime dependency.
