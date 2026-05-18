using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.Worldspace
{
    /// <summary>
    /// Phase 7 Step 5. Pooled world-space damage popups: 64 pre-built
    /// <see cref="UnityEngine.UI.Text"/> objects under one shared root, rotated to
    /// face the camera each tick. Overflow recycles the oldest active entry so the
    /// pool never allocates after <see cref="Init"/>.
    ///
    /// Driven from <see cref="WorldUIRenderer.LateUpdate"/> via <see cref="Tick"/>;
    /// teardown via <see cref="Clear"/> on world unload.
    /// </summary>
    public static class DamagePopup
    {
        static Transform? _root;
        static readonly Queue<GameObject> _free = new Queue<GameObject>();
        static readonly List<ActiveEntry> _active = new List<ActiveEntry>();
        public static int PoolSize = 64;
        static Font? _font;

        const float kLifetime = 1.2f;
        const float kRiseSpeed = 0.5f;
        const float kSpawnLift = 0.3f;

        struct ActiveEntry
        {
            public GameObject obj;
            public Text? text;
            public float expiry;
            public Vector3 velocity;
        }

        public static void Init(Transform parent)
        {
            if (_root != null) return;
            var rootGo = new GameObject("WSM3D.DamagePopups");
            _root = rootGo.transform;
            _root.SetParent(parent, worldPositionStays: false);
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            for (int i = 0; i < PoolSize; i++) _free.Enqueue(BuildOne());
        }

        public static void Spawn(Vector3 worldPos, int value, Color tint)
        {
            if (_root == null) return;
            if (_free.Count == 0)
            {
                // Pool exhausted: recycle oldest active entry.
                if (_active.Count == 0) return;
                var oldest = _active[0];
                _active.RemoveAt(0);
                if (oldest.obj != null)
                {
                    oldest.obj.SetActive(false);
                    _free.Enqueue(oldest.obj);
                }
            }
            var go = _free.Dequeue();
            if (go == null) return;
            go.SetActive(true);
            go.transform.position = worldPos + Vector3.up * kSpawnLift;
            Text? t = go.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.text = value.ToString();
                t.color = tint;
            }
            _active.Add(new ActiveEntry
            {
                obj = go,
                text = t,
                expiry = Time.time + kLifetime,
                velocity = Vector3.up * kRiseSpeed,
            });
        }

        public static void Tick()
        {
            if (_root == null) return;
            float now = Time.time;
            Camera? cam = CameraManager.MainCamera;
            if (cam != null)
            {
                foreach (var e in _active)
                {
                    var canvas = e.obj?.GetComponent<Canvas>();
                    if (canvas != null && canvas.worldCamera == null) canvas.worldCamera = cam;
                }
            }
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var e = _active[i];
                if (e.obj == null)
                {
                    _active.RemoveAt(i);
                    continue;
                }
                float tNorm = 1f - (e.expiry - now) / kLifetime;
                e.obj.transform.position += e.velocity * Time.deltaTime;
                if (e.text != null)
                {
                    var c = e.text.color;
                    c.a = Mathf.Clamp01(1f - tNorm);
                    e.text.color = c;
                }
                if (cam != null)
                {
                    e.obj.transform.LookAt(cam.transform);
                    e.obj.transform.Rotate(0f, 180f, 0f, Space.Self);
                }
                _active[i] = e;
                if (now >= e.expiry)
                {
                    e.obj.SetActive(false);
                    _free.Enqueue(e.obj);
                    _active.RemoveAt(i);
                }
            }
        }

        public static void Clear()
        {
            if (_root == null) return;
            Object.Destroy(_root.gameObject);
            _root = null;
            _free.Clear();
            _active.Clear();
        }

        static GameObject BuildOne()
        {
            var go = new GameObject("popup");
            go.SetActive(false);
            if (_root != null) go.transform.SetParent(_root, worldPositionStays: false);
            go.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = CameraManager.MainCamera;

            var textGo = new GameObject("text");
            textGo.transform.SetParent(go.transform, worldPositionStays: false);
            var text = textGo.AddComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 18;
            text.font = _font;
            text.color = Color.white;
            text.text = "0";
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var rt = text.rectTransform;
            rt.sizeDelta = new Vector2(100f, 40f);
            rt.anchoredPosition = Vector2.zero;
            return go;
        }
    }
}
