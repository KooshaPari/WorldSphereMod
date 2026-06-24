using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WorldSphereMod.Foliage;
using WorldSphereMod.Voxel;
using Debug = UnityEngine.Debug;

namespace WorldSphereMod.ProcGen
{
    public static class BuildingProcRender
    {
        static bool _procRenderTargetDiagLogged;
        static bool _firstBuildingPosLogged;
        // Per-frame budget cycling offset (same pattern as BuildingVoxelEmit).
        static int _budgetOffset;
        static bool _earlyReturnDiagLogged;
        static bool _buildingDrawDiagLogged;
        // IDLE FAST-PATH (#208 fps): when the visible-buildings identity hash is unchanged
        // and the visible count is the same, skip the procgen mesh cycle entirely. The FRONT
        // batched draws are still re-emitted by FlushQueuedBuildingDraws every frame from
        // the last good emit, so visuals are stable. Re-emit fires next frame when the count
        // differs or a sampled building's asset id changes (e.g. spawn / despawn / a building
        // entering or leaving the frustum). Mirrors the BuildingVoxelEmit hash check.
        static ulong _lastVisibleBuildingsHash;
        static int _lastVisibleBuildingsCount;
        static int _skippedStaticFrames;
        public static int SkippedStaticFrames => _skippedStaticFrames;

        static ulong GetBuildingAssetHash(Building building)
        {
            if (building == null || building.asset == null || string.IsNullOrEmpty(building.asset.id))
            {
                return 0UL;
            }
            return (ulong)building.asset.id.GetHashCode();
        }

        static ulong ComputeVisibleBuildingsHash(Building[] arr, int n)
        {
            if (arr == null || n <= 0) return 0;
            const ulong FNV_OFFSET = 1469598103934665603UL;
            const ulong FNV_PRIME = 1099511628211UL;
            ulong h = FNV_OFFSET;
            h = (h ^ GetBuildingAssetHash(arr[0])) * FNV_PRIME;
            if (n > 1)
            {
                int mid = n >> 1;
                h = (h ^ GetBuildingAssetHash(arr[mid])) * FNV_PRIME;
                h = (h ^ GetBuildingAssetHash(arr[n - 1])) * FNV_PRIME;
            }
            h = (h ^ (ulong)n) * FNV_PRIME;
            return h;
        }
        static readonly MethodInfo? TargetPrecalculateMethod = AccessTools.Method(
            typeof(BuildingManager),
            nameof(BuildingManager.precalculateRenderDataParallel));

        const int MaxMeshInstancedBatch = 1023;
        static readonly Dictionary<Mesh, List<Matrix4x4>> _buildingDrawBatches = new(); // BACK (emit fills)
        // FRONT buffer drawn EVERY frame by the flush; replaced wholesale at emit-end. Same
        // double-buffer fix as actor sprite cards — kills the building/tree FLASH (DrawMeshInstanced
        // is a 1-frame command but EmitMeshes runs intermittently, so clear-on-flush blanked them).
        static readonly Dictionary<Mesh, List<Matrix4x4>> _buildingFront = new();
        static readonly object _buildingBufLock = new object();
        static readonly Matrix4x4[] _meshInstancedMatrices = new Matrix4x4[MaxMeshInstancedBatch];

