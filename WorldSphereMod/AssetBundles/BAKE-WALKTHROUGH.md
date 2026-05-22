# WSM3D Shader AssetBundle Bake — Unity 6.3 Walkthrough

> One-time ~30-minute Editor session. Unblocks the entire "voxels render
> black / 2.5D / water blackworld / no HDR sky / toggles inert" cascade
> by giving runtime `Shader.Find("WSM3D/*")` valid resolution.

## Prerequisites

- ✅ Unity 6.3 installed
- ✅ Source `.shader` files in `WorldSphereMod/AssetBundles/Shaders/` (7 files)

## Steps

### 1. Create a baking project (one-time setup)

In Unity Hub:
1. New Project → 3D (Built-in Render Pipeline) → Unity 6.3
2. Name: `WSM3D-Bake` — location anywhere outside the mod repo
3. Open the project

### 2. Import shader sources

In Unity Editor:
1. Create folder `Assets/WSM3D/Shaders/`
2. Drag-drop ALL 7 `.shader` files from
   `WorldSphereMod/AssetBundles/Shaders/` into that folder:
   - `OpaqueVertexColor.shader`
   - `OpaqueVertexColorURP.shader` (skip if no URP package — see below)
   - `GerstnerWater.shader`
   - `ScreenSpaceAO.shader`
   - `ColorGradingLUT.shader`
   - `ProceduralSky.shader`
   - `Impostor.shader`
3. Wait for shader compile to finish (status bar bottom-right).
4. If `OpaqueVertexColorURP.shader` shows red errors — that's expected if
   you don't have the URP package installed; either:
   - **Skip it**: right-click → Delete. We'll bake BRP only.
   - **Or**: Window → Package Manager → Universal RP → Install.

### 3. Tag for AssetBundle

For each `.shader` file:
1. Click the file in Project window.
2. At bottom of Inspector → AssetBundle dropdown → `New...`
3. Type: `worldsphere` (lowercase). Press Enter.
4. Repeat for all shaders → they should all show `worldsphere` as bundle.

### 4. Drop in the BakeShaders.cs Editor script

Create `Assets/Editor/BakeShaders.cs`:

```csharp
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BakeShaders
{
    [MenuItem("WSM3D/Bake worldsphere AssetBundles")]
    public static void BakeAll()
    {
        string outDir = Path.Combine(Application.dataPath, "..", "Bundles");
        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

        var targets = new (BuildTarget t, string folder)[]
        {
            (BuildTarget.StandaloneWindows64, "win"),
            (BuildTarget.StandaloneLinux64, "linux"),
            (BuildTarget.StandaloneOSX, "osx"),
        };

        foreach (var (target, folder) in targets)
        {
            string platformDir = Path.Combine(outDir, folder);
            if (!Directory.Exists(platformDir)) Directory.CreateDirectory(platformDir);
            BuildPipeline.BuildAssetBundles(platformDir, BuildAssetBundleOptions.None, target);
            Debug.Log($"[WSM3D] Built bundles for {target} -> {platformDir}");
        }
        AssetDatabase.Refresh();
    }
}
```

### 5. Bake

In Unity Editor menu bar: **WSM3D → Bake worldsphere AssetBundles**.

Wait ~30 seconds. Output appears in `WSM3D-Bake/Bundles/{win,linux,osx}/`.

### 6. Copy outputs back to the mod repo

Copy `worldsphere` files (no extension, ~few KB each) from each platform
folder into the corresponding mod directory:

| From | To |
|---|---|
| `WSM3D-Bake/Bundles/win/worldsphere` | `WorldSphereMod/AssetBundles/win/worldsphere` |
| `WSM3D-Bake/Bundles/linux/worldsphere` | `WorldSphereMod/AssetBundles/linux/worldsphere` |
| `WSM3D-Bake/Bundles/osx/worldsphere` | `WorldSphereMod/AssetBundles/osx/worldsphere` |

(Overwrite existing.)

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
git add WorldSphereMod/AssetBundles/win/worldsphere
git add WorldSphereMod/AssetBundles/linux/worldsphere
git add WorldSphereMod/AssetBundles/osx/worldsphere
git commit -m "feat(shaders): bake 7 WSM3D/* shaders into worldsphere AssetBundle (Unity 6.3)"
git push
```

## Troubleshooting

| Symptom | Fix |
|---|---|
| "Shader compile failed for HLSL" | Unity 6.3 requires URP package for `Packages/com.unity.render-pipelines.universal/...` include. Either install URP or skip the URP variant shader. |
| "BuildAssetBundles returned null" | Check target platform module is installed in Unity Hub (Add Modules → e.g. Linux Build Support) |
| "Bundle file empty" | Verify each .shader has the `worldsphere` AssetBundle tag set in Inspector |
| "[WSM3D] Voxel material STILL resolved via Standard" | Bundle didn't load. Check Player.log for AssetBundle.LoadFromFile errors. Path expected: `<mod>/AssetBundles/{win,linux,osx}/worldsphere` |

## After this lands

The visual regressions documented across the session collapse together:
- Black voxel actors → vertex colors visible
- 2.5D open-box → opaque double-sided
- Water blackworld → GerstnerWater renders blue
- No HDR sky → ProceduralSky.shader resolves
- SSAO/LUT toggles silent → their shaders resolve too
