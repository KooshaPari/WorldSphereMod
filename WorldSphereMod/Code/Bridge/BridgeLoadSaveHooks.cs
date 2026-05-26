using System;
using HarmonyLib;
using UnityEngine;

namespace WorldSphereMod.Bridge
{
    /// <summary>
    /// Re-bind bridge host + drain queued RPC after save/load transitions.
    /// WorldBox exposes loadWorld(string, bool) only (no single-string overload).
    /// See docs/journeys/scratch/bridge-scene-transition-known-issue.md.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadWorld), typeof(string), typeof(bool))]
    public static class BridgeLoadSaveHooks
    {
        static bool _become3DQueued;

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
                if (Core.savedSettings != null && Core.savedSettings.Is3D && !Core.IsWorld3D && !_become3DQueued)
                {
                    _become3DQueued = true;
                    Core.Generated = true;
                    SmoothLoader.add(delegate { _become3DQueued = false; Core.Become3D(); }, "Becoming 3D!");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WSM3D][Bridge] loadWorld postfix survival failed: " + ex.Message);
            }
        }
    }
}
