# Minecraft texture pack importer spec

## Problem
WSM3D needs a user-local way to consume Minecraft resource packs such as BDcraft 128x, Patrix 256x, or Faithful 32x without shipping any third-party art. The pack should live in `wsm3d/texturepacks/`, be detected at mod load, and replace WSM3D’s default voxel-facing textures when present.

Minecraft resource packs are zip/folder structures with `pack.mcmeta` at the root and textures under `assets/minecraft/textures/...`; this spec follows that layout. Source: https://minecraft.wiki/w/Resource_pack

## Choice
Use a **scan -> validate -> import -> atlas -> bind** pipeline.

This is the cheapest safe design because it:

- keeps packs local to the player machine;
- supports both zip packs and unpacked folders;
- lets WSM3D fall back to its current textures when a pack is missing or invalid;
- keeps the runtime render path simple by sampling one atlas/registry instead of opening zip files on demand.

## Import Flow

1. At mod load, scan `wsm3d/texturepacks/` for `.zip` files and unpacked directories.
2. For each candidate, verify `pack.mcmeta` exists and parse:
   - `pack_format`
   - `description`
3. Reject packs that do not match the supported Minecraft resource-pack schema or cannot be opened.
4. Enumerate `assets/minecraft/textures/block/*.png` and load only the textures WSM3D knows how to use.
5. Build a single atlas texture from the imported images.
6. Write a small local manifest with:
   - pack name
   - source path
   - source hash
   - atlas dimensions
   - name -> atlas rect mapping
7. Bind the atlas into WSM3D’s material/texture registry before the first voxel render pass.

## Mapping Policy

Use a data-driven registry from Minecraft block names to WSM3D classes. Start with a curated default set and leave the rest unmapped.

Examples:

- `grass_block_top` -> `biome_grass`
- `grass_block_side` -> `biome_grass_side`
- `dirt` -> `biome_dirt`
- `stone` -> `mountain_rock`
- `cobblestone` -> `building_cobble`
- `oak_planks` -> `building_plank`
- `log_oak` -> `building_wood`
- `water_still` / `water_flow` -> `water_surface`
- `sand` -> `desert_sand`
- `snow` -> `tundra_snow`

For WSM3D content that is not a direct biome surface, map only the textures that already have a stable semantic slot in the mod. The importer should not guess at arbitrary Minecraft blocks.

## Atlas Rules

- Pack all accepted textures into one atlas per active resource pack.
- Prefer power-of-two atlas sizes.
- Pad each sprite by at least 2 pixels to reduce bleeding.
- Use point sampling for voxel surfaces unless a later shader explicitly wants filtered sampling.
- Preserve alpha; many packs use transparency for leaves, water edges, and decals.
- If a texture is missing, keep the current WSM3D fallback for that slot.

## Binding Model

The importer should not rewrite meshes. It should provide texture bindings that the existing systems can consume:

- voxel terrain surfaces use the atlas-backed material slot;
- building surfaces pull named block textures from the registry;
- actor-related texture overrides are opt-in and only apply to classes that already have a clean semantic texture target.

If a class has no pack match, WSM3D keeps its current default art.

## Licensing

Do not redistribute pack textures, extracted PNGs, or generated atlas files with the mod. The importer may read local user packs and generate a local cache, but all derived assets stay on the user machine and are never published to the mod package.

## Failure Policy

- No `pack.mcmeta`: skip the candidate.
- Bad zip or parse error: log and continue.
- Missing textures: bind what exists, leave the rest on defaults.
- Unsupported `pack_format`: reject with a clear warning.
- Import failure during load: do not block world startup.

## Minimal Touchpoints

- `WorldSphereMod/Code/Import/TexturePackImporter.cs`
- `WorldSphereMod/Code/Import/TexturePackRegistry.cs`
- `WorldSphereMod/Code/Voxel/VoxelRender.cs`
- `WorldSphereMod/Code/Water/WaterSurface.cs`
- `WorldSphereMod/Code/ProcGen/BuildingProcRender.cs`
- `WorldSphereMod/Code/Core.cs` for load-time hookup

## Result

Players can drop a Minecraft texture pack into `wsm3d/texturepacks/`, load the mod, and have WSM3D automatically use the pack’s block art for the surfaces it understands, without shipping or redistributing any third-party textures.
