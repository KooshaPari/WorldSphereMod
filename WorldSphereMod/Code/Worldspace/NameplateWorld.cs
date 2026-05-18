using UnityEngine;
using UnityEngine.UI;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.Worldspace
{
    /// <summary>
    /// Phase 7 Step 2. Per-actor world-space name label attached to a follow-rig.
    /// Uses <see cref="UnityEngine.UI.Text"/> (not TextMeshPro) to avoid adding a
    /// TMP dependency to the assembly reference set. Fades out linearly between
    /// <see cref="kFadeNear"/> and <see cref="kFadeFar"/> based on camera distance
    /// and re-orients to face the camera each <see cref="LateUpdate"/>.
    /// </summary>
    public sealed class NameplateWorld : MonoBehaviour
    {
        internal Actor? Actor;
        Canvas? _canvas;
        UnityEngine.UI.Text? _label;

        public const float kFadeNear = 10f;
        public const float kFadeFar = 30f;

        public static NameplateWorld? Attach(Actor a, Transform rigRoot)
        {
            if (a == null || rigRoot == null) return null;

            // Idempotent: if a nameplate already exists under this rig, reuse it.
            var existing = rigRoot.GetComponentInChildren<NameplateWorld>(true);
            if (existing != null) return existing;

            GameObject go = new GameObject("nameplate");
            Transform t = go.transform;
            t.SetParent(rigRoot, worldPositionStays: false);
            t.localPosition = new Vector3(0f, 0.5f, 0f);
            t.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = CameraManager.MainCamera;
            go.AddComponent<CanvasScaler>();

            UnityEngine.UI.Text label = go.AddComponent<UnityEngine.UI.Text>();
            // Built-in Arial is Unity's fallback default font and is always present
            // in the player even when no font asset is bundled.
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 14;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = a.getName() ?? string.Empty;

            // RectTransform sizing: small canvas so the label is legible at the
            // 0.01 world-unit scale set above.
            var rt = label.rectTransform;
            rt.sizeDelta = new Vector2(200f, 50f);
            rt.anchoredPosition = Vector2.zero;

            NameplateWorld np = go.AddComponent<NameplateWorld>();
            np._canvas = canvas;
            np._label = label;
            np.Actor = a;
            return np;
        }

        public static void Detach(Actor a)
        {
            if (a == null) return;
            var renderer = WorldUIRenderer.Instance;
            if (renderer == null) return;
            if (!renderer.Rigs.TryGetValue(a, out Transform rig) || rig == null) return;
            var np = rig.GetComponentInChildren<NameplateWorld>(true);
            if (np != null) Object.Destroy(np.gameObject);
        }

        public void Refresh(Vector3 worldPos, float camDistance)
        {
            transform.position = worldPos;
            ApplyFade(camDistance);
        }

        void Awake()
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_label == null) _label = GetComponent<UnityEngine.UI.Text>();
        }

        void LateUpdate()
        {
            if (Actor == null || _canvas == null) return;
            var cam = CameraManager.MainCamera;
            if (cam == null) return;

            float d = Vector3.Distance(cam.transform.position, transform.position);

            // Face camera (LookAt aims +Z at the target; flip 180° so the readable
            // front of the text faces the camera).
            transform.LookAt(cam.transform);
            transform.Rotate(0f, 180f, 0f, Space.Self);

            ApplyFade(d);
        }

        void ApplyFade(float camDistance)
        {
            if (_label == null) return;
            float alpha = 1f - Mathf.InverseLerp(kFadeNear, kFadeFar, camDistance);
            var c = _label.color;
            c.a = alpha;
            _label.color = c;
        }
    }
}
