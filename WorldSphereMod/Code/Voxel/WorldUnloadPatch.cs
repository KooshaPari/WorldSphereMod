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
            try { WorldSphereMod.Worldspace.WorldUIRenderer.OnWorldUnload(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] WorldUIRenderer.OnWorldUnload: " + e.Message); }
            try { WorldSphereMod.Voxel.VoxelMeshCache.Clear(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] VoxelMeshCache.Clear: " + e.Message); }
            try { WorldSphereMod.Voxel.SpriteVoxelizer.ClearPixelCache(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] SpriteVoxelizer.ClearPixelCache: " + e.Message); }
            try { WorldSphereMod.ProcGen.ProcGenCache.Clear(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] ProcGenCache.Clear: " + e.Message); }
            try { WorldSphereMod.Foliage.CrossedQuadMeshCache.Clear(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] CrossedQuadMeshCache.Clear: " + e.Message); }
            try { WorldSphereMod.Rig.RigCache.Clear(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] RigCache.Clear: " + e.Message); }
            try { WorldSphereMod.Rig.RigDriver.Clear(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] RigDriver.Clear: " + e.Message); }
            try { WorldSphereMod.LOD.ImpostorBillboard.Clear(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] ImpostorBillboard.Clear: " + e.Message); }
            try { WorldSphereMod.LOD.LodSelector.ResetHysteresis(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] LodSelector.ResetHysteresis: " + e.Message); }
            try { WorldSphereMod.Voxel.VoxelRender.Reset(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] VoxelRender.Reset: " + e.Message); }
            try { WorldSphereMod.Voxel.MeshInstanceBatcher.Reset(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] MeshInstanceBatcher.Reset: " + e.Message); }
            try { WorldSphereMod.Foliage.FoliageTileRender.ClearCache(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] FoliageTileRender.ClearCache: " + e.Message); }
        }
    }
}
