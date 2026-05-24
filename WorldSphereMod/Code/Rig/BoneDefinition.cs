using UnityEngine;

namespace WorldSphereMod.Rig
{
    public enum BoneId : byte
    {
        Root = 0, Hips, Spine, Head,
        LArmUpper, LArmLower, RArmUpper, RArmLower,
        LLegUpper, LLegLower, RLegUpper, RLegLower,
        Neck = 12, LFrontUpper, LFrontLower, RFrontUpper, RFrontLower,
        LRearUpper, LRearLower, RRearUpper, RRearLower,
    }

    public enum RigType
    {
        None,
        Humanoid,
        Quadruped,
        Bird,
        Snake,
        Insect,
        Static,
    }

    public readonly struct BoneDefinition
    {
        public readonly int ParentIndex;
        public readonly Vector3 BindPoseOffset;
        public readonly RectInt PixelRegion;

        public BoneDefinition(int parent, Vector3 bind, RectInt region)
        {
            ParentIndex = parent;
            BindPoseOffset = bind;
            PixelRegion = region;
        }
    }

    public struct SkinnedVoxelMesh
    {
        public Mesh BaseMesh;
        public byte[] BoneIndices;
        public RigType RigType;
    }
}
