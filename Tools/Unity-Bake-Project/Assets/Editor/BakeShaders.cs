using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BakeShaders
{
    const string SvcAssetPath = "Assets/WSM3D/wsm3d-shader-variants.shadervariants";

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
            Path.Combine(repoRoot, "WorldSphereMod", "Resources", "Shaders", "ScreenSpaceGI.shader"),
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

        // Belt-and-suspenders: add every shader to GraphicsSettings.alwaysIncludedShaders
        // so the stripping pipeline cannot remove them regardless of SVC state.
        PinShadersInGraphicsSettings(assetsShaderDir);

        // Build (or update) the ShaderVariantCollection so Unity 2022.3 variant
        // stripping cannot remove any of our 10 shaders from the bundle.
        // Without an SVC that references them, Unity treats shader variants as
        // unreachable and silently strips them during AssetBundle compilation.
        EnsureShaderVariantCollection(assetsShaderDir);

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
                BuildAssetBundleOptions.ForceRebuildAssetBundle |
                BuildAssetBundleOptions.StrictMode,
                target);
            Debug.Log($"[WSM3D-Bake] built wsm3d-shaders bundle for {target} -> {platformDir}");
        }
        Debug.Log("[WSM3D-Bake] All platforms done (shader-only bundle).");
    }

    // Creates or refreshes the ShaderVariantCollection at SvcAssetPath, adds
    // one no-keyword Normal-pass entry for every shader in assetsShaderDir,
    // tags the SVC asset to the wsm3d-shaders bundle, and registers it in
    // PlayerSettings.preloadedAssets so the loader materialises it on startup.
    static void EnsureShaderVariantCollection(string assetsShaderDir)
    {
        var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(SvcAssetPath);
        if (svc == null)
        {
            svc = new ShaderVariantCollection();
            AssetDatabase.CreateAsset(svc, SvcAssetPath);
            Debug.Log("[WSM3D-Bake] created ShaderVariantCollection: " + SvcAssetPath);
        }
        else
        {
            svc.Clear();
        }

        int added = 0;
        foreach (var path in Directory.GetFiles(assetsShaderDir, "*.shader"))
        {
            string rel = "Assets/" + Path.GetRelativePath(Application.dataPath, path).Replace('\\', '/');
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(rel);
            if (shader == null)
            {
                Debug.LogError("[WSM3D-Bake] SVC: shader not found at " + rel);
                continue;
            }
            // One no-keyword entry for the default forward pass (passType=Normal=0).
            // This is the minimum needed to tell the stripping pipeline "keep this shader."
            var variant = new ShaderVariantCollection.ShaderVariant(shader, PassType.Normal);
            if (!svc.Contains(variant))
                svc.Add(variant);
            added++;
            Debug.Log("[WSM3D-Bake] SVC +variant: " + shader.name);
        }

        EditorUtility.SetDirty(svc);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Tag the SVC itself to the same bundle so it travels with the shaders.
        var svcImporter = AssetImporter.GetAtPath(SvcAssetPath);
        if (svcImporter != null)
        {
            svcImporter.assetBundleName = "wsm3d-shaders";
            svcImporter.SaveAndReimport();
        }

        // Register as a preloaded asset so Unity initialises the SVC before any
        // scene loads — this is the second stripping guard beyond the bundle tag.
        var preloaded = PlayerSettings.GetPreloadedAssets().ToList();
        if (!preloaded.Contains(svc))
        {
            preloaded.Add(svc);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            Debug.Log("[WSM3D-Bake] registered SVC in PlayerSettings.preloadedAssets");
        }

        Debug.Log($"[WSM3D-Bake] ShaderVariantCollection ready — {added} shaders pinned.");
    }

    // Adds every shader in assetsShaderDir to GraphicsSettings.alwaysIncludedShaders
    // at editor-time, mirroring what the ProjectSettings/GraphicsSettings.asset YAML
    // already encodes.  Running this at bake time ensures the list stays current
    // even if Unity reimports and resets the asset file.
    static void PinShadersInGraphicsSettings(string assetsShaderDir)
    {
        // Load via the undocumented but stable SerializedObject path for ProjectSettings assets.
        var gsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (gsAssets == null || gsAssets.Length == 0)
        {
            Debug.LogWarning("[WSM3D-Bake] Could not load GraphicsSettings.asset for SerializedObject edit; skipping runtime pin.");
            return;
        }
        var gsSo = new SerializedObject(gsAssets[0]);
        var alwaysProp = gsSo.FindProperty("m_AlwaysIncludedShaders");
        if (alwaysProp == null)
        {
            Debug.LogWarning("[WSM3D-Bake] m_AlwaysIncludedShaders property not found; skipping runtime pin.");
            return;
        }

        int pinned = 0;
        foreach (var path in Directory.GetFiles(assetsShaderDir, "*.shader"))
        {
            string rel = "Assets/" + Path.GetRelativePath(Application.dataPath, path).Replace('\\', '/');
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(rel);
            if (shader == null)
            {
                Debug.LogWarning("[WSM3D-Bake] PinShaders: shader not found at " + rel);
                continue;
            }

            // Check if already present to avoid duplicates.
            bool found = false;
            for (int i = 0; i < alwaysProp.arraySize; i++)
            {
                var elem = alwaysProp.GetArrayElementAtIndex(i);
                if (elem.objectReferenceValue == shader) { found = true; break; }
            }
            if (!found)
            {
                alwaysProp.arraySize++;
                var newElem = alwaysProp.GetArrayElementAtIndex(alwaysProp.arraySize - 1);
                newElem.objectReferenceValue = shader;
                pinned++;
                Debug.Log("[WSM3D-Bake] PinShaders: added to AlwaysIncluded: " + shader.name);
            }
        }

        if (pinned > 0)
        {
            gsSo.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[WSM3D-Bake] PinShaders: {pinned} shaders added to GraphicsSettings.alwaysIncludedShaders.");
        }
        else
        {
            Debug.Log("[WSM3D-Bake] PinShaders: all shaders already present in alwaysIncludedShaders.");
        }
    }

    [MenuItem("WSM3D/Bake wsm3d-shaders AssetBundles")]
    public static void MenuBake() => BakeAll();
}
