using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.NewCamera;

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

        // Cached squared-distance LOD thresholds; recomputed only when any of the inputs
        // (camera FOV, LODScale, VoxelThreshold, ProxyThreshold) change. Saves an Mathf.Tan,
        // two divides and two muls per actor per frame; per-actor cost collapses to a
        // squared-distance compare.
        static float _cachedFov = float.NaN;
        static float _cachedLodScale = float.NaN;
        static float _cachedVoxelThreshold = float.NaN;
        static float _cachedProxyThreshold = float.NaN;
        static float _voxelMaxDistSqr;
        static float _proxyMaxDistSqr;
        // Entity height is the assumed world-units height used to compute the LOD
        // screen-projected size threshold. Phase 1 ships with VoxelScaleMultiplier=8
        // (see project_wsm3d_phase1_visible — meshes are 8x oversize so they're visible
        // at vanilla strategy-view altitude). Pre-multiplying entityHeight here keeps
        // the LOD math in sync with the actual rendered size without forcing the user
        // to set LODScale=8 manually.
        const float _entityHeight = 0.5f * 8.0f;

        public static LodTier Select(Vector3 worldPos, int instanceId)
        {
            if (ImpostorOnlyMode) return LodTier.Impostor;

            Camera cam = CameraManager.MainCamera;
            if (cam == null) return LodTier.Voxel;

            float fov = cam.fieldOfView;
            float lodScale = Core.savedSettings.LODScale;
            if (fov != _cachedFov || lodScale != _cachedLodScale
                || VoxelThreshold != _cachedVoxelThreshold || ProxyThreshold != _cachedProxyThreshold)
            {
                float tanHalfFov = Mathf.Max(0.0001f, Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
                float voxelMaxDist = _entityHeight * lodScale / (VoxelThreshold * tanHalfFov);
                float proxyMaxDist = _entityHeight * lodScale / (ProxyThreshold * tanHalfFov);
                _voxelMaxDistSqr = voxelMaxDist * voxelMaxDist;
                _proxyMaxDistSqr = proxyMaxDist * proxyMaxDist;
                _cachedFov = fov;
                _cachedLodScale = lodScale;
                _cachedVoxelThreshold = VoxelThreshold;
                _cachedProxyThreshold = ProxyThreshold;
            }

            Vector3 camPos = cam.transform.position;
            float dx = worldPos.x - camPos.x;
            float dy = worldPos.y - camPos.y;
            float dz = worldPos.z - camPos.z;
            float distSqr = dx * dx + dy * dy + dz * dz;

            LodTier proposed;
            if (distSqr < _voxelMaxDistSqr) proposed = LodTier.Voxel;
            else if (distSqr < _proxyMaxDistSqr) proposed = LodTier.Proxy;
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

        public static void Remove(int instanceId) { _hyst.Remove(instanceId); }
    }
}
