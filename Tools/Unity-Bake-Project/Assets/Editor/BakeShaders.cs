using System.IO;
using UnityEditor;
using UnityEngine;

public static class BakeShaders
{
    public static void BakeAll()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
        string shaderSrc = Path.Combine(repoRoot, "WorldSphereMod", "AssetBundles", "Shaders");
        string assetsShaderDir = Path.Combine(Application.dataPath, "WSM3D", "Shaders");
        Directory.CreateDirectory(assetsShaderDir);

        foreach (var src in Directory.GetFiles(shaderSrc, "*.shader"))
        {
            string dst = Path.Combine(assetsShaderDir, Path.GetFileName(src));
            File.Copy(src, dst, overwrite: true);
        }
        AssetDatabase.Refresh();

        foreach (var path in Directory.GetFiles(assetsShaderDir, "*.shader"))
        {
            string rel = "Assets/" + Path.GetRelativePath(Application.dataPath, path).Replace('\\', '/');
            AssetImporter ai = AssetImporter.GetAtPath(rel);
            if (ai != null) ai.assetBundleName = "worldsphere";
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

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
            Debug.Log($"[WSM3D-Bake] {target} -> {platformDir}");
        }
        Debug.Log("[WSM3D-Bake] All platforms done.");
    }

    [MenuItem("WSM3D/Bake worldsphere AssetBundles")]
    public static void MenuBake() => BakeAll();
}
