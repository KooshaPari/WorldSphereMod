# WSM3D Shader Bake Recovery — Combined Bundle

> The first bake (`b5f6c5a`) shipped 7 new shaders BUT replaced the bundle
> entirely, stripping `compoundspheremesh.asset` + `compoundspherematerial.mat`
> which Core.LoadAssets needs for SphereManager. Reverted at `29a3ae3`.
>
> This walkthrough re-bakes WITH the legacy assets, fixing both the
> Standard-shader-blackness cascade AND keeping the 3D world geometry.

## Prerequisites

- Unity 6.3 (Unity Hub → Editor → 6000.3.x)
- `WorldSphereMod/AssetBundles/win/worldsphere` reverted to pre-bake state
  (current HEAD has this)

## Step 1 — Extract legacy CompoundSphere assets from existing bundle

Create a new C# Editor script at
`Tools/Unity-Bake-Project/Assets/Editor/ExtractLegacyAssets.cs`:

```csharp
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ExtractLegacyAssets
{
    public static void Run()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
        string bundlePath = Path.Combine(repoRoot, "WorldSphereMod", "AssetBundles", "win", "worldsphere");
        string outDir = Path.Combine(Application.dataPath, "WSM3D", "LegacyAssets");
        Directory.CreateDirectory(outDir);

        AssetBundle ab = AssetBundle.LoadFromFile(bundlePath);
        if (ab == null) { Debug.LogError("[Extract] bundle load failed: " + bundlePath); return; }
        try
        {
            foreach (string name in ab.GetAllAssetNames())
            {
                Debug.Log($"[Extract] {name}");
                Object asset = ab.LoadAsset(name);
                if (asset is Mesh mesh)
                {
                    string fname = Path.Combine(outDir, Path.GetFileName(name).Replace(".asset", ".asset"));
                    AssetDatabase.CreateAsset(Object.Instantiate(mesh), "Assets/WSM3D/LegacyAssets/" + Path.GetFileName(name));
                }
                else if (asset is Material mat)
                {
                    string fname = Path.Combine(outDir, Path.GetFileName(name).Replace(".mat", ".mat"));
                    AssetDatabase.CreateAsset(new Material(mat), "Assets/WSM3D/LegacyAssets/" + Path.GetFileName(name));
                }
            }
            AssetDatabase.SaveAssets();
        }
        finally
        {
            ab.Unload(true);
        }
    }
}
```

Run it: `pwsh Tools/bake-shaders.ps1 -ExecuteMethod ExtractLegacyAssets.Run`
(modify `-executeMethod` in bake-shaders.ps1 → `ExtractLegacyAssets.Run`).

After: `Assets/WSM3D/LegacyAssets/CompoundSphereMesh.asset` +
`CompoundSphereMaterial.mat` exist in the project.

## Step 2 — Tag legacy assets into the worldsphere bundle

Add to `BakeShaders.cs` (or create `BakeAll.cs`):

```csharp
foreach (var path in Directory.GetFiles(Path.Combine(Application.dataPath, "WSM3D", "LegacyAssets"), "*"))
{
    string rel = "Assets/" + Path.GetRelativePath(Application.dataPath, path).Replace('\\','/');
    AssetImporter ai = AssetImporter.GetAtPath(rel);
    if (ai != null) ai.assetBundleName = "worldsphere";
}
```

This goes BEFORE the existing shader-tagging loop.

## Step 3 — Re-bake (now includes BOTH legacy + new shaders)

```powershell
pwsh Tools/bake-shaders.ps1
```

Verify bundle file sizes increased over the post-revert size (~12-19 KB)
to ~30-50 KB — they should contain BOTH the legacy assets AND the 7
new shaders.

## Step 4 — Commit + deploy

```powershell
git add WorldSphereMod/AssetBundles/win/worldsphere WorldSphereMod/AssetBundles/linux/worldsphere
git commit -m "feat(shaders): RE-BAKE with legacy CompoundSphereMesh+Material AND new WSM3D/* shaders"
git push
pwsh Tools/wsm3d.ps1 install
pwsh Tools/wsm3d.ps1 kill ; pwsh Tools/wsm3d.ps1 launch
```

In Player.log expect:
```
[WSM3D][MATERIAL] Shader probe: 'WSM3D/OpaqueVertexColor' FOUND
[WSM3D] Voxel material resolved via inline 'WSM3D/OpaqueVertexColor'.
[WSM3D] Water material resolved via 'WSM3D/GerstnerWater' (transparent blue)
```

## Why this works

The original bundle had ONLY `compoundspheremesh.asset` +
`compoundspherematerial.mat`. The first bake had ONLY the 7 .shader
files. Combining BOTH in the new bundle = runtime gets all assets it
expects + the new shaders. No more Standard fallback.

After this lands, all 5 visual regression cascades collapse:
- Black voxel actors → vertex colors visible via OpaqueVertexColor
- Water blackworld → GerstnerWater transparent blue
- HDR skybox missing → ProceduralSky resolves
- SSAO/LUT toggles silent → their shaders resolve
- Open-box 2.5D → opaque double-sided
