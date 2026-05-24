using UnityEngine;
using WorldSphereMod.Rig;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Runtime orchestration for anatomical templates (spec §5–§7).
    /// Not wired into <see cref="VoxelMeshCache"/> yet; extrusion remains the active path.
    /// </summary>
    public static class AnatomicalTemplatePipeline
    {
        public static bool ShouldUseTemplate(RigType rigType)
        {
            if (rigType == RigType.None)
            {
                return false;
            }

            if (!AnatomicalTemplateRegistry.TryGetTemplate(rigType, out AnatomicalTemplate template))
            {
                return false;
            }

            return AnatomicalTemplateValidation.TryValidate(template, out _);
        }

        /// <summary>
        /// Projects sprite colors onto template voxels (spec §2). Stub until templates exist.
        /// </summary>
        public static bool TryBuildColorizedTemplate(
            Sprite sprite,
            RigType rigType,
            out AnatomicalTemplate colorized)
        {
            colorized = default;
            if (!ShouldUseTemplate(rigType))
            {
                return false;
            }

            if (!AnatomicalTemplateRegistry.TryGetTemplate(rigType, out AnatomicalTemplate template))
            {
                return false;
            }

            if (!AnatomicalTemplateValidation.TryValidate(template, out _))
            {
                return false;
            }

            // Sprite projection and merge with extrusion backstop land in a follow-up change.
            colorized = template;
            return false;
        }
    }
}
