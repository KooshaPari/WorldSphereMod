using System.IO;
using UnityEditor;
using UnityEngine;

public static class BakeShaders
{
    public static void BakeAll()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        // ---------- Step 1: Extract legacy CompoundSphere assets from existing bundle ----------
        string legacyDir = Path.Combine(Application.dataPath, "WSM3D", "LegacyAssets");
        Directory.CreateDirectory(legacyDir);
        string existingBundle = Path.Combine(repoRoot, "WorldSphereMod", "AssetBundles", "win", "worldsphere");
        if (File.Exists(existingBundle))
        {
            try
            {
                AssetBundle ab = AssetBundle.LoadFromFile(existingBundle);
                if (ab != null)
                {
                    foreach (string name in ab.GetAllAssetNames())
                    {
                        Object asset = ab.LoadAsset(name);
                        if (asset == null) continue;
                        string assetFile = Path.GetFileName(name);
                        string outRel = "Assets/WSM3D/LegacyAssets/" + assetFile;
                        if (asset is Mesh srcMesh)
                        {
                            Mesh m = Object.Instantiate(srcMesh);
                            m.name = srcMesh.name;
                            if (!File.Exists(Path.Combine(Application.dataPath, "..", outRel)))
                            {
                                AssetDatabase.CreateAsset(m, outRel);
                                Debug.Log($"[WSM3D-Bake] extracted Mesh -> {outRel}");
                            }
                        }
                        else if (asset is Material srcMat)
                        {
                            Material m = new Material(srcMat) { name = srcMat.name };
                            if (!File.Exists(Path.Combine(Application.dataPath, "..", outRel)))
                            {
                                AssetDatabase.CreateAsset(m, outRel);
                                Debug.Log($"[WSM3D-Bake] extracted Material -> {outRel}");
                            }
                        }
                    }
                    ab.Unload(false);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[WSM3D-Bake] legacy extract failed (continuing without): {ex.Message}");
            }
        }

        // ---------- Step 2: Copy shader sources into Assets/WSM3D/Shaders/ ----------
        string shaderSrc = Path.Combine(repoRoot, "WorldSphereMod", "AssetBundles", "Shaders");
        string assetsShaderDir = Path.Combine(Application.dataPath, "WSM3D", "Shaders");
        Directory.CreateDirectory(assetsShaderDir);
        foreach (var src in Directory.GetFiles(shaderSrc, "*.shader"))
        {
            string dst = Path.Combine(assetsShaderDir, Path.GetFileName(src));
            File.Copy(src, dst, overwrite: true);
        }
        AssetDatabase.Refresh();

        // ---------- Step 3: Tag everything in LegacyAssets/ + Shaders/ to 'worldsphere' bundle ----------
        foreach (var path in Directory.GetFiles(assetsShaderDir, "*.shader"))
        {
            string rel = "Assets/" + Path.GetRelativePath(Application.dataPath, path).Replace('\\', '/');
            AssetImporter ai = AssetImporter.GetAtPath(rel);
            if (ai != null) ai.assetBundleName = "worldsphere";
        }
        foreach (var path in Directory.GetFiles(legacyDir, "*"))
        {
            if (path.EndsWith(".meta")) continue;
            string rel = "Assets/" + Path.GetRelativePath(Application.dataPath, path).Replace('\\', '/');
            AssetImporter ai = AssetImporter.GetAtPath(rel);
            if (ai != null) ai.assetBundleName = "worldsphere";
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ---------- Step 4: Build for win/linux/osx ----------
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
            BuildPipeline.BuildAssetBundles(platformDir, BuildAssetBundleOptions.None, target);
            Debug.Log($"[WSM3D-Bake] built bundles for {target} -> {platformDir}");
        }
        Debug.Log("[WSM3D-Bake] All platforms done (combined legacy + new shaders).");
    }

    [MenuItem("WSM3D/Bake worldsphere AssetBundles")]
    public static void MenuBake() => BakeAll();
}
