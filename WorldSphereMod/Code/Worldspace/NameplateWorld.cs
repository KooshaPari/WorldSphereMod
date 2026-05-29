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
    /// Phase 7 Step 2. Per-actor world-space name label attached to the shared worldspace rig.
    /// Uses a <c>TextMesh3D</c> when available, then faces the camera each <see cref="LateUpdate"/>.
    /// </summary>
    // Run after WorldUIRenderer (order 0) so the rig world-position is already
    // updated before we face the camera. Fixes one-frame lag that looks camera-fixed.
    [UnityEngine.DefaultExecutionOrder(100)]
    public sealed class NameplateWorld : MonoBehaviour
    {
        internal Actor? Actor;
        Text? _fallbackLabel;
        Component? _label3d;

        static Font? _labelFont;
        static readonly Dictionary<Actor, NameplateText> _suppressedUpstream = new();
        static readonly Type? s_textMesh3DType = GetTextMesh3DType();

        /// <summary>
        /// Optional declutter gate. When <c>true</c>, only the actor WorldBox currently
        /// has selected/inspected (or otherwise flagged "important", e.g. a leader) shows a
        /// nameplate; everyone else is hidden. Defaults to <c>false</c> so all visible actors
        /// get a label (matches the upstream NameplateText behaviour). This is a runtime
        /// toggle rather than a SavedSettings field because the Phase 7 worldspace settings
        /// surface lives outside this file; flip it from the bridge/console.
        /// </summary>
        public static bool ShowOnlySelectedActors;

        // Base vanilla actor sprite half-height in world units (mirrors LodSelector).
        // Rendered voxel half-height = kBaseEntityHalfHeight * VoxelScaleMultiplier, so the
        // nameplate must clear that to float above the mesh head instead of the body anchor.
        const float kBaseEntityHalfHeight = 0.5f;
        // Extra clearance above the computed head so descenders/outline never clip the mesh.
        const float kHeadClearance = 0.35f;

        // Reflection handle for the WorldBox selected/inspected actor, resolved lazily so we
        // never hard-depend on a field name that shifts across WorldBox builds.
        static bool s_selectedActorProbed;
        static FieldInfo? s_selectedActorField;
        static PropertyInfo? s_selectedActorProp;

        public static NameplateWorld? Attach(Actor a, Transform rigRoot)
        {
            if (a == null || rigRoot == null) return null;
            // WorldspaceLabel3D controls whether to PREFER 3D text (TextMesh3D).
            // Nameplates always render when WorldspaceUI is enabled; the flag only
            // selects the rendering path (3D text vs canvas/Text fallback).
            // Removing the early-return that was the root cause of missing labels (#191).
            bool prefer3D = Core.savedSettings != null && Core.savedSettings.WorldspaceLabel3D;

            Transform parent = rigRoot;
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
            // Lift the label above the voxel mesh head. The rig anchor sits at
            // current_position + kRigLift (mid-body); the rendered voxel reaches up
            // ~kBaseEntityHalfHeight * VoxelScaleMultiplier above its own centre, so without
            // this offset the text drew through the actor's chest. Rig localScale is 1, so
            // this local Y is world units.
            t.localPosition = new Vector3(0f, HeadOffset(), 0f);
            float baseScale = Core.savedSettings != null ? Core.savedSettings.NameplateBaseScale : 0.04f;
            t.localScale = Vector3.one * baseScale;

            var np = go.AddComponent<NameplateWorld>();
            np.Actor = a;
            SuppressUpstreamNameplate(a);
            // Only attempt 3D text when the setting explicitly opts in AND the type exists.
            np._label3d = prefer3D ? CreateTextMesh3D(go, name) : null;
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

            // Declutter gate: when enabled, hide every nameplate except the
            // selected/important actor's. Toggle the label visibility (not the GameObject) so
            // this MonoBehaviour keeps ticking and re-shows the instant selection changes.
            bool gatedOut = ShowOnlySelectedActors && !IsImportant(Actor);
            if (gatedOut)
            {
                SetLabelVisible(false);
                return;
            }

            // Keep the head offset live: VoxelScaleMultiplier can change at runtime via the
            // bridge, which would otherwise leave the label stranded at the old head height.
            float headY = HeadOffset();
            Vector3 lp = transform.localPosition;
            if (!Mathf.Approximately(lp.y, headY))
            {
                lp.y = headY;
                transform.localPosition = lp;
            }

            float d = Vector3.Distance(cam.transform.position, transform.position);

            // Billboard so the readable face points at the camera. TextMesh3D / Canvas Text
            // read off the -Z face, so we aim local +Z *away* from the camera
            // (position - camPos): that puts the readable -Z face toward the viewer without a
            // separate 180° flip. World-up locks roll so text stays horizontal and legible
            // even at oblique strategy-zoom pitch.
            transform.rotation =
                Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);

            // WHY: prior `Max(baseScale, Min(1, d/100))` snapped localScale to ~1.0 at
            // any strategy-view distance — ~6.7x the 0.15 base — making labels dwarf the
            // actor; anchor on baseScale, then scale DOWN with distance so far tags
            // never exceed ~1.5x tile-width, and never inflate as the camera pulls
            // back. Closer-than-ref tags still grow (capped at baseScale*maxScale).
            var s = Core.savedSettings;
            float baseScale = s != null ? s.NameplateBaseScale : 0.08f;
            float refDist = s != null ? s.NameplateReferenceDistance : 10f;
            float minScale = s != null ? s.NameplateMinScale : 0.25f;
            float maxScale = s != null ? s.NameplateMaxScale : 1.5f;
            // #208: shrink worldspace nametags to read at default zoom.
            // WHY: prior `baseScale * distFactor` shrank labels toward 0 at
            // strategy-view distance (3*refDist), which made the close-zoom
            // view show nametags the full baseScale — too large relative to
            // the voxel actor. Clamp the upper end so even at refDist the
            // label stays a fraction of the actor mesh height.
            float distFactor = refDist > 0.0001f
                ? Mathf.Clamp01(d / (refDist * 3f))
                : 1f;
            // cap baseScale-at-refDist to baseScale * 0.5 so close-zoom tags
            // read at ~half the previously-acceptable size.
            float effective = Mathf.Clamp(
                baseScale * distFactor * 0.5f,
                baseScale * minScale * 0.5f,
                baseScale * maxScale * 0.5f);
            transform.localScale = Vector3.one * effective;

            Debug.Log($"[WSM3D][BANNER] nametag-shrink v2.13 active, fontSize=6, baseScale={baseScale:F3}, distFactor={distFactor:F3}, effective={effective:F3}");
            ApplyFade(d);
        }

        void ApplyFade(float camDistance)
        {
            float fadeNear = Core.savedSettings != null ? Core.savedSettings.NameplateFadeNear : 10f;
            float fadeFar = Core.savedSettings != null ? Core.savedSettings.NameplateFadeFar : 30f;
            float alpha = 1f - Mathf.InverseLerp(fadeNear, fadeFar, camDistance);

            // Beyond fadeFar the label is fully transparent. Hide the label component so it
            // stops eating draw calls / text layout at strategy zoom where hundreds of actors
            // are on screen; re-show it when the camera comes back within range. We toggle the
            // label, not the GameObject, so LateUpdate keeps running to detect that return.
            if (alpha <= 0f)
            {
                SetLabelVisible(false);
                return;
            }
            SetLabelVisible(true);

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

        void SetLabelVisible(bool visible)
        {
            if (_fallbackLabel != null)
            {
                // The fallback Canvas + Text live on a child "label" GameObject; toggling it
                // leaves this NameplateWorld component active and ticking.
                Transform labelTf = _fallbackLabel.transform;
                if (labelTf != null && labelTf.gameObject.activeSelf != visible)
                {
                    labelTf.gameObject.SetActive(visible);
                }
                return;
            }

            // TextMesh3D is a Behaviour on this same GameObject; flip its enabled flag so the
            // renderer stops drawing while LateUpdate keeps running.
            if (_label3d is Behaviour beh)
            {
                if (beh.enabled != visible) beh.enabled = visible;
            }
        }

        static float HeadOffset()
        {
            float voxelScale = Core.savedSettings != null
                ? Mathf.Max(0.0001f, Core.savedSettings.VoxelScaleMultiplier)
                : 8f;
            return kBaseEntityHalfHeight * voxelScale + kHeadClearance;
        }

        static bool IsImportant(Actor actor)
        {
            if (actor == null) return false;
            Actor? selected = ResolveSelectedActor();
            return selected != null && ReferenceEquals(selected, actor);
        }

        static Actor? ResolveSelectedActor()
        {
            try
            {
                if (!s_selectedActorProbed)
                {
                    s_selectedActorProbed = true;
                    // Static-only: instance singletons would need their live instance resolved,
                    // which is brittle across WorldBox builds. WorldBox exposes the inspected
                    // unit statically; probe a few known candidate names and cache the first
                    // static Actor-typed member that resolves.
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    Type? selectorType =
                        Type.GetType("WorldTip, Assembly-CSharp")
                        ?? Type.GetType("WindowUnitInspector, Assembly-CSharp")
                        ?? Type.GetType("SelectedUnit, Assembly-CSharp");
                    if (selectorType != null)
                    {
                        foreach (string name in new[] { "selectedUnit", "selected_unit", "unit", "_unit", "actor", "inspected_unit" })
                        {
                            FieldInfo? f = selectorType.GetField(name, flags);
                            if (f != null && typeof(Actor).IsAssignableFrom(f.FieldType))
                            {
                                s_selectedActorField = f;
                                break;
                            }

                            PropertyInfo? p = selectorType.GetProperty(name, flags);
                            if (p != null && p.CanRead && typeof(Actor).IsAssignableFrom(p.PropertyType))
                            {
                                s_selectedActorProp = p;
                                break;
                            }
                        }
                    }
                }

                if (s_selectedActorField != null)
                {
                    return s_selectedActorField.GetValue(null) as Actor;
                }
                if (s_selectedActorProp != null)
                {
                    return s_selectedActorProp.GetValue(null) as Actor;
                }
            }
            catch
            {
                // Selection probe is best-effort; on any reflection failure fall back to
                // "no selection" so the gate simply hides non-selected actors (or, with the
                // gate off, this path is never reached).
            }

            return null;
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
            // WHY: prior fallback font size 9 still rendered too large in the upper
            // world-space HUD view. Halve again to 6 to drive nametag glyph height
            // toward the 4-6 px target in screenshot-based checks.
            text.fontSize = 6;
            text.font = _labelFont;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.text = name;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var rt = text.rectTransform;
            // WHY: companion rect-scaling to match the halved fontSize; move from
            // 3x0.75 to 1.5x0.375 world units to preserve relative spacing
            // while reducing rendered pixel footprint.
            rt.sizeDelta = new Vector2(1.5f, 0.375f);
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
            // WHY: 3D text mesh size mirrors the same reduction pattern as canvas
            // fallback text and was still above target after prior pass.
            SetFloatValue(label, 0.15f, "size");
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
