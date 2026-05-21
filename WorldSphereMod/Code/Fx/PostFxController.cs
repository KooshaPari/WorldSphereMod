using System;
using System.Reflection;
using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.Fx
{
    /// <summary>
    /// Phase 9 Step 3. Manages a global URP <c>Volume</c> + <c>VolumeProfile</c> plus the
    /// camera-data <c>renderPostProcessing</c> toggle, all via reflection because WorldBox's
    /// <c>Managed/</c> ships no URP runtime DLLs. If URP isn't actually present at runtime
    /// (no override types resolve), every entry point logs a warning and returns cleanly.
    ///
    /// Mirrors the resilience pattern of <see cref="Lighting.ShadowCascadeConfig"/>: every
    /// reflective write is wrapped so a property rename in a future URP build degrades to
    /// a missing-effect rather than a crash.
    /// </summary>
    public static class PostFxController
    {
        static GameObject? _volumeGO;
        static ScriptableObject? _profile;
        static bool _postFxUnavailable;

        const string VolumeTypeName = "UnityEngine.Rendering.Volume";
        const string VolumeProfileTypeName = "UnityEngine.Rendering.VolumeProfile";
        const string AdditionalCameraDataTypeName = "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData";
        const string AdditionalCameraDataExtTypeName = "UnityEngine.Rendering.Universal.CameraExtensions";
        const string BloomTypeName = "UnityEngine.Rendering.Universal.Bloom";
        const string ColorAdjustmentsTypeName = "UnityEngine.Rendering.Universal.ColorAdjustments";
        const string VignetteTypeName = "UnityEngine.Rendering.Universal.Vignette";
        const string TonemappingTypeName = "UnityEngine.Rendering.Universal.Tonemapping";

        static Type? FindType(string fullName)
        {
            Type? t = Type.GetType(fullName + ", Unity.RenderPipelines.Universal.Runtime", throwOnError: false);
            if (t != null) return t;
            t = Type.GetType(fullName + ", Unity.RenderPipelines.Core.Runtime", throwOnError: false);
            if (t != null) return t;
            t = Type.GetType(fullName, throwOnError: false);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { /* ignore broken assemblies */ }
            }
            return null;
        }

        static void TryWrite(object target, Type type, string name, object value)
        {
            try
            {
                var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (p != null && p.CanWrite) { p.SetValue(target, value); return; }
                var f = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) { f.SetValue(target, value); return; }
                Debug.LogWarning($"[WSM3D] PostFxController: no settable member {type.Name}.{name}");
            }
            catch (Exception e) { Debug.LogWarning($"[WSM3D] PostFxController: write {type.Name}.{name} failed: {e.Message}"); }
        }

        // Sets `value` on the URP override's parameter named `paramName`. URP parameters are
        // a wrapper class with a `.value` field — we walk one level into it before assigning.
        static void TryWriteParam(object overrideTarget, string paramName, object value)
        {
            try
            {
                Type t = overrideTarget.GetType();
                var f = t.GetField(paramName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                object? param = f?.GetValue(overrideTarget);
                if (param == null)
                {
                    var p = t.GetProperty(paramName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (p != null) param = p.GetValue(overrideTarget);
                }
                if (param == null)
                {
                    Debug.LogWarning($"[WSM3D] PostFxController: parameter {t.Name}.{paramName} not found");
                    return;
                }
                Type pt = param.GetType();
                var valueField = pt.GetField("value", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (valueField != null)
                {
                    object coerced = value;
                    if (valueField.FieldType.IsEnum)
                    {
                        try
                        {
                            coerced = Enum.Parse(valueField.FieldType, value.ToString() ?? string.Empty);
                        }
                        catch { coerced = value; }
                    }
                    if (!valueField.FieldType.IsEnum && valueField.FieldType != value.GetType() && value is IConvertible)
                    {
                        try { coerced = Convert.ChangeType(value, valueField.FieldType); }
                        catch { coerced = value; }
                    }
                    valueField.SetValue(param, coerced);
                }
                var overrideField = pt.GetField("overrideState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                overrideField?.SetValue(param, true);
            }
            catch (Exception e) { Debug.LogWarning($"[WSM3D] PostFxController: write param {paramName} failed: {e.Message}"); }
        }

        static object? GetUniversalAdditionalCameraData(Camera cam, Type additionalDataType)
        {
            try
            {
                var existing = cam.GetComponent(additionalDataType);
                if (existing != null) return existing;
            }
            catch (Exception e) { Debug.LogWarning($"[WSM3D] PostFxController: GetComponent UACD failed: {e.Message}"); }

            // Walk known extension method holders.
            string[] candidates =
            {
                AdditionalCameraDataExtTypeName,
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraDataExtensions",
            };
            foreach (var name in candidates)
            {
                Type? ext = FindType(name);
                if (ext == null) continue;
                try
                {
                    var m = ext.GetMethod("GetUniversalAdditionalCameraData", BindingFlags.Static | BindingFlags.Public);
                    if (m != null) return m.Invoke(null, new object[] { cam });
                }
                catch (Exception e) { Debug.LogWarning($"[WSM3D] PostFxController: {name}.GetUniversalAdditionalCameraData failed: {e.Message}"); }
            }

            // Fallback: AddComponent if extension method not located.
            try { return cam.gameObject.AddComponent(additionalDataType); }
            catch (Exception e) { Debug.LogWarning($"[WSM3D] PostFxController: AddComponent UACD failed: {e.Message}"); return null; }
        }

        public static void Create()
        {
            if (!Core.savedSettings.PostFX || !Core.IsWorld3D) return;
            if (_volumeGO != null) return;

            Type? volumeType = FindType(VolumeTypeName);
            Type? profileType = FindType(VolumeProfileTypeName);
            Type? additionalDataType = FindType(AdditionalCameraDataTypeName);
            Type? bloomType = FindType(BloomTypeName);
            Type? colorAdjustmentsType = FindType(ColorAdjustmentsTypeName);
            Type? vignetteType = FindType(VignetteTypeName);
            Type? tonemappingType = FindType(TonemappingTypeName);

            if (volumeType == null || profileType == null || additionalDataType == null
                || bloomType == null || colorAdjustmentsType == null || vignetteType == null)
            {
                Debug.LogWarning("[WSM3D] PostFxController: URP types not present at runtime — post-FX disabled.");
                _postFxUnavailable = true;
                return;
            }

            try
            {
                _volumeGO = new GameObject("WSM3D.PostFxVolume");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WSM3D] PostFxController: failed to allocate volume GO: {e.Message}");
                return;
            }

            Component? volume;
            try
            {
                volume = _volumeGO.AddComponent(volumeType);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WSM3D] PostFxController: AddComponent Volume failed: {e.Message}");
                UnityEngine.Object.Destroy(_volumeGO);
                _volumeGO = null;
                return;
            }

            TryWrite(volume!, volumeType, "isGlobal", true);
            TryWrite(volume!, volumeType, "weight", 1.0f);
            TryWrite(volume!, volumeType, "priority", 0f);

            try
            {
                _profile = ScriptableObject.CreateInstance(profileType);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WSM3D] PostFxController: failed to create VolumeProfile: {e.Message}");
                _profile = null;
            }

            if (_profile != null)
            {
                MethodInfo? addComp = profileType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(Type), typeof(bool) }, null);

                object? bloom = TryAddOverride(addComp, _profile, bloomType);
                if (bloom != null)
                {
                    TryWriteParam(bloom, "threshold", 0.9f);
                    TryWriteParam(bloom, "intensity", 0.4f);
                    TryWriteParam(bloom, "scatter", 0.7f);
                }

                object? color = TryAddOverride(addComp, _profile, colorAdjustmentsType);
                if (color != null)
                {
                    TryWriteParam(color, "contrast", 8f);
                    TryWriteParam(color, "saturation", 5f);
                }

                object? vignette = TryAddOverride(addComp, _profile, vignetteType);
                if (vignette != null)
                {
                    TryWriteParam(vignette, "intensity", 0.25f);
                    TryWriteParam(vignette, "smoothness", 0.4f);
                }

                if (tonemappingType != null)
                {
                    object? tonemapping = TryAddOverride(addComp, _profile, tonemappingType);
                    if (tonemapping != null)
                    {
                        TryWriteParam(tonemapping, "mode", "ACES");
                    }
                }

                TryWrite(volume!, volumeType, "sharedProfile", _profile);
                TryWrite(volume!, volumeType, "profile", _profile);
            }

            Camera cam = CameraManager.MainCamera;
            if (cam != null)
            {
                object? acd = GetUniversalAdditionalCameraData(cam, additionalDataType);
                if (acd != null) TryWrite(acd, additionalDataType, "renderPostProcessing", true);
            }
        }

        static object? TryAddOverride(MethodInfo? addComp, ScriptableObject profile, Type overrideType)
        {
            try
            {
                if (addComp != null)
                {
                    return addComp.Invoke(profile, new object[] { overrideType, true });
                }
                // Fallback: locate any Add(Type, ...) variant.
                foreach (var m in profile.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (m.Name != "Add") continue;
                    var ps = m.GetParameters();
                    if (ps.Length >= 1 && ps[0].ParameterType == typeof(Type))
                    {
                        object[] args = new object[ps.Length];
                        args[0] = overrideType;
                        for (int i = 1; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue! : (object)true;
                        return m.Invoke(profile, args);
                    }
                }
                Debug.LogWarning($"[WSM3D] PostFxController: VolumeProfile.Add not found for {overrideType.Name}");
            }
            catch (Exception e) { Debug.LogWarning($"[WSM3D] PostFxController: Add override {overrideType.Name} failed: {e.Message}"); }
            return null;
        }

        public static void Destroy()
        {
            if (_volumeGO == null) return;

            Type? additionalDataType = FindType(AdditionalCameraDataTypeName);
            Camera cam = CameraManager.MainCamera;
            if (additionalDataType != null && cam != null)
            {
                object? acd = null;
                try { acd = cam.GetComponent(additionalDataType); }
                catch (Exception e) { Debug.LogWarning($"[WSM3D] PostFxController: GetComponent UACD during Destroy failed: {e.Message}"); }
                if (acd != null) TryWrite(acd, additionalDataType, "renderPostProcessing", false);
            }

            try { UnityEngine.Object.Destroy(_volumeGO); }
            catch (Exception e) { Debug.LogWarning($"[WSM3D] PostFxController: Destroy GO failed: {e.Message}"); }
            _volumeGO = null;

            if (_profile != null)
            {
                try { UnityEngine.Object.Destroy(_profile); }
                catch (Exception e) { Debug.LogWarning($"[WSM3D] PostFxController: Destroy profile failed: {e.Message}"); }
                _profile = null;
            }
        }

        public static void ApplySetting(bool enabled)
        {
            if (_postFxUnavailable)
                return;
            if (enabled && _volumeGO == null) Create();
            else if (!enabled && _volumeGO != null) Destroy();
        }
    }
}
