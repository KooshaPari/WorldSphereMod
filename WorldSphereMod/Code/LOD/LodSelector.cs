using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.LOD
{
    public enum LodTier { Voxel, Proxy, Impostor }

    public static class LodSelector
    {
        public static bool ImpostorOnlyMode;
        public static float VoxelThreshold = 0.08f;
        public static float ProxyThreshold = 0.025f;

        struct LodHysteresis
        {
            public LodTier current;
            public LodTier pending;
            public int pendingFrames;
        }

        static readonly Dictionary<int, LodHysteresis> _hyst = new Dictionary<int, LodHysteresis>();

        public static LodTier Select(Vector3 worldPos, int instanceId)
        {
            if (ImpostorOnlyMode) return LodTier.Impostor;

            Camera cam = CameraManager.MainCamera;
            if (cam == null) return LodTier.Voxel;

            float distance = Vector3.Distance(worldPos, cam.transform.position);
            float tanHalfFov = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float entityHeight = 0.5f;
            float screenFrac = entityHeight / Mathf.Max(0.01f, distance * tanHalfFov) * Core.savedSettings.LODScale;

            LodTier proposed;
            if (screenFrac > VoxelThreshold) proposed = LodTier.Voxel;
            else if (screenFrac > ProxyThreshold) proposed = LodTier.Proxy;
            else proposed = LodTier.Impostor;

            if (!_hyst.TryGetValue(instanceId, out LodHysteresis h))
            {
                h = new LodHysteresis { current = proposed, pending = proposed, pendingFrames = 0 };
                _hyst[instanceId] = h;
                return h.current;
            }

            if (h.current == proposed)
            {
                h.pending = proposed;
                h.pendingFrames = 0;
                _hyst[instanceId] = h;
                return h.current;
            }

            if (h.pending == proposed)
            {
                h.pendingFrames++;
                if (h.pendingFrames >= 3)
                {
                    h.current = proposed;
                    h.pendingFrames = 0;
                }
            }
            else
            {
                h.pending = proposed;
                h.pendingFrames = 1;
            }

            _hyst[instanceId] = h;
            return h.current;
        }

        public static void ResetHysteresis()
        {
            _hyst.Clear();
        }
    }
}
