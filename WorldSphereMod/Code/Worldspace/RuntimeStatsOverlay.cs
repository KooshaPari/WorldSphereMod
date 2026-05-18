using UnityEngine;
using UnityEngine.UI;

namespace WorldSphereMod.Worldspace
{
    /// <summary>
    /// Lightweight worldspace-canvas overlay that prints fork cache + draw-call
    /// counts to the top-left when <c>Core.savedSettings.ProfilerDump</c> is true.
    /// Useful to eyeball whether a tier (voxel / procgen / foliage / impostor) is
    /// doing the work the LOD scale claims it is, without grepping the per-second
    /// <see cref="Perf.FrameProfiler"/> dump.
    ///
    /// Mounted on <see cref="Mod.Object"/> via <see cref="EnsureCreated"/> in
    /// <c>Mod.Init</c>. Per-frame cost when ProfilerDump is false: a single bool
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
            bool on = Core.savedSettings.ProfilerDump;
            if (_canvasGO != null) _canvasGO.SetActive(on);
            if (!on) return;

            EnsureLabel();
            if (_label == null) return;

            float ms = Time.unscaledDeltaTime * 1000f;
            _smoothedFrameMs = _smoothedFrameMs <= 0f
                ? ms
                : Mathf.Lerp(_smoothedFrameMs, ms, kSmoothing);

            int voxel = SafeCount(() => WorldSphereMod.Voxel.VoxelMeshCache.Count);
            int procgen = SafeCount(() => WorldSphereMod.ProcGen.ProcGenCache.Count);
            int foliage = SafeCount(() => WorldSphereMod.Foliage.CrossedQuadMeshCache.Count);
            int impostor = SafeCount(() => WorldSphereMod.LOD.ImpostorBillboard.Count);
            long draws = WorldSphereMod.Voxel.MeshInstanceBatcher.FrameDrawCalls;
            long instances = WorldSphereMod.Voxel.MeshInstanceBatcher.FrameInstances;

            // LOD V/P/I distribution intentionally omitted: would require
            // per-actor tier tagging in LodSelector across frames, which we
            // don't currently retain. Re-add once that tracker lands.
            _label.text =
                $"[WSM3D] Voxel meshes: {voxel} / Procgen: {procgen} / Foliage: {foliage} / Impostor: {impostor}\n" +
                $"[WSM3D] Draw calls last frame: {draws} / Instances: {instances}\n" +
                $"[WSM3D] Frame time: {_smoothedFrameMs:F2} ms";
        }

        static int SafeCount(System.Func<int> read)
        {
            try { return read(); }
            catch { return 0; }
        }
    }
}
