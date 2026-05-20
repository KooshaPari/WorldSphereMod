# Luminance-based depth complement

## Problem
Sprite voxelization currently relies on a 2D distance transform to decide how far opaque pixels should inflate into depth. That captures silhouette thickness, but it does not recover the visual cues already baked into WorldBox sprites: a 3/4 lit sprite usually has bright highlight planes and dark shadow planes that imply local relief.

If we ignore that lighting, flat surfaces that should read as cheeks, helmets, roofs, or torsos become uniformly extruded. The result is valid geometry, but weak shape reading.

## Choice
Use a hybrid depth model:

```text
voxel_depth(x, y, z) =
  base_distance_transform(x, y) * inflation
  - luminance_term(x, y) * shadow_recession
```

Interpretation:

- `base_distance_transform` remains the primary thickness source.
- `inflation` scales the existing extrusion budget.
- `luminance_term` is a normalized brightness-derived offset, sampled from the sprite pixel.
- `shadow_recession` subtracts depth where the art is darker, carving recessed regions into the volume.

This keeps the current silhouette logic intact while adding a cheap per-pixel shape cue.

## Luminance Mapping

- Compute luminance from the source sprite color after alpha filtering.
- Normalize luminance into `[0, 1]`.
- Convert luminance to a signed depth bias around a neutral midpoint:
  - dark pixels push depth inward
  - bright pixels preserve or slightly push depth outward
- Clamp the final depth so the luminance term cannot erase the silhouette thickness entirely.

Recommended starting point:

- `neutral_luminance = 0.5`
- `shadow_recession = 1.0` as the first tuning scalar
- per-sprite tuning should stay optional; global defaults are enough for phase 1

## Pipeline

1. Build the 2D distance transform as today.
2. Sample luminance from the sprite texel at the same `(x, y)`.
3. Apply the hybrid formula to produce depth for each opaque texel.
4. Feed the resulting depth field into the existing voxel fill and meshing path.

## Guardrails

- Do not use luminance as the only depth source; it is a complement, not a replacement.
- Ignore fully transparent pixels.
- Preserve the current alpha mask and silhouette boundaries.
- Prefer stable, deterministic sampling over any adaptive post-pass.

## Expected Result

Bright-facing areas should read as raised planes, while shadow-facing areas should recede. The sprite keeps its baked lighting language, but the voxel volume gains a stronger sense of sculpted form without requiring new art assets or a heavy reconstruction step.
