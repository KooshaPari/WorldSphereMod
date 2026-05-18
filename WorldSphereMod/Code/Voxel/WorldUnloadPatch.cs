using HarmonyLib;
using UnityEngine;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Centralised world-unload sink. Every fork-side cache that outlives Unity's
    /// scene teardown drains here so the next world generation starts clean instead
    /// of replaying stale meshes / hysteresis state from the previous session.
    /// Each call is try-wrapped so one cache's failure can't strand later drains.
    /// </summary>
    [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.Finish))]
    public static class WorldUnloadPatch
    {
        // TODO: three-Postfix ordering with other unload sinks is currently non-deterministic but harmless because cache contents are disjoint.
        [HarmonyPrefix]
        public static void OnFinish()
        {
            try { WorldSphereMod.Worldspace.WorldUIRenderer.OnWorldUnload(); } catch (System.Exception e) { Debug.LogWarning("WorldUIRenderer.OnWorldUnload: " + e.Message); }
            try { WorldSphereMod.Voxel.VoxelMeshCache.Clear(); } catch (System.Exception e) { Debug.LogWarning("VoxelMeshCache.Clear: " + e.Message); }
            try { WorldSphereMod.ProcGen.ProcGenCache.Clear(); } catch (System.Exception e) { Debug.LogWarning("ProcGenCache.Clear: " + e.Message); }
            try { WorldSphereMod.Foliage.CrossedQuadMeshCache.Clear(); } catch (System.Exception e) { Debug.LogWarning("CrossedQuadMeshCache.Clear: " + e.Message); }
            try { WorldSphereMod.Rig.RigCache.Clear(); } catch (System.Exception e) { Debug.LogWarning("RigCache.Clear: " + e.Message); }
            try { WorldSphereMod.Rig.RigDriver.Clear(); } catch (System.Exception e) { Debug.LogWarning("RigDriver.Clear: " + e.Message); }
            try { WorldSphereMod.LOD.ImpostorBillboard.Clear(); } catch (System.Exception e) { Debug.LogWarning("ImpostorBillboard.Clear: " + e.Message); }
            try { WorldSphereMod.LOD.LodSelector.ResetHysteresis(); } catch (System.Exception e) { Debug.LogWarning("LodSelector.ResetHysteresis: " + e.Message); }
            try { WorldSphereMod.Voxel.VoxelRender.Reset(); } catch (System.Exception e) { Debug.LogWarning("VoxelRender.Reset: " + e.Message); }
        }
    }
}
