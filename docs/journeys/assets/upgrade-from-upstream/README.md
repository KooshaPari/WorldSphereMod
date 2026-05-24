# Assets — `upgrade-from-upstream`

Captures for the [Upgrade from upstream](../../upgrade-from-upstream.md)
journey. All PNGs 1280 × 720, each under 2 MB.

| Filename               | Step | What it shows |
|------------------------|------|---------------|
| `01-coexist.png`       | Player → 2 | NeoModLoader's mod manager listing **both** upstream `WorldSphereMod` and `WorldSphereMod3D`, side-by-side. Upstream toggled OFF (grey), fork toggled ON (green). Demonstrates co-installability via the distinct `worldsphere3d.fork` GUID. |
| `02-migration-log.png` | Player → 4 | Player.log tail showing the settings migration line from v1.5 → v2.0 (the new fork-only flags being defaulted in). Crop to just the WSM-tagged log lines. |
| `03-before-after.png`  | Player → 5 | **Before/after panel**: a side-by-side composite — left half is upstream `WorldSphereMod` (sprite-billboard actors, 2D nameplates), right half is `WorldSphereMod3D` with `VoxelActors`/`MeshBuildings`/`Worldspace UI` on. Same world seed for both halves; 1-px divider down the middle; `BEFORE` / `AFTER` text overlay top-left of each half. |
