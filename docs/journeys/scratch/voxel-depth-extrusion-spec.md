# Sprite voxel depth extrusion spec

## Problem
`SpriteVoxelizer.Build()` currently turns each opaque texel into a stack of voxels that is only 1 voxel deep by default. From most camera angles that reads like a billboard with a thick edge, not a real 3D body.

## Choice
Use **symmetric extrusion** as the first fix.

Why this is the cheapest convincing option:

- It reuses the existing 3-axis greedy mesher.
- It needs only one new scalar setting, not per-pixel heuristics or extra art.
- It immediately produces visible side faces from oblique angles.
- It preserves sprite colors and the current render path.

Not chosen for the first pass:

- Color-based depth is more complex and unstable across palettes.
- Two-sided sprite composition needs more asset-specific logic.
- Bevel/rounding is extra work on top of extrusion.
- Mirror-only backfaces still need a real thickness band to read as 3D.

## Implementation

Add one setting:

```csharp
public int VoxelSpriteDepth = 3;
```

Then make `SpriteVoxelizer.Build()` treat `depth < 1` as "use the configured setting":

```csharp
public static Mesh Build(Sprite sprite, int depth = -1)
{
    depth = ResolveDepth(depth);
    ...
}

static int ResolveDepth(int depth)
{
    if (depth > 0) return depth;
    return Core.savedSettings != null && Core.savedSettings.VoxelSpriteDepth > 0
        ? Core.savedSettings.VoxelSpriteDepth
        : 3;
}
```

Pass the same unset/default path through `VoxelMeshCache.Get()` so cached builds and warm-cache builds both honor the setting.

## Shape Policy

- Default depth: `3`
- Keep the extrusion symmetric around local Z so the sprite pivot still lines up with the existing actor render transforms.
- Do not add per-region depth rules yet. That can be a later refinement if some sprites still look too blocky.

## Expected Result

This should stop the "flat slab" read at a glance. A 3-voxel extrusion gives each actor a visible front, back, and side wall while staying cheap enough to ship behind the existing voxel path.

## Minimal code touchpoints

- `WorldSphereMod/Code/SavedSettings.cs`
- `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs`
- `WorldSphereMod/Code/Voxel/VoxelMeshCache.cs`

