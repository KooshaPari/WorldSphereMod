# Sprite-to-Voxel Survey

Closest practical matches I found for WSM3D. Exact sprite-to-voxel repos are sparse, so I ranked by usefulness for a Unity mod pipeline.

- VoxelImporter (Unity Asset Store): https://assetstore.unity.com/packages/tools/modeling/voxel-importer-62914/reviews
  - Price: $33.
  - Integration cost: medium. Best if you want a packaged editor/import workflow instead of building your own parser.

- MagicaVoxel `.vox` format: https://github.com/ephtracy/voxel-model/blob/master/MagicaVoxel-file-format-vox.txt
  - Unity importers on GitHub: VoxToVFX (MIT, https://github.com/Zarbuz/VoxToVFX) and VoxReader (C# `.vox` reader, https://github.com/sandrofigo/VoxReader).
  - Integration cost: low for parsing, medium if you want runtime rendering or VFX Graph hookup.

- Top GitHub matches
  1. FileToVox: https://github.com/Zarbuz/FileToVox
     - License: MIT.
     - What it does: converts PNG folders and other inputs into MagicaVoxel `.vox`.
     - Integration cost: low-medium. Good as an offline prebuild step or CLI tool.
  2. VoxeloramaExtension: https://github.com/Orama-Interactive/VoxeloramaExtension
     - License: MIT.
     - What it does: turns 2D pixel art into voxel art.
     - Integration cost: medium. Strong fit conceptually, but it is Pixelorama/Godot-based, so Unity adoption is indirect.
  3. blender-image-voxel: https://github.com/knowuh/blender-image-voxel
     - License: MIT.
     - What it does: Blender add-on that converts an image into a single voxel mesh object.
     - Integration cost: medium. Easy for offline asset prep, not a Unity-native runtime dependency.
