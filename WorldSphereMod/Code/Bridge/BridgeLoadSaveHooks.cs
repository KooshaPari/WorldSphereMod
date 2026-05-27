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
                    int _retries = 0;
                    MapLoaderAction become3DAction = null;
                    become3DAction = delegate
                    {
                        if (Core.IsWorld3D) { _become3DQueued = false; return; }
                        if (_retries > 200)
                        {
                            Debug.LogError("[WSM3D][Bridge] Become3D deferred: exhausted 200 retries.");
                            _become3DQueued = false;
                            return;
                        }
                        _retries++;
                        if (World.world == null || World.world.tiles == null || World.world.tiles.Length == 0
                            || MapBox.width <= 0 || MapBox.height <= 0
                            || World.world._map_layers == null)
                        {
                            SmoothLoader.add(become3DAction, "Becoming 3D!");
                            return;
                        }
                        _become3DQueued = false;
                        Core.Sphere.PrepareWorld();
                        Core.Generated = true;
                        Core.Become3D();
                    };
                    SmoothLoader.add(become3DAction, "Becoming 3D!");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WSM3D][Bridge] loadWorld postfix survival failed: " + ex.Message);
            }
        }
    }
}
