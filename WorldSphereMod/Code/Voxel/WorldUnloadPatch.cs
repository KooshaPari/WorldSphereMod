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
        // Ordering: HarmonyPriority.Last so this Prefix runs AFTER any other mod's
        // Prefixes on Core.Sphere.Finish. Cache contents are disjoint with vanilla
        // unload paths so order doesn't actually matter for correctness, but
        // determinism makes debugging cleaner.
        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        public static void OnFinish()
        {
            try { WorldSphereMod.Worldspace.WorldUIRenderer.OnWorldUnload(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] WorldUIRenderer.OnWorldUnload: " + e.Message); }
            try { WorldSphereMod.Voxel.VoxelMeshCache.DrainPendingDestroy(); WorldSphereMod.Voxel.VoxelMeshCache.Clear(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] VoxelMeshCache.DrainPendingDestroy+Clear: " + e.Message); }
            try { WorldSphereMod.Voxel.SpriteVoxelizer.ClearPixelCache(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] SpriteVoxelizer.ClearPixelCache: " + e.Message); }
            try { WorldSphereMod.ProcGen.ProcGenCache.Clear(); WorldSphereMod.ProcGen.ProcGenCache.DrainPendingDestroy(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] ProcGenCache.Clear+DrainPendingDestroy: " + e.Message); }
            try { WorldSphereMod.Foliage.CrossedQuadMeshCache.Clear(); WorldSphereMod.Foliage.CrossedQuadMeshCache.DrainPendingDestroy(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] CrossedQuadMeshCache.Clear+DrainPendingDestroy: " + e.Message); }
            try { WorldSphereMod.Rig.RigCache.DrainPendingDestroy(); WorldSphereMod.Rig.RigCache.Clear(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] RigCache.DrainPendingDestroy+Clear: " + e.Message); }
            try { WorldSphereMod.Lighting.SunDriver.Teardown(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] SunDriver.Teardown: " + e.Message); }
            try { WorldSphereMod.Rig.RigDriver.Clear(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] RigDriver.Clear: " + e.Message); }
            try { WorldSphereMod.LOD.ImpostorBillboard.Clear(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] ImpostorBillboard.Clear: " + e.Message); }
            try { WorldSphereMod.LOD.LodSelector.ResetHysteresis(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] LodSelector.ResetHysteresis: " + e.Message); }
            try { WorldSphereMod.Fx.Environmental.Clear(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] Environmental.Clear: " + e.Message); }
            try { WorldSphereMod.Fx.CloudCrossedQuadRender.Clear(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] CloudCrossedQuadRender.Clear: " + e.Message); }
            try { WorldSphereMod.Voxel.VoxelRender.Reset(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] VoxelRender.Reset: " + e.Message); }
            try { WorldSphereMod.Voxel.MeshInstanceBatcher.Reset(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] MeshInstanceBatcher.Reset: " + e.Message); }
            try { WorldSphereMod.Foliage.FoliageTileRender.ClearCache(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] FoliageTileRender.ClearCache: " + e.Message); }
            try { WorldSphereMod.Terrain.MountainSlopeSurface.Destroy(); } catch (System.Exception e) { Debug.LogWarning("[WSM3D] MountainSlopeSurface.Destroy: " + e.Message); }
        }
    }
}
