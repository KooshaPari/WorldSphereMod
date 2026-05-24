Black voxel mesh diagnosis

- `SpriteVoxelizer.Build()` is doing the right thing. It reads the sprite texels from the atlas, stores each opaque texel in `color[x,y,z]`, and writes those colors into the mesh with `mesh.SetColors(cols)`. The per-texel path does the same. `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs:146-189`, `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs:235-299`
- `VoxelRender.ConfigureVoxelMaterial()` does not assign any sprite texture. It sets `_BaseColor`/`material.color` to white and only tweaks smoothness/metallic/cubemap for lit paths. There is no `_MainTex` or `_BaseMap` hookup here. `WorldSphereMod/Code/Voxel/VoxelRender.cs:125-163`
- `Standard` does not consume mesh vertex colors as albedo. So an opaque Standard material with white base color and no texture will ignore the sprite pixels that were baked into `mesh.colors32`, which matches the black voxel result. `WorldSphereMod/Code/Voxel/VoxelRender.cs:83-87`

Smallest fix

1. Stop resolving the voxel material to `Standard`.
2. Use a shader that explicitly reads vertex color (`COLOR`) and outputs it as albedo.
3. The smallest repo-local change is to update `Resources/Shaders/VoxelLit.shader` to accept mesh vertex color and multiply it into the final fragment color, then point `EnsureMaterial()` at that shader instead of `Standard`.

Note: `Resources/Shaders/VoxelLit.shader` currently only uses `_BaseColor` and `_InstanceColor`; it also ignores mesh vertex colors as written. `WorldSphereMod/Resources/Shaders/VoxelLit.shader:62-129`
