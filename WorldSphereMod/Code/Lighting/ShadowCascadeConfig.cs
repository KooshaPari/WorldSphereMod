using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldSphereMod.Lighting
{
    // Full reflection because WorldBox ships without URP DLLs in worldbox_Data/Managed,
    // so UniversalRenderPipelineAsset is unavailable at compile time. Runtime may still
    // have URP injected by other mods or future builds; probe by name and no-op if absent.
    public static class ShadowCascadeConfig
    {
        static bool _stashed;
        static int _origCount;
        static float _origDistance;
        static Vector3 _origCascade4;
        static float _origCascade2;
        static float _origDepthBias;
        static float _origNormalBias;

        const string UrpAssetTypeName = "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset";

        static Type? GetUrpAssetType()
        {
            Type? t = Type.GetType(UrpAssetTypeName + ", Unity.RenderPipelines.Universal.Runtime", throwOnError: false);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(UrpAssetTypeName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }

        static UnityEngine.Object? GetActiveUrpAsset(Type urpType)
        {
            var asset = GraphicsSettings.currentRenderPipeline;
            if (asset != null && urpType.IsInstanceOfType(asset)) return asset;
            asset = GraphicsSettings.defaultRenderPipeline;
            if (asset != null && urpType.IsInstanceOfType(asset)) return asset;
            return null;
        }

        static object? ReadProp(UnityEngine.Object target, Type type, string name)
        {
            try
            {
                var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (p != null && p.CanRead) return p.GetValue(target);
                var f = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) return f.GetValue(target);
            }
            catch (Exception e) { Debug.LogWarning($"ShadowCascadeConfig: read {name} failed: {e.Message}"); }
            return null;
        }

        static void WriteProp(UnityEngine.Object target, Type type, string name, object value)
        {
            try
            {
                var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (p != null && p.CanWrite) { p.SetValue(target, value); return; }
                var f = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) { f.SetValue(target, value); return; }
                Debug.LogWarning($"ShadowCascadeConfig: no settable member {name}");
            }
            catch (Exception e) { Debug.LogWarning($"ShadowCascadeConfig: write {name} failed: {e.Message}"); }
        }

        public static void Apply(bool highShadows)
        {
            Type? urpType = GetUrpAssetType();
            if (urpType == null)
            {
                Debug.LogWarning("ShadowCascadeConfig: URP not in use (UniversalRenderPipelineAsset type not found).");
                return;
            }
            UnityEngine.Object? urp = GetActiveUrpAsset(urpType);
            if (urp == null)
            {
                Debug.LogWarning("ShadowCascadeConfig: URP not in use (no active UniversalRenderPipelineAsset).");
                return;
            }

            if (!_stashed)
            {
                _origCount = (int)(ReadProp(urp, urpType, "shadowCascadeCount") ?? 1);
                _origDistance = (float)(ReadProp(urp, urpType, "shadowDistance") ?? 50f);
                _origCascade4 = (Vector3)(ReadProp(urp, urpType, "cascade4Split") ?? new Vector3(0.067f, 0.2f, 0.467f));
                _origCascade2 = (float)(ReadProp(urp, urpType, "cascade2Split") ?? 0.25f);
                _origDepthBias = (float)(ReadProp(urp, urpType, "shadowDepthBias") ?? 1.0f);
                _origNormalBias = (float)(ReadProp(urp, urpType, "shadowNormalBias") ?? 1.0f);
                _stashed = true;
            }

            if (highShadows)
            {
                WriteProp(urp, urpType, "shadowCascadeCount", 4);
                WriteProp(urp, urpType, "cascade4Split", new Vector3(0.067f, 0.2f, 0.467f));
            }
            else
            {
                WriteProp(urp, urpType, "shadowCascadeCount", 2);
                WriteProp(urp, urpType, "cascade2Split", 0.25f);
            }
            WriteProp(urp, urpType, "shadowDistance", 50f);
            WriteProp(urp, urpType, "shadowDepthBias", 1.0f);
            WriteProp(urp, urpType, "shadowNormalBias", 1.0f);
        }

        public static void Reset()
        {
            if (!_stashed) return;
            Type? urpType = GetUrpAssetType();
            if (urpType == null) { _stashed = false; return; }
            UnityEngine.Object? urp = GetActiveUrpAsset(urpType);
            if (urp == null) { _stashed = false; return; }

            WriteProp(urp, urpType, "shadowCascadeCount", _origCount);
            WriteProp(urp, urpType, "shadowDistance", _origDistance);
            WriteProp(urp, urpType, "cascade4Split", _origCascade4);
            WriteProp(urp, urpType, "cascade2Split", _origCascade2);
            WriteProp(urp, urpType, "shadowDepthBias", _origDepthBias);
            WriteProp(urp, urpType, "shadowNormalBias", _origNormalBias);
            _stashed = false;
        }
    }
}
