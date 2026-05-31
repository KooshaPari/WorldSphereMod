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
        public static float ProxyThreshold = 0.020f;

        struct LodHysteresis
        {
            public LodTier current;
            public LodTier pending;
            public int pendingFrames;
        }

        static readonly Dictionary<int, LodHysteresis> _hyst = new Dictionary<int, LodHysteresis>();

        // WHY: a tier change requires crossing the boundary by this fraction (deadband)
        // AND persisting this many frames. Without a distance deadband, actors sitting
        // near a hard threshold flipped Voxel<->Impostor every frame as the camera panned,
        // producing the left-to-right LOD WAVE the user observed.
        const float _hystMargin = 0.25f;   // 25% squared-distance deadband around each boundary
        const int _hystFrames = 8;          // proposed tier must persist N frames before promotion

        // Cached squared-distance LOD thresholds; recomputed only when any of the inputs
        // (camera FOV, LODScale, VoxelThreshold, ProxyThreshold) change. Saves an Mathf.Tan,
        // two divides and two muls per actor per frame; per-actor cost collapses to a
        // squared-distance compare.
        static float _cachedFov = float.NaN;
        static float _cachedLodScale = float.NaN;
        static float _cachedVoxelThreshold = float.NaN;
        static float _cachedProxyThreshold = float.NaN;
        static float _cachedVoxelScale = float.NaN;
        static float _voxelMaxDistSqr;
        static float _proxyMaxDistSqr;
        // Base vanilla actor sprite half-height in world units. Actual rendered
        // height = _baseEntityHeight * VoxelScaleMultiplier. Read VoxelScaleMultiplier
        // at runtime so the LOD math tracks the live setting (otherwise stale JSON or
        // a user-changed multiplier silently demotes every actor to Impostor — see
        // project_wsm3d_lod_threshold_bug).
        const float _baseEntityHeight = 0.5f;

        public static LodTier Select(Vector3 worldPos, int instanceId)
        {
            if (ImpostorOnlyMode) return LodTier.Impostor;

            Camera cam = CameraManager.MainCamera;
            if (cam == null) return LodTier.Voxel;

            float fov = cam.fieldOfView;
            float lodScale = Core.savedSettings.LODScale;
            // WHY: LOD distance must track the ACTUAL rendered actor height, which is now
            // VoxelScaleMultiplier * ActorVoxelScaleFactor (actors render reduced). Folding the
            // factor in keeps tier boundaries matched to real on-screen size.
            float voxelScale = Mathf.Max(0.0001f, Core.savedSettings.VoxelScaleMultiplier * Core.savedSettings.ActorVoxelScaleFactor);
            if (fov != _cachedFov || lodScale != _cachedLodScale
                || VoxelThreshold != _cachedVoxelThreshold || ProxyThreshold != _cachedProxyThreshold
                || voxelScale != _cachedVoxelScale)
            {
                float entityHeight = _baseEntityHeight * voxelScale;
                float tanHalfFov = Mathf.Max(0.0001f, Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
                float voxelMaxDist = entityHeight * lodScale / (VoxelThreshold * tanHalfFov);
                float proxyMaxDist = entityHeight * lodScale / (ProxyThreshold * tanHalfFov);
                _voxelMaxDistSqr = voxelMaxDist * voxelMaxDist;
                _proxyMaxDistSqr = proxyMaxDist * proxyMaxDist;
                _cachedFov = fov;
                _cachedLodScale = lodScale;
                _cachedVoxelThreshold = VoxelThreshold;
                _cachedProxyThreshold = ProxyThreshold;
                _cachedVoxelScale = voxelScale;
            }

            Vector3 camPos = cam.transform.position;
            float dx = worldPos.x - camPos.x;
            float dy = worldPos.y - camPos.y;
            float dz = worldPos.z - camPos.z;
            float distSqr = dx * dx + dy * dy + dz * dz;

            // Raw tier from the bare thresholds (no hysteresis).
            LodTier rawTier;
            if (distSqr < _voxelMaxDistSqr) rawTier = LodTier.Voxel;
            else if (distSqr < _proxyMaxDistSqr) rawTier = LodTier.Proxy;
            else rawTier = LodTier.Impostor;

            if (!_hyst.TryGetValue(instanceId, out LodHysteresis h))
            {
                h = new LodHysteresis { current = rawTier, pending = rawTier, pendingFrames = 0 };
                _hyst[instanceId] = h;
                return h.current;
            }

            // WHY: apply a deadband around the CURRENT tier's boundaries. Only propose a
            // change once distance crosses the boundary by _hystMargin. An actor that stays
            // inside the band keeps its tier no matter how the camera pans — kills the wave.
            LodTier proposed = ProposeWithDeadband(distSqr, h.current);

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
                if (h.pendingFrames >= _hystFrames)
                {
                    h.current = proposed;
                    h.pendingFrames = 0;
                }
            }
            else { h.pending = proposed; h.pendingFrames = 1; }

            _hyst[instanceId] = h;
            return h.current;
        }

        // Hysteresis deadband: a promotion to a NEARER tier requires distance to drop well
        // below the boundary; a demotion to a FARTHER tier requires it to rise well above.
        // Boundaries widen/narrow by _hystMargin depending on the current tier so a small
        // per-frame distance jitter never flips the tier.
        static LodTier ProposeWithDeadband(float distSqr, LodTier current)
        {
            float voxelEnter = _voxelMaxDistSqr * (1f - _hystMargin); // must be closer than this to ENTER Voxel
            float voxelExit  = _voxelMaxDistSqr * (1f + _hystMargin); // must be farther than this to LEAVE Voxel
            float proxyEnter = _proxyMaxDistSqr * (1f - _hystMargin);
            float proxyExit  = _proxyMaxDistSqr * (1f + _hystMargin);

            switch (current)
            {
                case LodTier.Voxel:
                    if (distSqr > voxelExit)
                        return distSqr > proxyExit ? LodTier.Impostor : LodTier.Proxy;
                    return LodTier.Voxel;
                case LodTier.Proxy:
                    if (distSqr < voxelEnter) return LodTier.Voxel;
                    if (distSqr > proxyExit) return LodTier.Impostor;
                    return LodTier.Proxy;
                default: // Impostor
                    if (distSqr < voxelEnter) return LodTier.Voxel;
                    if (distSqr < proxyEnter) return LodTier.Proxy;
                    return LodTier.Impostor;
            }
        }

        public static void ResetHysteresis()
        {
            _hyst.Clear();
        }

        public static void Remove(int instanceId) { _hyst.Remove(instanceId); }
    }
}
