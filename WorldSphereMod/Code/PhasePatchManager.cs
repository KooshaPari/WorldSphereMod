using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WorldSphereMod
{
    internal static class PhasePatchManager
    {
        private static readonly HashSet<Type> PatchedTypes = new();

        public static void ApplyPhaseToggle(string flagName, bool newValue)
        {
            if (Core.Patcher == null)
            {
                Debug.LogWarning($"[WSM3D] PhasePatchManager: missing harmony instance for {flagName}.");
                return;
            }

            var phaseTypes = new List<Type>();

            foreach (var type in GetPhaseTypes(flagName))
            {
                phaseTypes.Add(type);
            }

            if (newValue)
            {
                foreach (var type in phaseTypes)
                {
                    if (PatchedTypes.Add(type))
                    {
                        Core.Patcher.CreateClassProcessor(type).Patch();
                    }
                }
            }
            else
            {
                foreach (var type in phaseTypes)
                {
                    PatchedTypes.Remove(type);
                }

                Core.Patcher.UnpatchSelf();

                foreach (var type in PatchedTypes)
                {
                    Core.Patcher.CreateClassProcessor(type).Patch();
                }
            }

            Debug.Log($"[WSM3D] PhasePatchManager: {flagName} -> {newValue}, currently-patched count = {PatchedTypes.Count}");
        }

        public static void MarkTypePatched(Type type)
        {
            PatchedTypes.Add(type);
        }

        private static IEnumerable<Type> GetPhaseTypes(string flagName)
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var phaseAttr = type.GetCustomAttribute<PhaseAttribute>();
                if (phaseAttr == null) continue;
                if (!string.Equals(phaseAttr.SettingsFlagName, flagName, StringComparison.Ordinal)) continue;
                if (type.GetCustomAttribute<HarmonyPatch>() == null) continue;

                yield return type;
            }
        }
    }
}
