using System.IO;
using UnityEditor;
using UnityEngine;

public static class BakeShaders
{
    public static void BakeAll()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        // Copy shader sources into the Unity bake project so we can build a
        // dedicated shader bundle without touching the legacy worldsphere
        // bundle or its assets.
        string assetsShaderDir = Path.Combine(Application.dataPath, "WSM3D", "Shaders");
        Directory.CreateDirectory(assetsShaderDir);
        foreach (var src in Directory.GetFiles(Path.Combine(repoRoot, "WorldSphereMod", "AssetBundles", "Shaders"), "*.shader"))
        {
            string fn = Path.GetFileName(src);
            // Skip URP variants — needs com.unity.render-pipelines.universal
            // that isn't installed in this bake project; compile errors would
            // taint the entire batch.
            if (fn.IndexOf("URP", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.Log("[WSM3D-Bake] skip URP variant: " + fn);
                continue;
            }
            string dst = Path.Combine(assetsShaderDir, fn);
            File.Copy(src, dst, overwrite: true);
        }
        foreach (var src in new[]
        {
            Path.Combine(repoRoot, "WorldSphereMod", "Resources", "Shaders", "BrpACES.shader"),
            Path.Combine(repoRoot, "WorldSphereMod", "Resources", "Shaders", "BrpBloom.shader"),
        })
        {
            string fn = Path.GetFileName(src);
            if (fn.IndexOf("URP", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.Log("[WSM3D-Bake] skip URP variant: " + fn);
                continue;
            }
            string dst = Path.Combine(assetsShaderDir, fn);
            File.Copy(src, dst, overwrite: true);
        }
        AssetDatabase.Refresh();

        // Tag only the shader assets to the new bundle name.
        foreach (var path in Directory.GetFiles(assetsShaderDir, "*.shader"))
        {
            string rel = "Assets/" + Path.GetRelativePath(Application.dataPath, path).Replace('\\', '/');
            AssetImporter ai = AssetImporter.GetAtPath(rel);
            if (ai != null)
            {
                ai.assetBundleName = "wsm3d-shaders";
                ai.SaveAndReimport();
                Debug.Log("[WSM3D-Bake] tagged wsm3d-shaders: " + rel);
            }
            else
            {
                Debug.LogError("[WSM3D-Bake] AssetImporter NULL for shader: " + rel);
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Build only the shader bundle for win/linux/osx.
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
            // Uncompressed + chunk-based + strict-mode tries to maximize bundle
            // compatibility across Unity versions. Bake target is Unity 6.3 but
            // WorldBox runs Unity 2022 — Unity normally refuses cross-version bundle
            // load. Uncompressed + ChunkBasedCompression sometimes works as a stop-gap.
            BuildPipeline.BuildAssetBundles(
                platformDir,
                BuildAssetBundleOptions.UncompressedAssetBundle |
                BuildAssetBundleOptions.StrictMode,
                target);
            Debug.Log($"[WSM3D-Bake] built wsm3d-shaders bundle for {target} -> {platformDir}");
        }
        Debug.Log("[WSM3D-Bake] All platforms done (shader-only bundle).");
    }

    [MenuItem("WSM3D/Bake wsm3d-shaders AssetBundles")]
    public static void MenuBake() => BakeAll();
}
