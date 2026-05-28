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

        /// <summary>
        /// Apply or remove Harmony patches for the given phase flag.
        /// Returns normally even when 0 Harmony-patchable types exist for the flag
        /// (MonoBehaviour drivers like SunDriver, TimeOfDay, PostFxController are
        /// handled by Core.ApplyPhaseToggle's specific handlers, not here).
        /// </summary>
        public static void ApplyPhaseToggle(string flagName, bool newValue)
        {
            if (Core.Patcher == null)
            {
                Debug.LogWarning($"[WSM3D] PhasePatchManager: missing harmony instance for {flagName}.");
                return;
            }

            List<Type> phaseTypes;
            try
            {
                phaseTypes = GetPhaseTypes(flagName).ToList();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WSM3D] PhasePatchManager: GetPhaseTypes failed for {flagName}: {ex.Message}");
                return;
            }

            int harmonyCount = phaseTypes.Count(t => t.GetCustomAttribute<HarmonyPatch>() != null);
            int affected = 0;
            int alreadyPatched = 0;

            if (newValue)
            {
                foreach (var type in phaseTypes)
                {
                    if (type.GetCustomAttribute<HarmonyPatch>() == null)
                    {
                        Debug.Log($"[WSM3D] PhasePatchManager: skipping patch for non-HarmonyPatch type {type.FullName}");
                        continue;
                    }

                    if (PatchedTypes.Add(type))
                    {
                        try
                        {
                            Core.Patcher.CreateClassProcessor(type).Patch();
                            affected++;
                        }
                        catch (Exception ex)
                        {
                            PatchedTypes.Remove(type);
                            Debug.LogWarning($"[WSM3D] PhasePatchManager: Patch() failed for {type.FullName}: {ex.Message}");
                        }
                    }
                    else
                    {
                        alreadyPatched++;
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

            string alreadyNote = alreadyPatched > 0 ? $", {alreadyPatched} already active from init" : "";
            Debug.Log($"[WSM3D] PhasePatchManager: {flagName} -> {newValue} ({affected}/{harmonyCount} Harmony types affected, {phaseTypes.Count - harmonyCount} non-Harmony types skipped{alreadyNote})");
        }

        private static void UnpatchClass(Type type)
        {
            if (type.GetCustomAttribute<HarmonyPatch>() == null)
            {
                Debug.Log($"[WSM3D] PhasePatchManager: skipping unpatch for non-HarmonyPatch type {type.FullName}");
                return;
            }

            PatchClassProcessor processor;
            try
            {
                processor = Core.Patcher.CreateClassProcessor(type);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WSM3D] PhasePatchManager: CreateClassProcessor failed for {type.FullName}: {ex.Message}");
                return;
            }

            if (processor == null)
            {
                Debug.LogWarning($"[WSM3D] PhasePatchManager: CreateClassProcessor returned null for {type.FullName}");
                return;
            }

            var getBulkMethods = processor
                .GetType()
                .GetMethod("GetBulkMethods", BindingFlags.Instance | BindingFlags.NonPublic);

            if (getBulkMethods == null) return;

            IEnumerable patchMethods;
            try
            {
                patchMethods = getBulkMethods.Invoke(processor, new object[] { }) as IEnumerable;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WSM3D] PhasePatchManager: GetBulkMethods.Invoke failed for {type.FullName}: {ex.Message}");
                return;
            }

            if (patchMethods == null) return;

            foreach (var patchMethod in patchMethods)
            {
                if (patchMethod is not MethodBase method)
                {
                    continue;
                }

                try
                {
                    Core.Patcher.Unpatch(method, HarmonyPatchType.All, Core.Patcher.Id);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WSM3D] PhasePatchManager: Unpatch failed for {method.Name} on {type.FullName}: {ex.Message}");
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
                // Was: skip non-HarmonyPatch types. Now: include any type with [Phase]
                // so MonoBehaviour drivers (SunDriver, RigDriver, TimeOfDay,
                // PostFxController) get inventoried for /phase/<name> visibility.
                // ApplyPatch handles each kind appropriately.
                yield return type;
            }
        }
    }
}
