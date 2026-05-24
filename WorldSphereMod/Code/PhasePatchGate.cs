using System;
using System.Reflection;

namespace WorldSphereMod
{
    /// <summary>
    /// Central predicate for conditional Harmony patch dispatch at init.
    /// See <c>docs/adr/ADR-0007-conditional-patch-dispatch.md</c>.
    /// </summary>
    internal static class PhasePatchGate
    {
        /// <summary>
        /// Returns whether a type should receive Harmony patches during <see cref="Core.Patch"/>.
        /// Types without <see cref="PhaseAttribute"/> are always eligible; phased types require
        /// their <see cref="SavedSettings"/> flag to be enabled.
        /// </summary>
        public static bool ShouldApplyHarmonyPatch(Type type, SavedSettings settings)
        {
            var phaseAttr = type.GetCustomAttribute<PhaseAttribute>();
            if (phaseAttr == null)
            {
                return true;
            }

            return IsSettingsFlagEnabled(settings, phaseAttr.SettingsFlagName);
        }

        /// <summary>
        /// Reads a boolean field from <see cref="SavedSettings"/> by name.
        /// </summary>
        public static bool IsSettingsFlagEnabled(SavedSettings settings, string flagName)
        {
            var flagField = typeof(SavedSettings).GetField(flagName);
            if (flagField == null || flagField.FieldType != typeof(bool))
            {
                return false;
            }

            return (bool)flagField.GetValue(settings);
        }
    }
}
