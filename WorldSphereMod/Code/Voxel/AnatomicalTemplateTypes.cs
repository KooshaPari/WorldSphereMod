using UnityEngine;
using WorldSphereMod.Rig;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Per-voxel body region for anatomical templates
    /// (docs/journeys/scratch/anatomical-template-spec.md §1).
    /// </summary>
    public enum AnatomicalRegion
    {
        Head,
        Core,
        Limb,
        Wing,
        Tail,
    }

    /// <summary>
    /// Shell vs interior tag for later surface extraction (spec §1).
    /// </summary>
    public enum AnatomicalOccupancy
    {
        Surface,
        Interior,
    }

    /// <summary>
    /// Sprite sampling basis per rig family (spec §3).
    /// </summary>
    public enum AnatomicalProjectionMode
    {
        FrontFacing,
        Side,
        TopSideHybrid,
        LimbAxis,
    }

    /// <summary>
    /// One occupied cell in a rig's canonical local actor box.
    /// </summary>
    public readonly struct AnatomicalVoxel
    {
        public readonly Vector3Int Local;
        public readonly AnatomicalRegion Region;
        public readonly AnatomicalOccupancy Occupancy;

        public AnatomicalVoxel(Vector3Int local, AnatomicalRegion region, AnatomicalOccupancy occupancy)
        {
            Local = local;
            Region = region;
            Occupancy = occupancy;
        }
    }

    /// <summary>
    /// Sparse rig-typed volume before sprite color projection (spec §1).
    /// </summary>
    public readonly struct AnatomicalTemplate
    {
        public readonly RigType RigType;
        public readonly AnatomicalVoxel[] Voxels;

        public AnatomicalTemplate(RigType rigType, AnatomicalVoxel[] voxels)
        {
            RigType = rigType;
            Voxels = voxels ?? System.Array.Empty<AnatomicalVoxel>();
        }
    }
}