        [Phase(nameof(SavedSettings.ProceduralBuildings))]
        [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.precalculateRenderDataParallel))]
        public static class ProcMeshEmit
        {
            [HarmonyPostfix]
            public static void EmitMeshes(BuildingManager __instance)
            {
                bool isWorld3D = Core.IsWorld3D;
                bool proceduralBuildings = Core.savedSettings?.ProceduralBuildings ?? false;
                int visibleBuildingCount = __instance == null ? -1 : __instance._visible_buildings_count;
                if (!isWorld3D)
                {
                    LogEarlyReturn("!IsWorld3D", isWorld3D, proceduralBuildings, visibleBuildingCount);
                    return;
                }

                if (!proceduralBuildings)
                {
                    LogEarlyReturn("!ProceduralBuildings", isWorld3D, proceduralBuildings, visibleBuildingCount);
                    return;
                }

                Material? procBuildingMaterial = VoxelRender.GetResolvedMaterial();
                if (procBuildingMaterial == null)
                {
                    LogEarlyReturn("procBuildingMaterial == null", isWorld3D, proceduralBuildings, visibleBuildingCount);
                    return;
                }

                // Double-buffer: clear BACK at emit start; emit fills back; swap to FRONT at end.
                lock (_buildingBufLock)
                {
                    foreach (var b in _buildingDrawBatches.Values) b.Clear();
                }

                var rd = __instance.render_data;
                var arr = __instance._array_visible_buildings;
                int n = __instance._visible_buildings_count;

                // IDLE FAST-PATH (#208 fps): when the visible-building set is unchanged
                // (no spawn / despawn / building moved into frustum or out), skip the
                // per-building procgen mesh cycle entirely. The FRONT draw buffers are
                // still re-emitted by FlushQueuedBuildingDraws every frame from the last
                // good emit, so visuals are stable. Re-emit fires next frame when count
                // differs OR sampled building asset id changes.
                if (arr != null && n > 0 && n == _lastVisibleBuildingsCount)
                {
                    ulong h = ComputeVisibleBuildingsHash(arr, n);
                    if (h == _lastVisibleBuildingsHash)
                    {
                        _skippedStaticFrames++;
                        return;
                    }
                }
                if (arr != null && n > 0)
                {
                    _lastVisibleBuildingsHash = ComputeVisibleBuildingsHash(arr, n);
                }
                _lastVisibleBuildingsCount = n;

                bool profile = Core.savedSettings.ProfilerDump;
                Stopwatch totalSw = new Stopwatch();
                Stopwatch impostorSw = new Stopwatch();
                Stopwatch regularSw = new Stopwatch();
                int impostorCount = 0;
                int regularCount = 0;
                Dictionary<int, Mesh> frameSpriteMeshCache = new Dictionary<int, Mesh>(128);

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
                    Vector3 rawPos = rd.positions[i];
                    int tileX = Mathf.RoundToInt(rawPos.x);
                    int tileY = Mathf.RoundToInt(rawPos.y);
                    float height = rawPos.z;
                    int buildingId = b.GetHashCode();
                    string assetId = b.asset.id;
                    if (_lastRenderStateByBuildingId.TryGetValue(buildingId, out BuildingRenderState lastState) &&
                        lastState.Matches(assetId, tileX, tileY, height))
                    {
                        continue;
                    }

                    if (cullPos.z < Constants.ZDisplacement * 0.5f)
                    {
                        cullPos = cullPos.To3DTileHeight(false);
                    }
                    if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, 2f))
                    {
                        continue;
                    }
                    // DISTANCE GATE (#208 fps): buildings beyond the camera-visible radius
                    // can't be on-screen; the postfix walks the full visible-buildings list
                    // every cycle, so pruning these here cuts per-frame cost on big kingdoms
                    // (2364 buildings -> 8-15fps idle spikes from BuildingManager.precalculate
                    // RenderDataParallel). Gate uses RenderRange scaled by the unified building
                    // scale + 1.5x LOD-tier headroom. (#208)
                    float buildingScale = Core.savedSettings.VoxelScaleMultiplier * Core.savedSettings.BuildingVoxelScaleFactor;
                    float maxDist = Core.savedSettings.RenderRange * buildingScale * 1.5f;
                    float maxDistSqr = maxDist * maxDist;
                    Vector3 camPos = WorldSphereMod.NewCamera.CameraManager.transform.position;
                    if ((cullPos - camPos).sqrMagnitude > maxDistSqr)
                    {
                        rd.scales[i] = Vector3.zero;
                        continue;
                    }
                    // Unified building scale: same multiplication the legacy (non-procgen)
                    // path applies below (`scl *= VoxelScaleMultiplier * BuildingVoxelScaleFactor`).
                    // BuildingSize is NOT folded in here — it was double-counting with the
                    // rd.scales[i] upstream sprite scale and made procgen buildings ~2× smaller
                    // than voxel-path buildings for the same asset. (#208)
                    // ^ NOTE: buildingScale is now declared above the distance gate so both the
                    //   gate and the LOD/legacy paths can read it.
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.SelectForBuilding(
                        cullPos,
                        b.GetHashCode(),
                        buildingScale);
                    bool submitted = false;

                    if (tier == WorldSphereMod.LOD.LodTier.Cull)
                    {
                        rd.scales[i] = Vector3.zero;
                        if (profile) impostorSw.Start();
                        try
                        {
                            // FAR TIER = CULL, NEVER A BILLBOARD. Matching the actor/drop/
                            // projectile voxel paths (VoxelRender), distant buildings render
                            // NOTHING rather than a flat camera-facing impostor quad. The
                            // ImpostorBillboard cache is removed entirely. Near buildings still
                            // voxelize through the regular branch below; the vanilla 2D sprite
                            // for this building is suppressed via scales[i]=0 there, but at the
                            // Impostor tier we simply skip the submit so it's invisible at range.
                            _ = i;
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
                            rawPos = pos;
                            if (pos.z < Constants.ZDisplacement * 0.5f)
                            {
                                pos = pos.To3DTileHeight(false);
                            }
                            float renderRange = Core.savedSettings.RenderRange;
                            if (pos.sqrMagnitude > renderRange * renderRange * 4f)
                            {
                                continue;
                            }
                            Vector3 rot = rd.rotations[i];
                            Vector3 scl = rd.scales[i];
                            if (rd.flip_x_states[i]) scl.x = -scl.x;
                            scl.z = scl.x;
                            scl *= Core.savedSettings.VoxelScaleMultiplier * Core.savedSettings.BuildingVoxelScaleFactor;
                            Sprite? sp = rd.main_sprites[i];
                            if (sp == null) continue;
                            Matrix4x4 legacyTrs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), scl);

                            if (rules.Shape == BuildingShape.CrossedQuad || rules.Shape == BuildingShape.Single)
                            {
                                // VOXEL-OR-INVISIBLE (user, 2026-05-30): these foliage-tier
                                // building assets (trees/bushes flagged CrossedQuad, rocks/
                                // ground decals flagged Single) are REAL voxel volumes now —
                                // never crossed-quad billboards. CrossedQuad → OrganicBlob puff;
                                // Single → flat ground decal. The crossed-quad mesh path is gone.
                                LogFirstBuildingPos(rawPos, pos, scl);
                                if (!FoliageDensity.ShouldRender(rawPos, b.asset.id, Core.savedSettings.FoliageDensity))
                                {
                                    rd.scales[i] = Vector3.zero;
                                    continue;
                                }
                                Matrix4x4 trs = legacyTrs;
                                if (!FoliageMaterial.EnsureMaterial()) continue;
                                ShapeHint hint = rules.Shape == BuildingShape.Single
                                    ? ShapeHint.Flat
                                    : ShapeHint.OrganicBlob;
                                Mesh? fm = VoxelMeshCache.Get(sp, hint);
                                if (fm == null || fm.vertexCount == 0) continue;
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
                                    Mesh? m = ProcGenCache.GetOrGenerate(b.asset, rules);
                                    if (m == null) continue;
                                float procScale = buildingScale;
                                    if (rd.flip_x_states[i]) procScale = -procScale;
                                    Matrix4x4 procTrs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), Vector3.one * procScale);
                                    if (TryQueueBuildingDraw(m, procTrs))
                                    {
                                        submitted = true;
                                    }
                                }
                                else
                                {
                                    LogFirstBuildingPos(rawPos, pos, scl);
                                    Mesh m = VoxelMeshCache.Get(sp);
                                    if (m == null || m.vertexCount == 0) continue;
                                    if (VoxelRender.Submit(m, legacyTrs, Color.white))
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
                        _lastRenderStateByBuildingId[buildingId] = new BuildingRenderState(assetId, tileX, tileY, height);
                    }
                }
                // Build-time queue is now rendered every Unity frame from the shared
                // VoxelRender LateUpdate sink so DrawMeshInstanced is not tied to
                // precalculateRenderDataParallel cadence.
                // DOUBLE-BUFFER SWAP: emit complete — replace FRONT (drawn every frame) with this
                // emit's BACK contents so buildings/trees stay drawn across the intermittent-emit
                // gap (no flash). Front never goes empty between emits.
                lock (_buildingBufLock)
                {
                    _buildingFront.Clear();
                    foreach (var kv in _buildingDrawBatches)
                    {
                        if (kv.Value.Count == 0) continue;
                        _buildingFront[kv.Key] = new List<Matrix4x4>(kv.Value);
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

            static bool TryQueueBuildingDraw(Mesh mesh, Matrix4x4 matrix)
            {
                if (mesh == null) return false;

                if (!_buildingDrawBatches.TryGetValue(mesh, out var matrices))
                {
                    matrices = new List<Matrix4x4>(16);
                    _buildingDrawBatches[mesh] = matrices;
                }

                matrices.Add(matrix);
                return true;
            }

            internal static void FlushQueuedBuildingDraws(Material? material, out int flushCount, out int matricesTotal)
            {
                flushCount = 0;
                matricesTotal = 0;

                // Draw the FRONT buffer every frame (NOT cleared here — emit replaces it). Kills flash.
                if (material == null)
                {
                    return;
                }

                material.enableInstancing = true;
                List<KeyValuePair<Mesh, List<Matrix4x4>>> front;
                lock (_buildingBufLock)
                {
                    front = new List<KeyValuePair<Mesh, List<Matrix4x4>>>(_buildingFront);
                }
                foreach (var pair in front)
                {
                    List<Matrix4x4> matrices = pair.Value;
                    if (matrices == null || matrices.Count == 0) continue;

                    Mesh mesh = pair.Key;
                    if (mesh == null) continue;
                    int start = 0;
                    while (start < matrices.Count)
                    {
                        int count = Mathf.Min(MaxMeshInstancedBatch, matrices.Count - start);
                        matrices.CopyTo(start, _meshInstancedMatrices, 0, count);
                        Graphics.DrawMeshInstanced(mesh, 0, material, _meshInstancedMatrices, count);
                        MeshInstanceBatcher.FrameDrawCalls++;
                        WorldSphereMod.Voxel.VoxelRender.ActorDrawCallsCumulative++; // shared cumulative draw counter
                        start += count;
                        flushCount++;
                    }

                    matricesTotal += matrices.Count;
                }

                // One-shot diagnostic: first non-empty flush, so we capture the first frame where queues were actually rendered.
                if (!_buildingDrawDiagLogged && matricesTotal > 0)
                {
                    _buildingDrawDiagLogged = true;
                    Debug.Log($"[WSM3D][BUILDING-DRAW-DIAG] flushCount={flushCount} matricesTotal={matricesTotal} materialMissing=false");
                }
            }

            static void LogEarlyReturn(
                string reason,
                bool isWorld3D,
                bool proceduralBuildings,
                int buildingCount)
            {
                if (_earlyReturnDiagLogged) return;
                _earlyReturnDiagLogged = true;
                Debug.Log($"[WSM3D][PROCRENDER-DIAG] early-return reason={reason} IsWorld3D={isWorld3D} ProceduralBuildings={proceduralBuildings} buildingCount={buildingCount}");
            }
        }

        [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.precalculateRenderDataParallel))]
        public static class ProcRenderTargetProbe
        {
            [HarmonyPrefix]
            public static void PrecalculateTargetProbe(BuildingManager __instance)
            {
                if (_procRenderTargetDiagLogged) return;
                _procRenderTargetDiagLogged = true;

                int buildingCount = __instance == null ? -1 : __instance._visible_buildings_count;
                bool hasTarget = TargetPrecalculateMethod != null;
                bool hasPatcher = Core.Patcher != null;
                bool hasEmitPatch = false;
                int patchedMethodCount = 0;
                string patchedMethods = "N/A";
                bool hasPhaseAttr = typeof(ProcMeshEmit).GetCustomAttribute<PhaseAttribute>() != null;
                string phaseSetting = hasPhaseAttr ? nameof(SavedSettings.ProceduralBuildings) : "n/a";
                bool phaseFlagValue = Core.savedSettings != null && Core.savedSettings.ProceduralBuildings;
                string targetMethodName = hasTarget ? $"{TargetPrecalculateMethod!.DeclaringType?.Name}.{TargetPrecalculateMethod.Name}" : "null";

                if (hasPatcher)
                {
                    try
                    {
                        var methods = Core.Patcher!.GetPatchedMethods();
                        foreach (var method in methods)
                        {
                            patchedMethodCount++;
                            if (method != null && method.DeclaringType == typeof(ProcMeshEmit))
                            {
                                hasEmitPatch = true;
                            }
                        }

                        patchedMethods = $"count={patchedMethodCount}";
                    }
                    catch (System.Exception ex)
                    {
                        patchedMethods = $"ERROR:{ex.Message}";
                    }
                }

                Debug.Log($"[WSM3D][PROCRENDER-DIAG] patch-state phaseFlag={phaseSetting}:{phaseFlagValue} patchInstalled={hasEmitPatch} targetMethod={targetMethodName} targetFound={hasTarget} isWorld3D={Core.IsWorld3D} buildingCount={buildingCount} patcherReady={hasPatcher} patchedMethods={patchedMethods}");
            }
        }
    }
}
