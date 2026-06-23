using UnityEngine;
using UnityEngine.UI;

namespace WorldSphereMod.Worldspace
{
    /// <summary>
    /// Lightweight worldspace-canvas overlay that prints a single compact runtime
    /// stats line to the top-left when <c>Core.savedSettings.DebugHUDVisible</c>
    /// is true.
    /// Useful to eyeball whether a tier (voxel / procgen / foliage / impostor)
    /// is doing the work the LOD scale claims it is, without grepping the per-second
    /// <see cref="Perf.FrameProfiler"/> dump.
    ///
    /// On-screen rendering is intentionally decoupled from <c>ProfilerDump</c>:
    /// ProfilerDump controls the per-second Player.log telemetry dump and is often
    /// left stale-true in the settings JSON, which previously caused this overlay
    /// (and only this overlay's compact line) to paint over the game view. The
    /// dedicated <c>DebugHUDVisible</c> flag defaults OFF, so by default NOTHING
    /// draws to screen regardless of ProfilerDump. No verbose log ring is ever
    /// painted here — only the single stats line below.
    ///
    /// Mounted on <see cref="Mod.Object"/> via <see cref="EnsureCreated"/> in
    /// <c>Mod.Init</c>. Per-frame cost when DebugHUDVisible is false: a single bool
    /// branch in <see cref="LateUpdate"/> (label canvas is also disabled).
    ///
    /// Implementation note: we use uGUI (already referenced for the powers tab)
    /// instead of IMGUI so we don't need to add <c>UnityEngine.IMGUIModule</c>
    /// to <c>WorldSphereMod.csproj</c>.
    /// </summary>
    public sealed class RuntimeStatsOverlay : MonoBehaviour
    {
        public static RuntimeStatsOverlay? Instance { get; private set; }

        const float kSmoothing = 0.1f;

        GameObject? _canvasGO;
        Text? _label;
        float _smoothedFrameMs;

        public static void EnsureCreated()
        {
            if (Instance != null) return;
            if (Mod.Object == null) return;
            if (Mod.Object.GetComponent<RuntimeStatsOverlay>() != null) return;
            Mod.Object.AddComponent<RuntimeStatsOverlay>();
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (_canvasGO != null) Object.Destroy(_canvasGO);
            _canvasGO = null;
            _label = null;
            if (Instance == this) Instance = null;
        }

        void EnsureLabel()
        {
            if (_label != null) return;

            _canvasGO = new GameObject("WSM3D.RuntimeStatsOverlay");
            _canvasGO.transform.SetParent(transform, worldPositionStays: false);

            var canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            _canvasGO.AddComponent<CanvasScaler>();

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(_canvasGO.transform, worldPositionStays: false);
            var rt = labelGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(8f, -8f);
            rt.sizeDelta = new Vector2(560f, 80f);

            _label = labelGO.AddComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _label.fontSize = 12;
            _label.color = Color.white;
            _label.alignment = TextAnchor.UpperLeft;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.raycastTarget = false;
        }

        void LateUpdate()
        {
            // On-screen drawing is gated SOLELY on the dedicated DebugHUDVisible
            // flag (default OFF) — NEVER on ProfilerDump, which is frequently
            // stale-true in the settings JSON. By default nothing draws to screen.
            if (Core.savedSettings == null || !Core.savedSettings.DebugHUDVisible)
            {
                if (_canvasGO != null) _canvasGO.SetActive(false);
                return;
            }

            if (_canvasGO != null) _canvasGO.SetActive(true);

            EnsureLabel();
            if (_label == null) return;

            float ms = Time.unscaledDeltaTime * 1000f;
            _smoothedFrameMs = _smoothedFrameMs <= 0f
                ? ms
                : Mathf.Lerp(_smoothedFrameMs, ms, kSmoothing);
            float fps = _smoothedFrameMs > 0f ? 1000f / _smoothedFrameMs : 0f;

            int voxel = SafeCount(() => WorldSphereMod.Voxel.VoxelMeshCache.Count);
            int procgen = SafeCount(() => WorldSphereMod.ProcGen.ProcGenCache.Count);
            // Crossed-quad foliage + impostor billboard caches removed (foliage/fx are
            // voxel; far-LOD culls). Slots held at 0 for overlay-string stability.
            int foliage = 0;
            int impostor = 0;
            long draws = WorldSphereMod.Voxel.MeshInstanceBatcher.FrameDrawCalls;
            long instances = WorldSphereMod.Voxel.MeshInstanceBatcher.FrameInstances;
            long vHits = SafeLong(() => WorldSphereMod.Voxel.VoxelMeshCache.HitCount);
            long vMisses = SafeLong(() => WorldSphereMod.Voxel.VoxelMeshCache.MissCount);
            float vRate = (vHits + vMisses) > 0 ? (float)vHits / (vHits + vMisses) * 100f : 0f;
            float iRate = 0f;
            float instPerDraw = draws > 0 ? (float)instances / draws : 0f;

            // LOD V/P/I distribution intentionally omitted: would require per-actor
            // tier tagging in LodSelector across frames, which we don't currently
            // retain. Re-add once that tracker lands.
            _label.text =
                $"[WSM3D] FPS={fps:F1} DrawCalls={draws} Instances={instances} InstPerFlush={instPerDraw:F1} " +
                $"ImpostorCount={impostor} VoxelMeshes={voxel} ProcGenMeshes={procgen} FoliageCount={foliage} " +
                $"VoxCacheHit={vRate:F1}% ImpCacheHit={iRate:F1}% FrameMs={_smoothedFrameMs:F2}";
        }

        static int SafeCount(System.Func<int> read)
        {
            try { return read(); }
            catch { return 0; }
        }

        static long SafeLong(System.Func<long> read)
        {
            try { return read(); }
            catch { return 0L; }
        }
    }
}
