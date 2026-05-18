using HarmonyLib;
using UnityEngine;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.ProcGen
{
    public static class BuildingProcRender
    {
        [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.precalculateRenderDataParallel))]
        public static class ProcMeshEmit
        {
            [HarmonyPostfix]
            public static void EmitMeshes(BuildingManager __instance)
            {
                if (!Core.IsWorld3D || !Core.savedSettings.ProceduralBuildings) return;
                // Reuse the Phase 1 voxel material until Phase 5 ships VoxelLit.shader.
                if (!VoxelRender.EnsureMaterial()) return;

                var rd = __instance.render_data;
                var arr = __instance._array_visible_buildings;
                int n = __instance._visible_buildings_count;
                for (int i = 0; i < n; i++)
                {
                    Building b = arr[i];
                    if (b == null || b.asset == null) continue;
                    if (Constants.PerpBuildings.ContainsKey(b.asset.id)) continue;

                    BuildingRules rules = BuildingRulesRegistry.Resolve(b.asset.id);
                    Mesh m = ProcGenCache.GetOrGenerate(b.asset, rules);
                    if (m == null) continue;

                    Vector3 pos = rd.positions[i];
                    Vector3 rot = rd.rotations[i];
                    Vector3 scl = rd.scales[i];
                    if (rd.flip_x_states[i]) scl.x = -scl.x;
                    Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), scl);
                    VoxelRender.Submit(m, trs, rd.colors[i]);
                    // BuildingRenderData has no has_normal_render; zeroing scales hides the
                    // sprite quad without nulling main_sprites (downstream chokes on null).
                    rd.scales[i] = Vector3.zero;
                }
            }
        }
    }
}
