using System;

namespace WorldSphereMod
{
    /// <summary>
    /// Marks a Harmony patch class as belonging to a specific phase gate in <see cref="SavedSettings"/>.
    /// Used by <see cref="Core.Patch()"/> to conditionally apply patches only when their phase flag
    /// is enabled, avoiding IL detour overhead for disabled phases.
    ///
    /// Example usage:
    /// <code>
    /// [Phase(nameof(SavedSettings.VoxelEntities))]
    /// [HarmonyPatch(...)]
    /// public static class SomeVoxelPatch { ... }
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PhaseAttribute : Attribute
    {
        /// <summary>
        /// The name of a boolean field in <see cref="SavedSettings"/> that gates this patch.
        /// </summary>
        public string SettingsFlagName { get; }

        public PhaseAttribute(string settingsFlagName)
        {
            SettingsFlagName = settingsFlagName;
        }
    }
}
