using System;
using HarmonyLib;
using UnityEngine;

namespace WorldSphereMod.Bridge
{
    /// <summary>
    /// Re-bind bridge host + drain queued RPC after save/load transitions.
    /// See docs/journeys/scratch/bridge-scene-transition-known-issue.md.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadWorld))]
    public static class BridgeLoadSavePostfix
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                BridgeServer.CaptureMainThread();
                BridgeServer.EnsureCreated();
                BridgeServer.DrainStaticQueue();
                if (Core.savedSettings != null && Core.savedSettings.DebugSanityCube)
                {
                    BridgeServer.LiveTelemetryProbeEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WSM3D][Bridge] loadWorld postfix survival failed: " + ex.Message);
            }
        }
    }
}
