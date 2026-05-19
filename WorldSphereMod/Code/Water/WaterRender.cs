using CompoundSpheres;
using HarmonyLib;
using UnityEngine;

namespace WorldSphereMod.Water
{
    public static class WaterRender
    {
        static bool _lastMeshWater;

        /// <summary>
        /// Per-frame lifecycle check — runtime toggle of SavedSettings.MeshWater
        /// creates/destroys the WaterSurface without requiring a world reload.
        /// Called from VoxelFrameDriver.LateUpdate.
        /// </summary>
        public static void UpdateLifecycle()
        {
            bool now = Core.IsWorld3D && Core.savedSettings.MeshWater;
            if (now == _lastMeshWater) return;
            _lastMeshWater = now;
            if (now)
            {
                WaterMaskBuffer.RebuildMask();
                Transform? capsule = Core.Sphere.CenterCapsule;
                if (capsule != null && capsule.parent != null)
                    WaterSurface.Create(capsule.parent);
            }
            else
            {
                WaterSurface.Destroy();
                WaterMaskBuffer.Clear();
            }
        }

        [Phase(nameof(SavedSettings.MeshWater))]
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
                _lastMeshWater = true;
            }
        }

        [Phase(nameof(SavedSettings.MeshWater))]
        [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.Finish))]
        public static class FinishPrefix
        {
            [HarmonyPrefix]
            public static void OnSphereFinish()
            {
                WaterSurface.Destroy();
                WaterMaskBuffer.Clear();
                _lastMeshWater = false;
            }
        }

        [Phase(nameof(SavedSettings.MeshWater))]
        [HarmonyPatch(typeof(CompoundSphereScripts), nameof(CompoundSphereScripts.SphereTileColor))]
        public static class ColorSuppression
        {
            [HarmonyPostfix]
            public static void OnSphereTileColor(SphereTile SphereTile, ref Color32 __result)
            {
                if (!Core.savedSettings.MeshWater) return;
                // Don't suppress terrain colour until the mesh exists — otherwise tiles
                // go invisible during the one-frame gap before UpdateLifecycle fires.
                if (WaterSurface.Instance == null) return;
                if (!WaterMaskBuffer.IsWater(SphereTile.Index())) return;
                __result.a = 0;
            }
        }

        // Tile-change invalidation. Full mask rebuild + full mesh rebuild for now.
        // TODO Phase 4 polish: dirty-track per-tile, update only the changed cell.
        [Phase(nameof(SavedSettings.MeshWater))]
        [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.UpdateBaseLayer))]
        public static class UpdateBaseLayerPostfix
        {
            [HarmonyPostfix]
            public static void OnUpdate()
            {
                if (!Core.savedSettings.MeshWater || WaterSurface.Instance == null) return;
                WaterMaskBuffer.RebuildMask();
                WaterSurface.Instance.RebuildMesh();
            }
        }

        [Phase(nameof(SavedSettings.MeshWater))]
        [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.UpdateScale))]
        public static class UpdateScalePostfix
        {
            [HarmonyPostfix]
            public static void OnUpdate()
            {
                if (!Core.savedSettings.MeshWater || WaterSurface.Instance == null) return;
                WaterMaskBuffer.RebuildMask();
                WaterSurface.Instance.RebuildMesh();
            }
        }
    }
}
