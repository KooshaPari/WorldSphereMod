using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Parallel BatchRendererGroup-backed batcher path for experimental BRG migration.
    /// It initializes the BRG runtime handle and exposes a feature-gated
    /// compatibility surface. Rendering remains on the legacy path until this
    /// strategy is explicitly completed.
    /// </summary>
    public static class MeshInstanceBatcherBRG
    {
        static BatchRendererGroup? _group;
        static bool _ready;
        static bool _initialized;

        public static bool IsReady => _ready;
        public static bool HasPendingSubmissions => false;

        public static bool TrySubmit(Mesh mesh, Material mat, Matrix4x4 matrix, Color tint)
        {
            if (!IsReady)
            {
                if (!Initialize()) return false;
            }

            if (!_ready) return false;
            if (mesh == null || mat == null) return false;

            // BRG submit path is intentionally not yet wired to culling/render output.
            return false;
        }

        public static bool TryFlush(int layer = 0, ShadowCastingMode shadows = ShadowCastingMode.On, bool receive = true)
        {
            return IsReady && false;
        }

        public static void Reset()
        {
            _group?.Dispose();
            _group = null;
            _ready = false;
            _initialized = false;
        }

        static bool Initialize()
        {
            if (_initialized) return _ready;
            _initialized = true;

            try
            {
                _group = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
                _ready = true;
            }
            catch (Exception ex)
            {
                _ready = false;
                _group = null;
                Debug.LogWarning("[WSM3D][BRG] BatchRendererGroup unavailable or not supported: " + ex.Message);
            }

            return _ready;
        }

        static Unity.Jobs.JobHandle OnPerformCulling(BatchRendererGroup rendererGroup, BatchCullingContext cullingContext, BatchCullingOutput cullingOutput, IntPtr userContext)
        {
            // Placeholder callback: keeps BRG signature in place for future work.
            return default(Unity.Jobs.JobHandle);
        }
    }
}
