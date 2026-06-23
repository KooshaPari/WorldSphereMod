// WSM3D bundle validation — confirms every shader in the freshly-baked
// wsm3d-shaders bundle is a FULL compiled shader (not an 80-byte stripped stub),
// BEFORE shipping. Run via menu: WSM3D → Validate wsm3d-shaders Bundle.
//
// Background (#204/#208): when the AssetBundle variant-strip pass sees no reachable
// ShaderVariantCollection (e.g. m_PreloadedShaders was empty) it drops all compiled
// program data, writing only the ~80-byte serialized header with m_SubProgramBlob
// absent. At load WorldBox reads that stub and aborts with "Mismatched serialization
// in the builtin class 'Shader'. Read N bytes but expected M".
//
// FALSE-POSITIVE-FREE DESIGN: earlier ValidateBundle used bundle.LoadAsset<Shader>()
// then checked sh.name/sh.isSupported.  In the Editor those fields are re-populated
// by the editor's live shader import pipeline (hot-recompile), NOT from the bundle
// bytes — so a stripped stub can report a non-empty name and isSupported==true in-
// Editor while remaining an unusable 80-byte blob at player runtime.
//
// The correct check is shader.subshaderCount (reads the SERIALIZED bundle bytes, not
// the editor pipeline). A stripped stub has subshaderCount == 0. A valid compiled
// shader has subshaderCount >= 1. This accurately reflects the player-side load.
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ValidateBundle
{
    const string WinBundle =
        "E:/Dev/WorldSphereMod/WorldSphereMod/AssetBundles/win/wsm3d-shaders";

    // Minimum average bytes per shader (11 shaders × 2048 = 22,528 byte floor).
    // A bundle where this fails is almost certainly all stubs.
    const long MinBytesPerShader = 2048L;
    const int  ExpectedShaderCount = 11;

    [MenuItem("WSM3D/Validate wsm3d-shaders Bundle")]
    public static void Validate()
    {
        if (!File.Exists(WinBundle))
        {
            Debug.LogError($"[Validate] Bundle not found: {WinBundle}");
            return;
        }

        var info = new FileInfo(WinBundle);
        Debug.Log($"[Validate] Bundle size = {info.Length:N0} bytes " +
                  $"(stub-only floor ~{ExpectedShaderCount * 80} bytes; " +
                  $"valid floor ~{ExpectedShaderCount * MinBytesPerShader:N0} bytes).");

        // Sanity: total size / ExpectedShaderCount must be >= MinBytesPerShader.
        // If every shader is an 80-byte stub the total is ~880 bytes; valid is ~157 kB+.
        long sizePerShader = info.Length / ExpectedShaderCount;
        if (sizePerShader < MinBytesPerShader)
        {
            Debug.LogError($"[Validate] SIZE-FAIL: average {sizePerShader} bytes/shader < {MinBytesPerShader} threshold. " +
                           "Bundle is likely all stripped stubs — do NOT deploy. Re-bake after fixing m_PreloadedShaders registration.");
        }

        var bundle = AssetBundle.LoadFromFile(WinBundle);
        if (bundle == null)
        {
            Debug.LogError("[Validate] AssetBundle.LoadFromFile returned null — bundle unreadable or corrupt.");
            return;
        }

        string[] names = bundle.GetAllAssetNames();
        Debug.Log($"[Validate] Bundle lists {names.Length} assets.");

        int validShaders = 0, stubbedShaders = 0, otherAssets = 0;

        foreach (var name in names)
        {
            // LoadAsset deserializes the bytes from the bundle (not the editor pipeline).
            Object obj = bundle.LoadAsset<Object>(name);
            if (obj == null)
            {
                Debug.LogError($"[Validate] LOAD-NULL: {name} — asset failed to deserialize.");
                stubbedShaders++;
                continue;
            }

            var sh = obj as Shader;
            if (sh == null)
            {
                // Non-shader asset (SVC, compute shader, etc.) — just log OK.
                Debug.Log($"[Validate] OK non-shader: {name} ({obj.GetType().Name})");
                otherAssets++;
                continue;
            }

            // KEY CHECK: subshaderCount reads the serialized blob from the bundle bytes.
            // A stripped stub has subshaderCount == 0 (no m_SubProgramBlob in the bundle).
            // This is FALSE-POSITIVE-FREE — it cannot be spoofed by editor hot-recompile.
            int subCount = sh.subshaderCount;
            if (subCount == 0)
            {
                Debug.LogError($"[Validate] STRIPPED-STUB: {name} -> '{sh.name}' subshaderCount=0. " +
                               "This is an 80-byte stub — the variant strip pass discarded all compiled programs. " +
                               "Root cause: m_PreloadedShaders was empty or INSTANCING_ON variants were absent from SVC.");
                stubbedShaders++;
            }
            else
            {
                Debug.Log($"[Validate] VALID shader: {name} -> '{sh.name}' subshaderCount={subCount}");
                validShaders++;
            }
        }

        bundle.Unload(true);

        string verdict = stubbedShaders == 0 ? "VALID" : "INVALID";
        Debug.Log($"[Validate] RESULT: {validShaders} valid shaders, {stubbedShaders} stubs, {otherAssets} other assets. " +
                  $"Bundle is {verdict}.");
        if (stubbedShaders == 0)
        {
            Debug.Log("[Validate] VALID — safe to set Core.Sphere.ShaderBundleAvailable = true.");
        }
        else
        {
            Debug.LogError("[Validate] INVALID — bundle has stripped stubs. Do NOT deploy. " +
                           "Fix: ensure EnsureShaderVariantCollection registered SVC in GraphicsSettings.m_PreloadedShaders " +
                           "AND INSTANCING_ON variants are present for Impostor/OpaqueVertexColor/StratumVoxelPBR, then re-bake.");
        }
    }
}
