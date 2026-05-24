using WorldSphereMod.Voxel;

namespace WorldSphereMod.Rig
{
    /// <summary>
    /// Vehicle actors (cars, tanks, boats, wagons) voxelize through
    /// <see cref="ShapeHint.Mirror"/> → balloon inflation in
    /// <see cref="AssetShapeRegistry"/> until <c>VehicleRig</c> animation lands.
    /// See <c>docs/journeys/scratch/vehicle-rigging-spec.md</c>.
    /// </summary>
    /// <remarks>
    /// Deferred per vehicle-rigging-spec §2–4: wheel spin, terrain body alignment,
    /// seat attachment, and tread/propeller animation require
    /// <c>VehicleRig</c> + <c>RegisterVehicleRig</c> integration in
    /// <c>VoxelRender.ActorVoxelEmit</c>. Until then, vehicles stay on the actor
    /// path with <see cref="Constants.ResolveActorRig"/> returning
    /// <see cref="RigType.None"/> (vehicle prefix guard) and mirror-shaped voxel meshes from
    /// <see cref="AssetShapeRegistry.GetShapeHint"/>.
    /// </remarks>
    public static class VehicleShapeHints
    {
        /// <summary>
        /// Asset-id prefixes registered as Mirror in <see cref="AssetShapeRegistry"/>.
        /// Keep in sync with <c>AssetShapeRegistry._prefixHints</c> vehicle rows.
        /// </summary>
        public static readonly string[] MirrorAssetPrefixes =
        {
            "boat", "ship", "wagon", "cart", "car", "tank", "vehicle",
        };

        /// <summary>
        /// True when <paramref name="assetId"/> should use mirror/balloon voxel inflation.
        /// </summary>
        public static bool IsVehicleAssetId(string assetId)
        {
            if (string.IsNullOrEmpty(assetId))
            {
                return false;
            }

            string lower = assetId.ToLowerInvariant();
            foreach (string prefix in MirrorAssetPrefixes)
            {
                if (lower.StartsWith(prefix) ||
                    lower.Contains("_" + prefix) ||
                    lower.Contains(prefix + "_"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Shape hint for vehicle voxel builds. Non-vehicle ids return <see cref="ShapeHint.Auto"/>.
        /// </summary>
        public static ShapeHint GetVoxelShapeHint(string assetId)
        {
            if (!IsVehicleAssetId(assetId))
            {
                return ShapeHint.Auto;
            }

            return AssetShapeRegistry.GetShapeHint(assetId);
        }

        /// <summary>
        /// Inflation style string for <see cref="VoxelMeshCache"/> dispatch.
        /// </summary>
        public static string ResolveVoxelStyle(string assetId, UnityEngine.Sprite sprite) =>
            AssetShapeRegistry.ResolveStyle(assetId, sprite);
    }
}
