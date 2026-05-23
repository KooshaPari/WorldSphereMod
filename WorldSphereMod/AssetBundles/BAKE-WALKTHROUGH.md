# WSM3D Shader AssetBundle Bake - Unity 6.3 Walkthrough

> One-time ~30-minute Editor session. Unblocks the entire "voxels render
> black / 2.5D / water blackworld / no HDR sky / toggles inert" cascade
> by giving runtime `Shader.Find("WSM3D/*")` valid resolution.

## Prerequisites

- ✅ Unity 6.3 installed
- ✅ Source `.shader` files in `WorldSphereMod/AssetBundles/Shaders/` (six shipped BRP files; `OpaqueVertexColorURP.shader` is intentionally excluded)

## Steps

### 1. Create a baking project (one-time setup)

In Unity Hub:
1. New Project → 3D (Built-in Render Pipeline) → Unity 6.3
2. Name: `WSM3D-Bake` — location anywhere outside the mod repo
3. Open the project

### 2. Import shader sources

In Unity Editor:
1. Create folder `Assets/WSM3D/Shaders/`
2. Drag-drop the shipped `.shader` files from
   `WorldSphereMod/AssetBundles/Shaders/` into that folder:
   - `OpaqueVertexColor.shader`
   - `GerstnerWater.shader`
   - `ScreenSpaceAO.shader`
   - `ColorGradingLUT.shader`
   - `ProceduralSky.shader`
   - `Impostor.shader`
3. Wait for shader compile to finish (status bar bottom-right).
4. If Unity imports a leftover `OpaqueVertexColorURP.shader`, delete it. Keep it out of the bake project: the editor script skips URP variants on purpose, and the checked-in `wsm3d-shaders` bundle is the BRP-only six-shader set.

### 3. Tag for AssetBundle

For each `.shader` file:
1. Click the file in Project window.
2. At bottom of Inspector → AssetBundle dropdown → `New...`
3. Type: `wsm3d-shaders` (lowercase). Press Enter.
4. Repeat for all shaders → they should all show `wsm3d-shaders` as bundle.

### 4. Drop in the BakeShaders.cs Editor script

Create `Assets/Editor/BakeShaders.cs`:

```csharp
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BakeShaders
{
    [MenuItem("WSM3D/Bake wsm3d-shaders AssetBundles")]
    public static void BakeAll()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
        string shaderSrc = Path.Combine(repoRoot, "WorldSphereMod", "AssetBundles", "Shaders");
        string assetsShaderDir = Path.Combine(Application.dataPath, "WSM3D", "Shaders");
        Directory.CreateDirectory(assetsShaderDir);

        foreach (var src in Directory.GetFiles(shaderSrc, "*.shader"))
        {
            string fn = Path.GetFileName(src);
            if (fn.IndexOf("URP", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }
            File.Copy(src, Path.Combine(assetsShaderDir, fn), overwrite: true);
        }

        AssetDatabase.Refresh();
        foreach (var path in Directory.GetFiles(assetsShaderDir, "*.shader"))
        {
            string rel = "Assets/" + Path.GetRelativePath(Application.dataPath, path).Replace('\\', '/');
            AssetImporter ai = AssetImporter.GetAtPath(rel);
            if (ai != null)
            {
                ai.assetBundleName = "wsm3d-shaders";
                ai.SaveAndReimport();
            }
        }

        string outBase = Path.Combine(repoRoot, "WorldSphereMod", "AssetBundles");
        var targets = new (BuildTarget t, string folder)[]
        {
            (BuildTarget.StandaloneWindows64, "win"),
            (BuildTarget.StandaloneLinux64, "linux"),
            (BuildTarget.StandaloneOSX, "osx"),
        };

        foreach (var (target, folder) in targets)
        {
            string platformDir = Path.Combine(outBase, folder);
            Directory.CreateDirectory(platformDir);
            BuildPipeline.BuildAssetBundles(platformDir, BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.StrictMode, target);
        }
    }
}
```

### 5. Bake

In Unity Editor menu bar: **WSM3D → Bake wsm3d-shaders AssetBundles**.

Wait ~30 seconds. Output appears in `WorldSphereMod/AssetBundles/{win,linux,osx}/`.

### 6. Confirm outputs

The baked bundle files land in `WorldSphereMod/AssetBundles/{win,linux,osx}/wsm3d-shaders`.
Overwrite the checked-in copies there if you rebuilt them locally.

### 7. Verify in-game

```powershell
pwsh Tools/wsm3d.ps1 install
pwsh Tools/wsm3d.ps1 kill
pwsh Tools/wsm3d.ps1 launch
```

In Player.log, look for:
```
[WSM3D][MATERIAL] Shader probe: 'WSM3D/OpaqueVertexColor' FOUND
[WSM3D] Voxel material resolved via inline 'WSM3D/OpaqueVertexColor'.
```

If you see those, the bake worked. Voxels should now render with sprite
colors (not black) and be opaque (not 2.5D).

### 8. Commit binary blobs

```powershell
cd C:/Users/koosh/Dev/WorldSphereMod
git add WorldSphereMod/AssetBundles/win/wsm3d-shaders
git add WorldSphereMod/AssetBundles/linux/wsm3d-shaders
git add WorldSphereMod/AssetBundles/osx/wsm3d-shaders
git commit -m "feat(shaders): bake 6 WSM3D/* shaders into wsm3d-shaders AssetBundle (Unity 6.3)"
git push
```

## Troubleshooting

| Symptom | Fix |
|---|---|
| "Shader compile failed for HLSL" | Check whether a leftover `OpaqueVertexColorURP.shader` was imported. Delete it from `Assets/WSM3D/Shaders/` and re-run the bake. The checked-in bundle is BRP-only, so the URP file must not be part of the bake project. |
| "BuildAssetBundles returned null" | Check target platform module is installed in Unity Hub (Add Modules → e.g. Linux Build Support) |
| "Bundle file empty" | Verify each .shader has the `wsm3d-shaders` AssetBundle tag set in Inspector |
| "[WSM3D] Voxel material STILL resolved via Standard" | Bundle didn't load. Check Player.log for AssetBundle.LoadFromFile errors. Path expected: `<mod>/AssetBundles/{win,linux,osx}/worldsphere` for the main bundle and `<mod>/AssetBundles/{win,linux,osx}/wsm3d-shaders` for shaders |

## After this lands

The visual regressions documented across the session collapse together:
- Black voxel actors → vertex colors visible
- 2.5D open-box → opaque double-sided
- Water blackworld → GerstnerWater renders blue
- No HDR sky → ProceduralSky.shader resolves
- SSAO/LUT toggles silent → their shaders resolve too
