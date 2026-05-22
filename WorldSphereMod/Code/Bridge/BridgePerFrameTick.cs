using HarmonyLib;
using UnityEngine;

namespace WorldSphereMod.Bridge
{
    /// <summary>
    /// Harmony Postfix on MapBox.renderStuff (vanilla per-frame method WorldBox calls
    /// every frame after the scene transition). Drains BridgeServer's static main-thread
    /// queue + emits telemetry — bypasses our MonoBehaviour entirely, which dies on
    /// scene transition per docs/journeys/scratch/bridge-scene-transition-known-issue.md.
    /// </summary>
    [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.precalculateRenderDataParallel))]
    public static class BridgePerFrameTick
    {
        static float _lastTelemetryLog;

        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                BridgeServer.DrainStaticQueue();
            }
            catch { }

            float now = Time.realtimeSinceStartup;
            if (now - _lastTelemetryLog > 10f)
            {
                _lastTelemetryLog = now;
                try
                {
                    Debug.Log($"[WSM3D][Telemetry] frameMs={Time.unscaledDeltaTime * 1000:F2}" +
                              $" drawCalls={WorldSphereMod.Voxel.MeshInstanceBatcher.FrameDrawCalls}" +
                              $" instances={WorldSphereMod.Voxel.MeshInstanceBatcher.FrameInstances}" +
                              $" cacheSize={WorldSphereMod.Voxel.VoxelMeshCache.Count}" +
                              $" cacheHits={WorldSphereMod.Voxel.VoxelMeshCache.HitCount}" +
                              $" cacheMisses={WorldSphereMod.Voxel.VoxelMeshCache.MissCount}" +
                              $" gcMB={System.GC.GetTotalMemory(false) / 1048576f:F1}");
                }
                catch { }
            }
        }
    }
}
