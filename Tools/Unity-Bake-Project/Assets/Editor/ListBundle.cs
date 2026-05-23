using System.IO;
using UnityEditor;
using UnityEngine;

public static class ListBundle
{
    public static void Run()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
        string bundlePath = Path.Combine(repoRoot, "WorldSphereMod", "AssetBundles", "win", "worldsphere");
        AssetBundle ab = AssetBundle.LoadFromFile(bundlePath);
        if (ab == null) { Debug.LogError("[List] bundle null"); return; }
        Debug.Log("[List] bundle: " + bundlePath);
        foreach (string n in ab.GetAllAssetNames())
        {
            Debug.Log("[List] " + n);
        }
        ab.Unload(false);
    }
}
