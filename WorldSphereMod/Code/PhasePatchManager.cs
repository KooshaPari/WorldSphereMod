using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
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

            var phaseTypes = GetPhaseTypes(flagName).ToList();
            int affected = 0;

            if (newValue)
            {
                foreach (var type in phaseTypes)
                {
                    if (PatchedTypes.Add(type))
                    {
                        Core.Patcher.CreateClassProcessor(type).Patch();
                        affected++;
                    }
                }
            }
            else
            {
                foreach (var type in phaseTypes)
                {
                if (PatchedTypes.Remove(type))
                {
                    UnpatchClass(type);
                    affected++;
                }
            }
            }

            Debug.Log($"[WSM3D] PhasePatchManager: {flagName} -> {newValue} ({affected} types affected)");
        }

        private static void UnpatchClass(Type type)
        {
            var processor = Core.Patcher.CreateClassProcessor(type);
            var getBulkMethods = processor
                .GetType()
                .GetMethod("GetBulkMethods", BindingFlags.Instance | BindingFlags.NonPublic);

            if (getBulkMethods == null) return;

            if (getBulkMethods.Invoke(processor, new object[] { }) is IEnumerable patchMethods)
            {
                foreach (var patchMethod in patchMethods)
                {
                    if (patchMethod is not MethodBase method)
                    {
                        continue;
                    }

                    Core.Patcher.Unpatch(method, HarmonyPatchType.All, Core.Patcher.Id);
                }
            }
        }

        public static void MarkTypePatched(Type type)
        {
            PatchedTypes.Add(type);
        }

        private static IEnumerable<Type> GetPhaseTypes(string flagName)
        {
            var phaseAssembly = typeof(PhaseAttribute).Assembly;
            var types = phaseAssembly.GetTypes();
            Debug.Log($"[WSM3D] PhasePatchManager: scanning assembly {phaseAssembly.FullName} for {flagName}.");
            Debug.Log($"[WSM3D] PhasePatchManager: {flagName} phase candidates: {string.Join(", ", types.Where(type => type.GetCustomAttribute<PhaseAttribute>()?.SettingsFlagName == flagName).Select(type => type.FullName))}");
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
