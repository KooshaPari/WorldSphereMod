using HarmonyLib;
using UnityEngine;

namespace WorldSphereMod.Bridge
{
    /// <summary>
    /// Shared per-frame bridge/voxel survival hooks. Primary: MapBox.renderStuff Postfix
    /// (always reached when RefreshSphere Prefix runs). Backup: ActorManager
    /// precalculateRenderDataParallel Postfix (fires whenever actor render prep runs).
    /// See docs/journeys/scratch/bridge-scene-transition-known-issue.md.
    /// </summary>
    public static class BridgeSurvival
    {
        static int _voxelTickFrame = -1;

        /// <param name="runVoxelFrame">When true, run <see cref="Voxel.VoxelFrameDriver.TickPerFrame"/> at most once per Unity frame.</param>
        public static void Run(bool runVoxelFrame)
        {
            BridgeServer.CaptureMainThread();
            BridgeServer.EnsureCreated();
            BridgeServer.DrainStaticQueue();
            WorldSphereMod.Voxel.MeshInstanceBatcher.SetMainThread();

            if (!runVoxelFrame || !Core.IsWorld3D) return;

            int frame = Time.frameCount;
            if (frame == _voxelTickFrame) return;
            _voxelTickFrame = frame;
            WorldSphereMod.Voxel.VoxelFrameDriver.TickPerFrame();
        }
    }

    /// <summary>
    /// Harmony Postfix on MapBox.renderStuff — always reached via TileMapToSphere.RefreshSphere
    /// Prefix (returns false) every frame, including after save/load scene transitions.
    /// </summary>
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.renderStuff))]
    public static class BridgePerFrameTick
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { BridgeSurvival.Run(runVoxelFrame: true); }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WSM3D][Bridge] per-frame tick failed: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Backup bridge drain when MapBox.renderStuff Postfix is skipped. Does not run the voxel
    /// frame driver — emit postfixes on this method must finish before LateUpdate flush.
    /// </summary>
    [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.precalculateRenderDataParallel))]
    public static class BridgeSurvivalBackup
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { BridgeSurvival.Run(runVoxelFrame: false); }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WSM3D][Bridge] survival backup failed: " + ex.Message);
            }
        }
    }
}
