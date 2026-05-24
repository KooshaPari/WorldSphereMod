# Anatomical Template Spec

Goal: extend voxel actors from “sprite as depth stack” into rig-typed 3D silhouettes, while preserving the current extrusion path as a safe fallback.

This follows the Phase 6 rig split already scoped in `docs/phase6-architecture.md:150-161` and the asset registry direction in `docs/journeys/scratch/phase6-rig-variety-spec.md:33-37`.

## 1) Template model

Each `RigType` gets a canonical anatomical template: a sparse set of local voxel coordinates that describe the intended 3D body volume.

- `Humanoid`: torso cylinder, head sphere, two arm cylinders, two leg cylinders.
- `Quadruped`: longer body cylinder, head volume, four leg cylinders, optional tail stub.
- `Bird`: rounded body, neck, head, two folded wing volumes, thin legs.
- `Snake`: continuous tapered tube, minimal head bulge, no limbs.
- `Insect`: segmented thorax/abdomen, head capsule, six leg rays, optional wing pair.

Template coordinates are normalized to a local actor box, not sprite pixels. The same template can scale across species variants without changing topology. The template should expose:

- `RigType`
- ordered voxel coordinates
- a per-voxel region label, such as `Head`, `Core`, `Limb`, `Wing`, `Tail`
- a surface/occupancy tag for shell extraction if later needed

## 2) Sprite projection

For each occupied template voxel, sample one color from the source sprite and assign that color to the voxel.

Projection rule:

1. Build the template in local 3D space.
2. Project each template voxel to a 2D sprite coordinate using the rig’s dominant axis.
3. Sample the sprite at that coordinate with nearest or bilinear filtering, matching the current sprite texture pipeline.
4. Copy alpha and color into the voxel record.

The projection should prefer anatomical continuity over strict pixel identity. For example, a humanoid torso voxel may sample from a torso band even if the sprite has minor arm overlap, because the template is the silhouette driver. This keeps the 3D read stable across frames.

## 3) Template-specific projection modes

Use a simple projection basis per rig:

- `Humanoid`: front-facing projection on torso/head, limb projection along limb axis.
- `Quadruped`: side projection for body, separate front/back sampling for legs.
- `Bird`: side projection for body and wings, with wing voxels sampling from wing regions.
- `Snake`: side projection along the body spline.
- `Insect`: top/side hybrid, because the body is segmented and the leg count is higher.

The projection step can reuse the same sprite bounds and pixel sampling logic already used by extrusion; only the mapping from voxel coordinate to sprite coordinate changes.

## 4) Fallback policy

Keep existing per-pixel voxel extrusion as the fallback path.

Use fallback when any of these are true:

- the actor has `RigType.None`
- the rig type is unregistered
- the template is missing coverage for a sprite region
- the template build fails validation
- the sprite is too ambiguous for a confident anatomical fit

Fallback means: preserve the current `SpriteVoxelizer.Build()` result unchanged, including current depth behavior from `docs/journeys/scratch/voxel-depth-extrusion-spec.md:4-55`.

## 5) Composition rule

The final voxel actor should be the union of two sources:

- template voxels, colorized by sprite projection
- extruded voxels, used as a backstop for uncovered pixels or non-template rigs

When both sources cover the same local area, prefer the template voxel if the rig is recognized. This keeps the silhouette coherent and avoids double-thick features.

## 6) Validation gates

Template builds should reject degenerate outputs:

- too few occupied voxels
- disconnected body parts where the template expects a single mass
- impossible proportions for the declared rig type
- missing sprite samples for a large template region

If validation fails, log once and drop to extrusion-only rendering for that asset instance.

## 7) Implementation shape

This design fits the current Phase 6 flow without changing the skeletal path contract in `docs/phase6-architecture.md:53-64,196-198`.

Suggested runtime order:

1. Resolve `RigType` from the actor registry.
2. If a template exists, build the anatomical volume and project sprite colors onto it.
3. Merge in per-pixel extrusion only where the template is missing coverage.
4. Submit the resulting mesh through the existing voxel actor pipeline.

That gives the 3D silhouette upgrade for known rig families while keeping the current voxelizer as the compatibility floor.

## Implementation status (2026-05-23)

| Item | Status |
|------|--------|
| `AnatomicalRegion`, `AnatomicalOccupancy`, `AnatomicalVoxel`, `AnatomicalTemplate` | Done (scaffold) |
| `AnatomicalProjectionMode` + per-rig mode map in `AnatomicalTemplateRegistry` | Done (scaffold) |
| `AnatomicalTemplateValidation` (min voxels, 6-neighbor connectivity) | Done (scaffold) |
| `AnatomicalTemplatePipeline.ShouldUseTemplate` / `TryBuildColorizedTemplate` | Stub — always defers |
| Canonical voxel coordinate sets per `RigType` | Not started |
| Sprite projection (`§2`) onto template voxels | Not started |
| Union merge with `SpriteVoxelizer.Build()` backstop (`§5`) | Not started |
| `VoxelMeshCache` / `RigCache` wiring (`§7` runtime order) | Not started |
| Unit source invariants (`AnatomicalTemplateScaffoldTests`) | Done |

### Research notes

- **Integration point:** `VoxelMeshCache.BuildVoxelMesh` and `RigCache.GetOrBuild` already resolve `RigType` and call `SpriteVoxelizer.BuildPerTexel` for rigged actors. Template path should branch before mesh finalize, not inside `SpriteVoxelizer.Build` itself (matches phase6 note: segmentation stays cache-time).
- **Fallback today:** `AnatomicalTemplateRegistry.TryGetTemplate` returns false for all rigs, so `ShouldUseTemplate` is false and extrusion behavior is unchanged.
- **Humanoid vs quadruped projection:** Registry maps humanoid body to `FrontFacing`, quadruped/bird/snake to `Side`, insect to `TopSideHybrid`; limbs can override via `GetLimbProjectionMode` when projection is implemented.
- **Next authoring step:** Add sparse `AnatomicalVoxel[]` tables per rig (cylinder/sphere stubs in local box coordinates), then implement `ProjectSpriteColor` reusing `SpriteVoxelizer.GetPixelsCached` sampling.
- **Static / None:** `RigType.Static` and `RigType.None` are excluded from template lookup per `§4` fallback policy.
