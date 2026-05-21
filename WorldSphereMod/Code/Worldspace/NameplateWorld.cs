using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.Worldspace
{
    /// <summary>
    /// Phase 7 Step 2. Per-actor world-space name label attached to the actor head object.
    /// Uses a <c>TextMesh3D</c> when available, then faces the camera each <see cref="LateUpdate"/>.
    /// </summary>
    public sealed class NameplateWorld : MonoBehaviour
    {
        internal Actor? Actor;
        Text? _fallbackLabel;
        Component? _label3d;

        static Font? _labelFont;
        static readonly Dictionary<Actor, NameplateText> _suppressedUpstream = new();
        static readonly Type? s_textMesh3DType = GetTextMesh3DType();

        public const float kFadeNear = 10f;
        public const float kFadeFar = 30f;

        public static NameplateWorld? Attach(Actor a, Transform rigRoot)
        {
            if (a == null || rigRoot == null) return null;
            if (!Core.savedSettings.WorldspaceLabel3D) return null;

            Transform parent = ResolveHeadTransform(a) ?? rigRoot;
            var existing = parent.GetComponentInChildren<NameplateWorld>(true);
            if (existing != null) return existing;

            if (_labelFont == null)
            {
                _labelFont = ResolveFont("Helvetica Bold") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            string name = a.getName() ?? string.Empty;
            GameObject go = new GameObject("nameplate");
            Transform t = go.transform;
            t.SetParent(parent, worldPositionStays: false);
            t.localPosition = Vector3.zero;
            t.localScale = Vector3.one;

            var np = go.AddComponent<NameplateWorld>();
            np.Actor = a;
            SuppressUpstreamNameplate(a);
            np._label3d = CreateTextMesh3D(go, name);
            if (np._label3d == null)
            {
                SetupFallbackCanvasLabel(go, name);
                np._fallbackLabel = go.GetComponentInChildren<Text>(true);
            }

            if (np._label3d == null && np._fallbackLabel == null)
            {
                UnityEngine.Object.Destroy(go);
                RestoreUpstreamNameplate(a);
                return null;
            }

            return np;
        }

        public static void Detach(Actor a)
        {
            if (a == null) return;
            var renderer = WorldUIRenderer.Instance;
            if (renderer == null) return;
            if (renderer.Rigs.TryGetValue(a, out Transform rig) && rig != null)
            {
                var npFromRig = rig.GetComponentInChildren<NameplateWorld>(true);
                if (npFromRig != null)
                {
                    UnityEngine.Object.Destroy(npFromRig.gameObject);
                }
            }

            var np = Resources.FindObjectsOfTypeAll<NameplateWorld>()
                .FirstOrDefault(x => x != null && x.Actor == a);
            if (np != null)
            {
                UnityEngine.Object.Destroy(np.gameObject);
            }
            RestoreUpstreamNameplate(a);
        }

        public void Refresh(Vector3 worldPos, float camDistance)
        {
            ApplyFade(camDistance);
        }

        public static void Reset()
        {
            foreach (var kv in _suppressedUpstream)
            {
                RestoreUpstreamNameplate(kv.Key, kv.Value);
            }
            _suppressedUpstream.Clear();
        }

        void OnDestroy()
        {
            if (Actor != null)
            {
                RestoreUpstreamNameplate(Actor);
            }
        }

        void LateUpdate()
        {
            if (Actor == null) return;
            var cam = CameraManager.MainCamera;
            if (cam == null) return;

            float d = Vector3.Distance(cam.transform.position, transform.position);
            transform.LookAt(cam.transform.position, Vector3.up);

            ApplyFade(d);
        }

        void ApplyFade(float camDistance)
        {
            float alpha = 1f - Mathf.InverseLerp(kFadeNear, kFadeFar, camDistance);

            if (_fallbackLabel != null)
            {
                Color c = _fallbackLabel.color;
                c.a = alpha;
                _fallbackLabel.color = c;
                return;
            }

            if (_label3d == null) return;
            SetColorValue(_label3d, new Color(1f, 1f, 1f, alpha), "color", "textColor");
        }

        static void SetupFallbackCanvasLabel(GameObject parent, string name)
        {
            var canvas = parent.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = CameraManager.MainCamera;

            var textGo = new GameObject("label");
            textGo.transform.SetParent(parent.transform, worldPositionStays: false);
            var text = textGo.AddComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 18;
            text.font = _labelFont;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.text = name;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var rt = text.rectTransform;
            rt.sizeDelta = new Vector2(6f, 1.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        static Component? CreateTextMesh3D(GameObject host, string text)
        {
            if (s_textMesh3DType == null) return null;
            var label = host.AddComponent(s_textMesh3DType);
            if (label == null) return null;

            SetTextValue(label, text);
            if (!SetColorValue(label, Color.white, "color"))
            {
                SetColorValue(label, Color.white, "textColor");
            }

            SetColorValue(label, Color.black, "outlineColor", "outline_color");
            SetBoolValue(label, true, "outline");
            SetFloatValue(label, 0.5f, "size");
            SetFontValue(label, _labelFont);

            return label;
        }

        static bool SetTextValue(Component target, string text)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] names = { "text", "labelText" };
            foreach (string name in names)
            {
                PropertyInfo? p = target.GetType().GetProperty(name, flags);
                if (p != null && p.CanWrite && p.PropertyType == typeof(string))
                {
                    p.SetValue(target, text);
                    return true;
                }

                FieldInfo? f = target.GetType().GetField(name, flags);
                if (f != null && f.FieldType == typeof(string))
                {
                    f.SetValue(target, text);
                    return true;
                }
            }

            return false;
        }

        static bool SetColorValue(Component target, Color color, params string[] names)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in names)
            {
                PropertyInfo? p = target.GetType().GetProperty(name, flags);
                if (p != null && p.CanWrite && p.PropertyType == typeof(Color))
                {
                    p.SetValue(target, color);
                    return true;
                }

                FieldInfo? f = target.GetType().GetField(name, flags);
                if (f != null && f.FieldType == typeof(Color))
                {
                    f.SetValue(target, color);
                    return true;
                }
            }

            return false;
        }

        static bool SetBoolValue(Component target, bool value, params string[] names)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in names)
            {
                PropertyInfo? p = target.GetType().GetProperty(name, flags);
                if (p != null && p.CanWrite && p.PropertyType == typeof(bool))
                {
                    p.SetValue(target, value);
                    return true;
                }

                FieldInfo? f = target.GetType().GetField(name, flags);
                if (f != null && f.FieldType == typeof(bool))
                {
                    f.SetValue(target, value);
                    return true;
                }
            }

            return false;
        }

        static bool SetFloatValue(Component target, float value, params string[] names)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in names)
            {
                PropertyInfo? p = target.GetType().GetProperty(name, flags);
                if (p != null && p.CanWrite)
                {
                    if (p.PropertyType == typeof(float))
                    {
                        p.SetValue(target, value);
                        return true;
                    }

                    if (p.PropertyType == typeof(int))
                    {
                        p.SetValue(target, (int)Math.Round(value));
                        return true;
                    }
                }

                FieldInfo? f = target.GetType().GetField(name, flags);
                if (f != null)
                {
                    if (f.FieldType == typeof(float))
                    {
                        f.SetValue(target, value);
                        return true;
                    }

                    if (f.FieldType == typeof(int))
                    {
                        f.SetValue(target, (int)Math.Round(value));
                        return true;
                    }
                }
            }

            return false;
        }

        static bool SetFontValue(Component target, Font? font)
        {
            if (font == null) return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in new[] { "font", "fontAsset" })
            {
                PropertyInfo? p = target.GetType().GetProperty(name, flags);
                if (p != null && p.CanWrite && p.PropertyType == typeof(Font))
                {
                    p.SetValue(target, font);
                    return true;
                }

                FieldInfo? f = target.GetType().GetField(name, flags);
                if (f != null && f.FieldType == typeof(Font))
                {
                    f.SetValue(target, font);
                    return true;
                }
            }

            return false;
        }

        static Font? ResolveFont(string name)
        {
            try
            {
                Font? found = Resources.FindObjectsOfTypeAll<Font>()
                    .FirstOrDefault(font => string.Equals(font.name, name, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
            }
            catch
            {
                // Ignore font lookup failures and use built-in fallback.
            }

            return null;
        }

        static Type? GetTextMesh3DType()
        {
            return Type.GetType("TextMesh3D, Assembly-CSharp")
                ?? Type.GetType("TextMesh3D, Assembly-CSharp-Publicized")
                ?? Type.GetType("TextMesh3D");
        }

        static NameplateText? SuppressUpstreamNameplate(Actor actor)
        {
            var head = ResolveHeadTransform(actor);
            if (head == null) return null;
            var upstream = head.GetComponentInChildren<NameplateText>(true);
            if (upstream == null) return null;
            if (_suppressedUpstream.ContainsKey(actor))
            {
                return upstream;
            }

            upstream.enabled = false;
            _suppressedUpstream[actor] = upstream;
            return upstream;
        }

        static Transform? ResolveHeadTransform(Actor actor)
        {
            if (actor == null) return null;

            try
            {
                var t = actor.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo? field = t.GetField("head_object", flags);
                if (field != null)
                {
                    object? value = field.GetValue(actor);
                    if (value is Transform tr) return tr;
                    if (value is GameObject go) return go.transform;
                    if (value is Component comp) return comp.transform;
                }

                PropertyInfo? prop = t.GetProperty("head_object", flags);
                if (prop != null)
                {
                    object? value = prop.GetValue(actor);
                    if (value is Transform tr) return tr;
                    if (value is GameObject go) return go.transform;
                    if (value is Component comp) return comp.transform;
                }
            }
            catch
            {
                // Fall through to rig root.
            }

            return null;
        }

        static void RestoreUpstreamNameplate(Actor actor)
        {
            if (actor == null) return;
            if (_suppressedUpstream.TryGetValue(actor, out var upstream))
            {
                RestoreUpstreamNameplate(actor, upstream);
            }
        }

        static void RestoreUpstreamNameplate(Actor actor, NameplateText label)
        {
            RestoreUpstreamNameplate(label);
            _suppressedUpstream.Remove(actor);
        }

        static void RestoreUpstreamNameplate(NameplateText label)
        {
            if (label == null) return;
            label.enabled = true;
        }
    }
}
