using UnityEngine;

namespace WorldSphereMod.UI
{
    /// <summary>
    /// Lightweight IMGUI HUD in the top-left corner showing live mod state:
    /// FPS, frame ms, draw calls, instance count, visible units, voxel cache
    /// size, current shape, and isWorld3D. Toggle with F8.
    ///
    /// Distinct from <see cref="WorldSphereMod.Worldspace.RuntimeStatsOverlay"/>
    /// which is uGUI-based and gated on ProfilerDump. This one is IMGUI so it
    /// works before any Canvas is alive (e.g. on the main menu / pre-world).
    /// </summary>
    public sealed class DebugHUD : MonoBehaviour
    {
        public static DebugHUD Instance { get; private set; }

        const float kSmoothing = 0.1f;
        float _smoothedFrameMs;
        GUIStyle _style;

        public static void EnsureCreated()
        {
            if (Instance != null) return;
            if (Mod.Object == null) return;
            if (Mod.Object.GetComponent<DebugHUD>() != null) return;
            Mod.Object.AddComponent<DebugHUD>();
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (Core.savedSettings != null)
                {
                    Core.savedSettings.DebugHUDVisible = !Core.savedSettings.DebugHUDVisible;
                }
            }

            float ms = Time.unscaledDeltaTime * 1000f;
            _smoothedFrameMs = _smoothedFrameMs <= 0f
                ? ms
                : Mathf.Lerp(_smoothedFrameMs, ms, kSmoothing);
        }

        void OnGUI()
        {
            if (Core.savedSettings == null || !Core.savedSettings.DebugHUDVisible) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin != null ? GUI.skin.label : new GUIStyle())
                {
                    fontSize = 12,
                    alignment = TextAnchor.UpperLeft,
                    richText = false,
                    wordWrap = false,
                };
                _style.normal.textColor = Color.white;
            }

            float fps = _smoothedFrameMs > 0f ? 1000f / _smoothedFrameMs : 0f;
            long drawCalls = SafeLong(() => WorldSphereMod.Voxel.MeshInstanceBatcher.FrameDrawCalls);
            long instances = SafeLong(() => WorldSphereMod.Voxel.MeshInstanceBatcher.FrameInstances);
            int visibleUnits = SafeInt(() =>
            {
                var w = World.world;
                if (w == null || w.units == null) return 0;
                return w.units.visible_units.count;
            });
            int cacheSize = SafeInt(() => WorldSphereMod.Voxel.VoxelMeshCache.Count);
            int shape = Core.savedSettings != null ? Core.savedSettings.CurrentShape : -1;
            bool isWorld3D = SafeBool(() => Core.IsWorld3D);

            string text =
                $"[WSM3D HUD] (F8 to hide)\n" +
                $"FPS         {fps,7:F1}\n" +
                $"FrameMs     {_smoothedFrameMs,7:F2}\n" +
                $"DrawCalls   {drawCalls,7}\n" +
                $"Instances   {instances,7}\n" +
                $"VisibleUnits{visibleUnits,7}\n" +
                $"VoxelCache  {cacheSize,7}\n" +
                $"Shape       {shape,7}\n" +
                $"IsWorld3D   {isWorld3D,7}";

            // Black backdrop for legibility against bright terrain.
            var rect = new Rect(8f, 8f, 240f, 140f);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, rect.height - 8f), text, _style);
        }

        static long SafeLong(System.Func<long> read)
        {
            try { return read(); } catch { return 0L; }
        }

        static int SafeInt(System.Func<int> read)
        {
            try { return read(); } catch { return 0; }
        }

        static bool SafeBool(System.Func<bool> read)
        {
            try { return read(); } catch { return false; }
        }
    }
}
