using CompoundSpheres;
using HarmonyLib;
using UnityEngine;

namespace WorldSphereMod.Water
{
    public static class WaterRender
    {
        [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.Begin))]
        public static class BeginPostfix
        {
            [HarmonyPostfix]
            public static void OnSphereBegin()
            {
                if (!Core.IsWorld3D || !Core.savedSettings.MeshWater) return;
                WaterMaskBuffer.RebuildMask();
                // Manager is private; CenterCapsule is its first child, so .parent is Manager.transform.
                Transform? capsule = Core.Sphere.CenterCapsule;
                if (capsule == null || capsule.parent == null) return;
                WaterSurface.Create(capsule.parent);
            }
        }

        [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.Finish))]
        public static class FinishPrefix
        {
            [HarmonyPrefix]
            public static void OnSphereFinish()
            {
                WaterSurface.Destroy();
                WaterMaskBuffer.Clear();
            }
        }

        [HarmonyPatch(typeof(CompoundSphereScripts), nameof(CompoundSphereScripts.SphereTileColor))]
        public static class ColorSuppression
        {
            [HarmonyPostfix]
            public static void OnSphereTileColor(SphereTile SphereTile, ref Color32 __result)
            {
                if (!Core.savedSettings.MeshWater) return;
                if (!WaterMaskBuffer.IsWater(SphereTile.Index())) return;
                __result.a = 0;
            }
        }
    }
}
