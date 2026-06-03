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
        // Apparent-size threshold: entity voxelizes when its angular size (height/dist/tanHalfFov)
        // exceeds this fraction. Lower = larger voxel-render radius.
        // 0.08 → voxelMaxDist≈43 for buildings (entityH=4, lodScale=0.5) — too small, buildings
        // at dist=110 (normal zoom) all cull. 0.02 → voxelMaxDist≈173 — covers observed distances
        // with margin; truly far buildings (>173 units) still cull. (#208 lod-impostor fix)
        public static float VoxelThreshold = 0.01f;

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

        // Building path uses a wider deadband and no multi-frame wait to avoid
        // synchronized full-frame Cull/voxel flips.
        const float _buildingHystMargin = 0.45f;
        const int _buildingHystFrames = 1;

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
            return Select(worldPos, instanceId, 0f, _hystMargin, _hystFrames);
        }

        /// <summary>
        /// Select the LOD tier for an entity at <paramref name="worldPos"/>.
        /// <paramref name="entityHeightOverride"/> lets callers supply the actual rendered
        /// world-space height when it differs from the actor default
        /// (_baseEntityHeight * VoxelScaleMultiplier * ActorVoxelScaleFactor). Buildings
        /// do not use ActorVoxelScaleFactor — pass their real mesh height so the distance
        /// threshold is consistent with what the user sees on screen. (#208 lodImpostor fix)
        /// </summary>
        public static LodTier Select(Vector3 worldPos, int instanceId, float entityHeightOverride)
        {
            return Select(worldPos, instanceId, entityHeightOverride, _hystMargin, _hystFrames);
        }

        /// <summary>
        /// Building-specific LOD selection with a wider deadband and no multi-frame
        /// promotion delay to keep procedurally-rendered buildings from flapping
        /// all at once at the far threshold.
        /// </summary>
        public static LodTier SelectForBuilding(Vector3 worldPos, int instanceId, float entityHeightOverride)
        {
            return Select(worldPos, instanceId, entityHeightOverride, _buildingHystMargin, _buildingHystFrames);
        }

        static LodTier Select(
            Vector3 worldPos,
            int instanceId,
            float entityHeightOverride,
            float hysteresisMargin,
            int hysteresisFrames)
        {
            if (ImpostorOnlyMode) return LodTier.Cull;

            Camera cam = CameraManager.MainCamera;
            if (cam == null) return LodTier.Voxel;

            float fov = cam.fieldOfView;
            float lodScale = Core.savedSettings.LODScale;
            // Compute the squared-distance threshold. Use entityHeightOverride when provided
            // (e.g. buildings) so the threshold matches the actual rendered size, not the
            // actor-specific ActorVoxelScaleFactor. Use the shared cache only for the default
            // actor path to avoid inter-entity cache collisions.
            float voxelMaxDistSqr;
            if (entityHeightOverride > 0f)
            {
                // Per-call computation — no shared cache since entity heights vary.
                float tanHalfFov = Mathf.Max(0.0001f, Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
                float d = entityHeightOverride * lodScale / (VoxelThreshold * tanHalfFov);
                voxelMaxDistSqr = d * d;
            }
            else
            {
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
                voxelMaxDistSqr = _voxelMaxDistSqr;
            }

            Vector3 camPos = cam.transform.position;
            float dx = worldPos.x - camPos.x;
            float dy = worldPos.y - camPos.y;
            float dz = worldPos.z - camPos.z;
            float distSqr = dx * dx + dy * dy + dz * dz;

            // Raw tier from the bare threshold (no hysteresis).
            LodTier rawTier = distSqr < voxelMaxDistSqr ? LodTier.Voxel : LodTier.Cull;

            if (!_hyst.TryGetValue(instanceId, out LodHysteresis h))
            {
                h = new LodHysteresis { current = rawTier, pending = rawTier, pendingFrames = 0 };
                _hyst[instanceId] = h;
                return h.current;
            }

            // WHY: apply a deadband around the CURRENT tier's boundary. Only propose a
            // change once distance crosses the boundary by the configured margin. An object that
            // stays inside the band keeps its tier no matter how the camera pans — this
            // kills the wave.
            LodTier proposed = ProposeWithDeadband(distSqr, h.current, voxelMaxDistSqr, hysteresisMargin);

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
                if (h.pendingFrames >= hysteresisFrames)
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
        static LodTier ProposeWithDeadband(float distSqr, LodTier current, float voxelMaxDistSqr, float hysteresisMargin)
        {
            float voxelEnter = voxelMaxDistSqr * (1f - hysteresisMargin); // closer than this to ENTER Voxel
            float voxelExit  = voxelMaxDistSqr * (1f + hysteresisMargin); // farther than this to LEAVE Voxel

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
