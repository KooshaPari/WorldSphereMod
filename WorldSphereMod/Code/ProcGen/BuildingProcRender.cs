using HarmonyLib;
using UnityEngine;
using WorldSphereMod.Foliage;
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

                    Vector3 cullPos = rd.positions[i];
                    float radius = 0.5f;
                    if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, radius))
                    {
                        rd.scales[i] = Vector3.zero;
                        continue;
                    }
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, b.GetHashCode());

                    if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        Sprite? impSp = rd.main_sprites[i];
                        if (impSp == null) { rd.scales[i] = Vector3.zero; continue; }
                        Mesh? im = WorldSphereMod.LOD.ImpostorBillboard.GetOrCreate(impSp);
                        Material? imMat = WorldSphereMod.LOD.ImpostorBillboard.GetMaterial();
                        if (im == null || imMat == null) { rd.scales[i] = Vector3.zero; continue; }
                        Vector3 imPos = rd.positions[i];
                        Vector3 imScl = rd.scales[i];
                        if (rd.flip_x_states[i]) imScl.x = -imScl.x;
                        Quaternion br = Tools.RotateToCamera(ref imPos);
                        Matrix4x4 imTrs = Matrix4x4.TRS(imPos, br, imScl);
                        MeshInstanceBatcher.Submit(im, imMat, imTrs, rd.colors[i]);
                        rd.scales[i] = Vector3.zero;
                        continue;
                    }

                    BuildingRules rules = BuildingRulesRegistry.Resolve(b.asset.id);

                    Vector3 pos = rd.positions[i];
                    Vector3 rot = rd.rotations[i];
                    Vector3 scl = rd.scales[i];
                    if (rd.flip_x_states[i]) scl.x = -scl.x;
                    Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), scl);

                    if (rules.Shape == BuildingShape.CrossedQuad || rules.Shape == BuildingShape.Single)
                    {
                        if (!FoliageMaterial.EnsureMaterial()) continue;
                        Sprite? sp = rd.main_sprites[i];
                        if (sp == null) continue;
                        Mesh? fm = CrossedQuadMeshCache.GetOrBuild(sp, rules.Shape, rules.SwayAmplitude);
                        if (fm == null) continue;
                        Material? mat = FoliageMaterial.Get();
                        if (mat == null) continue;
                        MeshInstanceBatcher.Submit(fm, mat, trs, rd.colors[i]);
                    }
                    else
                    {
                        Mesh m = ProcGenCache.GetOrGenerate(b.asset, rules);
                        if (m == null) continue;
                        VoxelRender.Submit(m, trs, rd.colors[i]);
                    }
                    // BuildingRenderData has no has_normal_render; zeroing scales hides the
                    // sprite quad without nulling main_sprites (downstream chokes on null).
                    rd.scales[i] = Vector3.zero;
                }
            }
        }
    }
}
