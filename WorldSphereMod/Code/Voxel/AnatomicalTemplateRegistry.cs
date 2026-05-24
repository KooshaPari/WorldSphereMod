using WorldSphereMod.Rig;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Canonical anatomical volumes keyed by <see cref="RigType"/>.
    /// Returns false until rig-specific coordinate sets are authored (spec §1, §4).
    /// </summary>
    public static class AnatomicalTemplateRegistry
    {
        public static AnatomicalProjectionMode GetProjectionMode(RigType rigType)
        {
            switch (rigType)
            {
                case RigType.Humanoid:
                    return AnatomicalProjectionMode.FrontFacing;
                case RigType.Quadruped:
                case RigType.Bird:
                case RigType.Snake:
                    return AnatomicalProjectionMode.Side;
                case RigType.Insect:
                    return AnatomicalProjectionMode.TopSideHybrid;
                default:
                    return AnatomicalProjectionMode.FrontFacing;
            }
        }

        /// <summary>
        /// Limb voxels use axis-aligned sampling even when the rig's body uses front/side projection.
        /// </summary>
        public static AnatomicalProjectionMode GetLimbProjectionMode(RigType rigType)
        {
            if (rigType == RigType.Humanoid || rigType == RigType.Quadruped || rigType == RigType.Insect)
            {
                return AnatomicalProjectionMode.LimbAxis;
            }

            return GetProjectionMode(rigType);
        }

        public static bool TryGetTemplate(RigType rigType, out AnatomicalTemplate template)
        {
            template = default;
            if (rigType == RigType.None || rigType == RigType.Static)
            {
                return false;
            }

            // Canonical coordinate volumes are not authored yet; callers must extrude-only fallback.
            return false;
        }
    }
}
