# voxel-preview

`voxel-preview` is a tiny Rust crate that renders **before / after** PNG previews for the
WorldSphereMod sprite voxelizer without running Unity or WorldBox.

## CLI

```bash
cargo run --release -- render <input.png> --out <output.png> [--iso] [--side 256]
cargo run --release -- render-all <input-dir> <output-dir>
```

- `render`: builds one voxelized render from a single PNG.
- `render-all`: renders all `*.png` files in a directory to:
  - `<stem>.before.png` (upscaled source at `side`)
  - `<stem>.after.png` (voxel render)

## Implementation notes

- Greedy meshing follows `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs`.
- Every opaque texel spawns depth-1 cubes, hidden faces are removed, and exposed faces are
  merged by axis/plane and same RGBA color.
- Renderer is pure software rasterization:
  - orthographic camera, isometric orientation (`--iso`)
  - per-triangle barycentric fill
  - per-pixel z-buffer
  - flat directional light `dot(normal, normalize(1,2,3))`

## Outputs

```bash
cd tools/voxel-preview
cargo build --release
cargo run --release -- render-all ../../WorldSphereMod/GameResources/WorldSphereMod docs/journeys/voxel-previews
```

