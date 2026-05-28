using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using WorldSphereMod.Foliage;
using WorldSphereMod.Voxel;
using Debug = UnityEngine.Debug;

namespace WorldSphereMod.ProcGen
{
    public static class BuildingProcRender
    {
        static bool _firstBuildingPosLogged;
        // Per-frame budget cycling offset (same pattern as BuildingVoxelEmit).
        static int _budgetOffset;

        [Phase(nameof(SavedSettings.ProceduralBuildings))]
        [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.precalculateRenderDataParallel))]
        public static class ProcMeshEmit
        {
            [HarmonyPostfix]
            public static void EmitMeshes(BuildingManager __instance)
            {
                if (!Core.IsWorld3D || !Core.savedSettings.ProceduralBuildings) return;
                if (!VoxelRender.EnsureMaterial()) return;

                var rd = __instance.render_data;
                var arr = __instance._array_visible_buildings;
                int n = __instance._visible_buildings_count;
                bool profile = Core.savedSettings.ProfilerDump;
                Stopwatch totalSw = new Stopwatch();
                Stopwatch impostorSw = new Stopwatch();
                Stopwatch regularSw = new Stopwatch();
                int impostorCount = 0;
                int regularCount = 0;

                if (profile) totalSw.Start();

                // Per-frame budget: only process a slice of visible buildings each
                // frame, cycling through the full set. 0 = unlimited.
                int budget = Core.savedSettings.BuildingRenderBudget;
                int start = 0;
                int end = n;
                if (budget > 0 && n > budget)
                {
                    if (_budgetOffset >= n) _budgetOffset = 0;
                    start = _budgetOffset;
                    end = UnityEngine.Mathf.Min(start + budget, n);
                    _budgetOffset = end >= n ? 0 : end;
                }

                for (int i = start; i < end; i++)
                {
                    Building b = arr[i];
                    if (b == null || b.asset == null) continue;
                    if (Constants.PerpBuildings.ContainsKey(b.asset.id)) continue;

                    Vector3 cullPos = rd.positions[i];
                    if (cullPos.z < Constants.ZDisplacement * 0.5f)
                    {
                        cullPos = cullPos.To3DTileHeight(false);
                    }
                    if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, 2f))
                    {
                        continue;
                    }
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, b.GetHashCode());
                    bool submitted = false;

                    if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        if (profile) impostorSw.Start();
                        try
                        {
                            Sprite? impSp = rd.main_sprites[i];
                            if (impSp == null) continue;
                            Mesh? im = WorldSphereMod.LOD.ImpostorBillboard.GetOrCreate(impSp);
                            Material? imMat = WorldSphereMod.LOD.ImpostorBillboard.GetMaterial(impSp);
                            if (im == null || imMat == null) continue;
                            Vector3 imPos = rd.positions[i];
                            Vector3 imScl = rd.scales[i];
                            if (rd.flip_x_states[i]) imScl.x = -imScl.x;
                            if (imPos.z < Constants.ZDisplacement * 0.5f)
                            {
                                imPos = imPos.To3DTileHeight(false);
                            }
                            Quaternion br = Tools.RotateToCamera(ref imPos);
                            Matrix4x4 imTrs = Matrix4x4.TRS(imPos, br, imScl);
                            if (!MeshInstanceBatcher.InstancingBroken)
                            {
                                MeshInstanceBatcher.Submit(im, imMat, imTrs, Color.white);
                                submitted = true;
                            }
                        }
                        finally
                        {
                            if (profile)
                            {
                                impostorSw.Stop();
                                impostorCount++;
                            }
                        }
                    }
                    else
                    {
                        if (profile) regularSw.Start();
                        try
                        {
                            BuildingRules rules = BuildingRulesRegistry.Resolve(b.asset.id);

                            Vector3 pos = rd.positions[i];
                            Vector3 rawPos = pos;
                            if (pos.z < Constants.ZDisplacement * 0.5f)
                            {
                                pos = pos.To3DTileHeight(false);
                            }
                            Vector3 rot = rd.rotations[i];
                            Vector3 scl = rd.scales[i];
                            if (rd.flip_x_states[i]) scl.x = -scl.x;
                            scl.z = scl.x;
                            scl *= Core.savedSettings.VoxelScaleMultiplier;
                            Sprite? sp = rd.main_sprites[i];
                            if (sp == null) continue;

                            if (rules.Shape == BuildingShape.CrossedQuad || rules.Shape == BuildingShape.Single)
                            {
                                LogFirstBuildingPos(rawPos, pos, scl);
                                if (!FoliageDensity.ShouldRender(rawPos, b.asset.id, Core.savedSettings.FoliageDensity))
                                {
                                    rd.scales[i] = Vector3.zero;
                                    continue;
                                }
                                Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), scl);
                                if (!FoliageMaterial.EnsureMaterial()) continue;
                                Mesh? fm = CrossedQuadMeshCache.GetOrBuild(sp, rules.Shape, rules.SwayAmplitude, b.asset.id);
                                if (fm == null) continue;
                                Material? mat = FoliageMaterial.Get();
                                if (mat == null) continue;
                                if (!MeshInstanceBatcher.InstancingBroken)
                                {
                                    MeshInstanceBatcher.Submit(fm, mat, trs, Color.white);
                                    submitted = true;
                                }
                            }
                            else
                            {
                                if (Core.savedSettings.BuildingStyleProcgen)
                                {
                                    LogFirstBuildingPos(rawPos, pos, scl);
                                    Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), scl);
                                    Mesh? m = ProcGenCache.GetOrGenerate(b.asset, rules);
                                    if (m == null) continue;
                                    if (VoxelRender.Submit(m, trs, Color.white))
                                    {
                                        submitted = true;
                                    }
                                }
                                else
                                {
                                    LogFirstBuildingPos(rawPos, pos, scl);
                                    Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), scl);
                                    Mesh m = VoxelMeshCache.Get(sp);
                                    if (m == null || m.vertexCount == 0) continue;
                                    if (VoxelRender.Submit(m, trs, Color.white))
                                    {
                                        submitted = true;
                                    }
                                }
                            }
                        }
                        finally
                        {
                            if (profile)
                            {
                                regularSw.Stop();
                                regularCount++;
                            }
                        }
                    }

                    if (submitted)
                    {
                        rd.scales[i] = Vector3.zero;
                    }
                }

                if (profile)
                {
                    totalSw.Stop();
                    Debug.Log($"[WSM3D][PERF] BuildingProcRender.EmitMeshes total={totalSw.Elapsed.TotalMilliseconds:F3}ms");
                    Debug.Log($"[WSM3D][PERF] BuildingProcRender.EmitMeshes.Impostor={impostorSw.Elapsed.TotalMilliseconds:F3}ms count={impostorCount}");
                    Debug.Log($"[WSM3D][PERF] BuildingProcRender.EmitMeshes.Regular={regularSw.Elapsed.TotalMilliseconds:F3}ms count={regularCount}");
                }
            }

            static void LogFirstBuildingPos(Vector3 rawPos, Vector3 liftedPos, Vector3 scl)
            {
                if (_firstBuildingPosLogged) return;
                _firstBuildingPosLogged = true;
                Debug.Log($"[WSM3D] First-building pos: raw={rawPos}, lifted={liftedPos}, scl={scl}");
            }
        }
    }
}
