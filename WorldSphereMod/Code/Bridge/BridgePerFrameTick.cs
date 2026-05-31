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

            // Passive camera-pan sampling for the input-capture substrate (debounced; no-op when
            // capture disabled). Runs every frame on the main thread, before the 3D-only gate so
            // 2D camera moves are captured too.
            WorldSphereMod.Capture.CaptureCameraSampler.Tick();

            if (!Core.IsWorld3D) return;

            if (runVoxelFrame)
            {
                int frame = Time.frameCount;
                if (frame != _voxelTickFrame)
                {
                    _voxelTickFrame = frame;
                    WorldSphereMod.Voxel.VoxelFrameDriver.TickPerFrame();
                }
            }

        }
    }

    /// <summary>
    /// Harmony Postfix on MapBox.renderStuff — NOTE: this is suppressed by
    /// RefreshSphere.Prefix returning false. BridgeSurvival.Run is called
    /// explicitly from the Prefix instead. This Postfix is kept as a safety net
    /// for any code path where the Prefix doesn't fire.
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
