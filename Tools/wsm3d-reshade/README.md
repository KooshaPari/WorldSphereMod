# WSM3D ReShade preset

This folder contains a starter ReShade preset that enables:
- SSAO
- ColorGrading
- Bloom
- AmbientLight
- MartyMcFly RTGI

Files
- `wsm3d-preset.ini` — import into ReShade and enable the shader file set below.

## Install for `worldbox.exe` (DirectX 11)

1. Copy `wsm3d-preset.ini` into your ReShade preset folder:
   - `ReShade\Presets\`
2. Ensure ReShade has the required shader files available in `ReShade\Shaders\`:
   - `SSAO.fx`
   - `ColorGrading.fx`
   - `Bloom.fx`
   - `AmbientLight.fx`
   - `MartyMcFly_RTGI.fx`
3. Run/Reinstall ReShade on `worldbox.exe` using the DX11 profile:
   - Launch `worldbox.exe` from your WorldBox install directory with API set to **Direct3D 11**.
   - If needed, run ReShade installer and select `worldbox.exe`, then choose the **DX11** API.
4. In ReShade, load `wsm3d-preset.ini`:
   - Open ReShade menu in-game.
   - Load preset from `Presets`.
5. Enable all listed techniques if not already active:
   - AmbientLight
   - SSAO
   - Bloom
   - ColorGrading
   - RTGI (from MartyMcFly_RTGI.fx)
6. Save preset after tuning parameters to taste.

## Recommended launch path

Install `worldbox.exe` in DirectX 11 mode for ReShade compatibility:
- `steamapps/common/WorldBox/worldbox.exe` (or your local install equivalent)

Then run the game normally; your preset should be available and selectable.