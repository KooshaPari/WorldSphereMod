# Assets — `extend-via-api`

Captures for the [Extend via the API](../../extend-via-api.md) journey.
PNGs 1280 × 720, GIFs 800 × 450, each under 2 MB.

| Filename                 | Step | What it shows |
|--------------------------|------|---------------|
| `01-api-link.png`        | 1    | An external mod's `.csproj` open in editor, with the `<Reference Include="WorldSphereAPI">` block highlighted. The `$(WorldBoxPath)` hint path should be readable. |
| `02-connect-call.png`    | 2    | C# source open in editor showing the `WorldSphereAPI.Connect(out var api)` call site, with `api.IsModel3D` and `api.IsWorld3D` reads visible. Use IDE syntax highlighting; crop to ~30 lines. |
| `03-registered-mesh.gif` | 5    | In-game GIF: spawn the asset whose `assetId` was registered via `RegisterCustomMesh`. Camera orbits 360° so the hand-made mesh (not the voxelized sprite) is clearly the one being rendered. 4–6 seconds, ~12 fps. |
