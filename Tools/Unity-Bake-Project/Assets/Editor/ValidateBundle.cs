// WSM3D bundle validation — confirms every shader in the freshly-baked
// wsm3d-shaders bundle DESERIALIZES without a native ManagedStream abort,
// BEFORE shipping. Run via menu: WSM3D → Validate wsm3d-shaders Bundle.
//
// Background (#204/#208): a bundle baked in a Unity patch version that
// differs from the WorldBox runtime (2022.3.60f1) produces shaders whose
// serialized byte layout mismatches → "Mismatched serialization in builtin
// class 'Shader'. Read N bytes but expected M" → native crash on load.
// This validator loads each shader individually so a bad one is named, and
// reports per-shader OK/empty-name/unsupported so you can confirm a clean
// bake before flipping Core.Sphere.ShaderBundleAvailable = true.
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ValidateBundle
{
    const string WinBundle =
        "E:/Dev/WorldSphereMod/WorldSphereMod/AssetBundles/win/wsm3d-shaders";

    [MenuItem("WSM3D/Validate wsm3d-shaders Bundle")]
    public static void Validate()
    {
        if (!File.Exists(WinBundle))
        {
            Debug.LogError($"[Validate] Bundle not found: {WinBundle}");
            return;
        }
        var info = new FileInfo(WinBundle);
        Debug.Log($"[Validate] Bundle size = {info.Length} bytes (corrupt-stub is ~80; valid is ~157,000+).");

        var bundle = AssetBundle.LoadFromFile(WinBundle);
        if (bundle == null)
        {
            Debug.LogError("[Validate] AssetBundle.LoadFromFile returned null — bundle unreadable.");
            return;
        }

        // GetAllAssetNames does NOT deserialize the shader objects (safe).
        string[] names = bundle.GetAllAssetNames();
        Debug.Log($"[Validate] Bundle lists {names.Length} assets.");

        int ok = 0, bad = 0;
        foreach (var name in names)
        {
            // Load each asset individually. If a shader is layout-mismatched,
            // the native abort happens HERE and names the offending asset in
            // the line above this one in the Editor log.
            Object obj = bundle.LoadAsset<Object>(name);
            if (obj == null)
            {
                Debug.LogError($"[Validate] LOAD-NULL: {name}");
                bad++;
                continue;
            }
            var sh = obj as Shader;
            if (sh != null)
            {
                if (string.IsNullOrEmpty(sh.name))
                {
                    Debug.LogError($"[Validate] EMPTY-NAME (corrupt): {name}");
                    bad++;
                }
                else
                {
                    Debug.Log($"[Validate] OK shader: {name} -> '{sh.name}' (supported={sh.isSupported})");
                    ok++;
                }
            }
            else
            {
                Debug.Log($"[Validate] OK asset: {name} ({obj.GetType().Name})");
                ok++;
            }
        }
        bundle.Unload(true);
        Debug.Log($"[Validate] DONE — {ok} OK, {bad} bad. " +
                  (bad == 0
                    ? "Bundle is VALID — safe to set Core.Sphere.ShaderBundleAvailable = true."
                    : "Bundle has BAD assets — do NOT re-enable; re-bake in the runtime's exact Unity version (2022.3.60f1)."));
    }
}
