using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.LOD
{
    // VOXEL-OR-INVISIBLE (user, 2026-05-30): the render ladder has exactly TWO tiers —
    // Voxel (near: emit a real voxel mesh) and Cull (far: draw NOTHING). The legacy
    // research lineage carried a third Impostor/Proxy billboard tier that fix/ removed;
    // the f1b0ad9e merge re-fused it, producing the left-to-right LOD WAVE where objects
    // oscillated between an impostor billboard and the voxel state every frame
    // (project_wsm3d_lod_threshold_bug). There is NO intermediate billboard tier: far =
    // cull. Hysteresis keeps a near/far flip from happening every frame.
    public enum LodTier { Voxel, Cull }

    public static class LodSelector
    {
        // When the GPU can't run the voxel path at all (no compute/indirect), everything
        // is culled rather than billboarded — voxel-or-invisible holds even on the
        // compatibility path. (No impostor fallback tier exists anymore.)
        public static bool ImpostorOnlyMode;
        public static float VoxelThreshold = 0.08f;

        struct LodHysteresis
        {
            public LodTier current;
            public LodTier pending;
            public int pendingFrames;
        }

        static readonly Dictionary<int, LodHysteresis> _hyst = new Dictionary<int, LodHysteresis>();

        // WHY: a tier change requires crossing the boundary by this fraction (deadband)
        // AND persisting this many frames. Without a distance deadband, actors sitting
        // near the hard threshold flipped Voxel<->Cull every frame as the camera panned,
        // producing the left-to-right LOD WAVE the user observed.
        const float _hystMargin = 0.25f;   // 25% squared-distance deadband around the boundary
        const int _hystFrames = 3;          // proposed tier must persist N frames before promotion

        // Cached squared-distance LOD threshold; recomputed only when any of the inputs
        // (camera FOV, LODScale, VoxelThreshold, VoxelScaleMultiplier) change. Saves an
        // Mathf.Tan, a divide and a mul per actor per frame; per-actor cost collapses to a
        // single squared-distance compare.
        static float _cachedFov = float.NaN;
        static float _cachedLodScale = float.NaN;
        static float _cachedVoxelThreshold = float.NaN;
        static float _cachedVoxelScale = float.NaN;
        static float _voxelMaxDistSqr;
        // Base vanilla actor sprite half-height in world units. Actual rendered
        // height = _baseEntityHeight * VoxelScaleMultiplier. Read VoxelScaleMultiplier
        // at runtime so the LOD math tracks the live setting (otherwise stale JSON or
        // a user-changed multiplier silently culls every actor — see
        // project_wsm3d_lod_threshold_bug).
        const float _baseEntityHeight = 0.5f;

        public static LodTier Select(Vector3 worldPos, int instanceId)
        {
            if (ImpostorOnlyMode) return LodTier.Cull;

            Camera cam = CameraManager.MainCamera;
            if (cam == null) return LodTier.Voxel;

            float fov = cam.fieldOfView;
            float lodScale = Core.savedSettings.LODScale;
            // WHY: LOD distance must track the ACTUAL rendered actor height, which is now
            // VoxelScaleMultiplier * ActorVoxelScaleFactor (actors render reduced). Folding the
            // factor in keeps the tier boundary matched to real on-screen size.
            float voxelScale = Mathf.Max(0.0001f, Core.savedSettings.VoxelScaleMultiplier * Core.savedSettings.ActorVoxelScaleFactor);
            if (fov != _cachedFov || lodScale != _cachedLodScale
                || VoxelThreshold != _cachedVoxelThreshold
                || voxelScale != _cachedVoxelScale)
            {
                float entityHeight = _baseEntityHeight * voxelScale;
                float tanHalfFov = Mathf.Max(0.0001f, Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
                float voxelMaxDist = entityHeight * lodScale / (VoxelThreshold * tanHalfFov);
                _voxelMaxDistSqr = voxelMaxDist * voxelMaxDist;
                _cachedFov = fov;
                _cachedLodScale = lodScale;
                _cachedVoxelThreshold = VoxelThreshold;
                _cachedVoxelScale = voxelScale;
            }

            Vector3 camPos = cam.transform.position;
            float dx = worldPos.x - camPos.x;
            float dy = worldPos.y - camPos.y;
            float dz = worldPos.z - camPos.z;
            float distSqr = dx * dx + dy * dy + dz * dz;

            // Raw tier from the bare threshold (no hysteresis).
            LodTier rawTier = distSqr < _voxelMaxDistSqr ? LodTier.Voxel : LodTier.Cull;

            if (!_hyst.TryGetValue(instanceId, out LodHysteresis h))
            {
                h = new LodHysteresis { current = rawTier, pending = rawTier, pendingFrames = 0 };
                _hyst[instanceId] = h;
                return h.current;
            }

            // WHY: apply a deadband around the CURRENT tier's boundary. Only propose a
            // change once distance crosses the boundary by _hystMargin. An object that
            // stays inside the band keeps its tier no matter how the camera pans — this
            // kills the wave.
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

        // Hysteresis deadband around the single Voxel<->Cull boundary. Entering Voxel
        // (near) requires distance to drop well below the boundary; leaving Voxel for Cull
        // (far) requires it to rise well above. A small per-frame distance jitter therefore
        // never flips the tier.
        static LodTier ProposeWithDeadband(float distSqr, LodTier current)
        {
            float voxelEnter = _voxelMaxDistSqr * (1f - _hystMargin); // closer than this to ENTER Voxel
            float voxelExit  = _voxelMaxDistSqr * (1f + _hystMargin); // farther than this to LEAVE Voxel

            switch (current)
            {
                case LodTier.Voxel:
                    return distSqr > voxelExit ? LodTier.Cull : LodTier.Voxel;
                default: // Cull
                    return distSqr < voxelEnter ? LodTier.Voxel : LodTier.Cull;
            }
        }

        public static void ResetHysteresis()
        {
            _hyst.Clear();
        }

        public static void Remove(int instanceId) { _hyst.Remove(instanceId); }
    }
}
