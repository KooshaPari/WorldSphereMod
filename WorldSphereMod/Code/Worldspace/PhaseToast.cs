using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WorldSphereMod.Worldspace
{
    /// <summary>
    /// Screen-space toast overlay for phase toggle feedback. Shows a brief
    /// message at the bottom-center of the screen that fades out after a few
    /// seconds. Non-blocking — purely informational.
    ///
    /// Mounted on <see cref="Mod.Object"/> via <see cref="EnsureCreated"/>.
    /// Per-frame cost when no toasts are active: a single count check in
    /// <see cref="Update"/>.
    /// </summary>
    public sealed class PhaseToast : MonoBehaviour
    {
        public static PhaseToast? Instance { get; private set; }

        const float kDefaultDuration = 4f;
        const float kFadeStart = 0.7f; // fraction of duration before fade begins
        const int kMaxVisible = 4;
        const int kFontSize = 14;
        const float kSlotHeight = 28f;
        const float kBottomOffset = 120f;

        GameObject? _canvasGO;
        readonly List<ToastEntry> _active = new();
        readonly Queue<ToastSlot> _pool = new();

        struct ToastEntry
        {
            public ToastSlot Slot;
            public float Expiry;
            public float Duration;
        }

        struct ToastSlot
        {
            public GameObject GO;
            public Text Label;
            public Image Background;
        }

        public static void EnsureCreated()
        {
            if (Instance != null) return;
            if (Mod.Object == null) return;
            if (Mod.Object.GetComponent<PhaseToast>() != null) return;
            Mod.Object.AddComponent<PhaseToast>();
        }

        /// <summary>
        /// Show a success toast (green tint).
        /// </summary>
        public static void ShowSuccess(string message, float duration = kDefaultDuration)
        {
            Show(message, new Color(0.2f, 0.8f, 0.3f, 0.9f), duration);
        }

        /// <summary>
        /// Show an error toast (red tint).
        /// </summary>
        public static void ShowError(string message, float duration = kDefaultDuration + 1f)
        {
            Show(message, new Color(0.9f, 0.25f, 0.2f, 0.9f), duration);
        }

        /// <summary>
        /// Show a warning toast (yellow tint).
        /// </summary>
        public static void ShowWarning(string message, float duration = kDefaultDuration)
        {
            Show(message, new Color(0.9f, 0.75f, 0.1f, 0.9f), duration);
        }

        public static void Show(string message, Color bgColor, float duration = kDefaultDuration)
        {
            if (Instance == null) EnsureCreated();
            if (Instance == null) return;
            Instance.EnqueueToast(message, bgColor, duration);
        }

        void Awake()
        {
            Instance = this;
            BuildCanvas();
        }

        void OnDestroy()
        {
            if (_canvasGO != null) Object.Destroy(_canvasGO);
            _canvasGO = null;
            _active.Clear();
            _pool.Clear();
            if (Instance == this) Instance = null;
        }

        void BuildCanvas()
        {
            _canvasGO = new GameObject("WSM3D.PhaseToast");
            _canvasGO.transform.SetParent(transform, worldPositionStays: false);

            var canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32761; // above RuntimeStatsOverlay

            var scaler = _canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            _canvasGO.AddComponent<GraphicRaycaster>();
        }

        void EnqueueToast(string message, Color bgColor, float duration)
        {
            // Evict oldest if at capacity
            while (_active.Count >= kMaxVisible)
            {
                var oldest = _active[0];
                ReturnSlot(oldest.Slot);
                _active.RemoveAt(0);
            }

            var slot = GetSlot();
            slot.Label.text = message;
            slot.Background.color = bgColor;
            slot.GO.SetActive(true);

            _active.Add(new ToastEntry
            {
                Slot = slot,
                Expiry = Time.unscaledTime + duration,
                Duration = duration,
            });

            RepositionAll();
        }

        void Update()
        {
            if (_active.Count == 0) return;

            float now = Time.unscaledTime;
            bool repositionNeeded = false;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var e = _active[i];
                float remaining = e.Expiry - now;

                if (remaining <= 0f)
                {
                    ReturnSlot(e.Slot);
                    _active.RemoveAt(i);
                    repositionNeeded = true;
                    continue;
                }

                // Fade out during the last portion
                float fadeWindow = e.Duration * (1f - kFadeStart);
                if (fadeWindow > 0f && remaining < fadeWindow)
                {
                    float alpha = remaining / fadeWindow;
                    var bg = e.Slot.Background;
                    var c = bg.color;
                    c.a = Mathf.Lerp(0f, 0.9f, alpha);
                    bg.color = c;

                    var lc = e.Slot.Label.color;
                    lc.a = alpha;
                    e.Slot.Label.color = lc;
                }
            }

            if (repositionNeeded) RepositionAll();
        }

        void RepositionAll()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                var rt = _active[i].Slot.GO.GetComponent<RectTransform>();
                if (rt == null) continue;
                // Stack upward from bottom-center
                float y = kBottomOffset + (i * (kSlotHeight + 4f));
                rt.anchoredPosition = new Vector2(0f, y);
            }
        }

        ToastSlot GetSlot()
        {
            if (_pool.Count > 0)
            {
                var s = _pool.Dequeue();
                s.Label.color = Color.white;
                return s;
            }
            return BuildSlot();
        }

        void ReturnSlot(ToastSlot slot)
        {
            if (slot.GO == null) return;
            slot.GO.SetActive(false);
            _pool.Enqueue(slot);
        }

        ToastSlot BuildSlot()
        {
            var go = new GameObject("toast");
            go.SetActive(false);
            go.transform.SetParent(_canvasGO!.transform, worldPositionStays: false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(500f, kSlotHeight);
            rt.anchoredPosition = new Vector2(0f, kBottomOffset);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.85f);
            bg.raycastTarget = false;

            var textGO = new GameObject("text");
            textGO.transform.SetParent(go.transform, worldPositionStays: false);
            var textRt = textGO.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8f, 2f);
            textRt.offsetMax = new Vector2(-8f, -2f);

            var label = textGO.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = kFontSize;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            return new ToastSlot { GO = go, Label = label, Background = bg };
        }
    }
}
