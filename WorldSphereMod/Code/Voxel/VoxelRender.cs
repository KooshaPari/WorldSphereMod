using HarmonyLib;
using UnityEngine;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Phase 1 wiring. Sits on top of <see cref="VoxelMeshCache"/> + <see cref="MeshInstanceBatcher"/>
    /// and provides the two integration points the rest of the mod needs:
    ///   • <see cref="EnsureMaterial"/> — lazy material resolution (no shader asset shipped yet).
    ///   • <see cref="Submit"/> / <see cref="Flush"/> — the per-frame submission API.
    ///
    /// The actual per-actor / per-building submission happens in this file's Harmony
    /// patches as a Postfix on the existing render-data calculation passes. They run
    /// AFTER the upstream Prefix has populated <c>render_data</c>, then walk it,
    /// emit voxel meshes, and finally suppress the upstream sprite render by clearing
    /// <c>has_normal_render</c> for the actors we drew in 3D.
    ///
    /// Gated behind <see cref="SavedSettings.VoxelEntities"/>. Default off during
    /// alpha — flip in the in-game settings tab once a tester confirms it renders.
    /// </summary>
    public static class VoxelRender
    {
        static Material? _material;
        static bool _materialAttempted;

        /// <summary>
        /// Destroy the cached material and clear the resolve-attempted latch. Call when
        /// the world reloads — static fields outlive Unity's scene teardown and the
        /// underlying Material may have been invalidated.
        /// TODO: wire from a world-reload Postfix once one exists in Core. Until then
        /// only matters across multiple in-session world generations.
        /// </summary>
        public static void Reset()
        {
            if (_material != null) Object.Destroy(_material);
            _material = null;
            _materialAttempted = false;
        }

        /// <summary>
        /// Resolve a material capable of rendering the voxel mesh's per-vertex colors.
        /// Walks a fallback chain of Unity built-in shaders so we don't need to ship a
        /// new shader asset in Phase 1 (Phase 5 introduces VoxelLit.shader and a real
        /// lit + shadow-casting material via the AssetBundle).
        /// </summary>
        public static bool EnsureMaterial()
        {
            if (_material != null) return true;
            if (_materialAttempted) return false;
            _materialAttempted = true;

            string[] candidates =
            {
                "Universal Render Pipeline/Particles/Unlit",
                "Particles/Standard Unlit",
                "Sprites/Default",
                "Hidden/Internal-Colored",
            };
            Shader? s = null;
            foreach (var name in candidates)
            {
                s = Shader.Find(name);
                if (s != null) break;
            }
            if (s == null)
            {
                Debug.LogWarning("[WSM3D] No voxel shader found; disabling voxel renderer.");
                return false;
            }
            _material = new Material(s) { name = "WSM3D.Voxel.Placeholder", enableInstancing = true };
            return true;
        }

        /// <summary>Per-frame submission. Matrix should already include scale.</summary>
        public static void Submit(Mesh mesh, Matrix4x4 trs, Color tint)
        {
            if (_material == null && !EnsureMaterial()) return;
            MeshInstanceBatcher.Submit(mesh, _material!, trs, tint);
        }

        /// <summary>Issue all batched draw calls. Call once per frame after submissions.</summary>
        public static void Flush()
        {
            if (_material == null) return;
            MeshInstanceBatcher.Flush();
            VoxelMeshCache.Tick();
        }

        // ---------------------------------------------------------------------
        // Harmony hooks. Registered automatically via Patcher.PatchAll on the
        // existing Core.Patch() pass because [HarmonyPatch] is declared here.

        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.precalculateRenderDataParallel))]
        public static class ActorVoxelEmit
        {
            [HarmonyPostfix]
            public static void EmitVoxels(ActorManager __instance)
            {
                if (!Core.IsWorld3D || !Core.savedSettings.VoxelEntities) return;
                if (!EnsureMaterial()) return;

                var rd = __instance.render_data;
                var arr = __instance.visible_units.array;
                int n = __instance.visible_units.count;
                for (int i = 0; i < n; i++)
                {
                    Actor a = arr[i];
                    if (a == null || a.asset == null) continue;
                    // Per-asset opt-out: the existing v1 API hands designers a way to
                    // mark assets as "perp" (ground-aligned billboard). Those keep
                    // sprite rendering for now — they tend to be flat decals (arrows,
                    // ground markers) where voxelization adds nothing.
                    if (Constants.PerpActors.ContainsKey(a.asset.id)) continue;
                    if (!rd.has_normal_render[i]) continue;

                    Vector3 cullPos = rd.positions[i];
                    float radius = 0.5f;
                    if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, radius))
                    {
                        // Offscreen per our frustum probe: skip voxel emission.
                        // Do NOT flip has_normal_render — vanilla rendering has
                        // its own culling, and a false negative here (stale
                        // planes, wrong camera, position-space mismatch) used
                        // to make the actor entirely invisible because we hid
                        // the sprite without drawing a replacement.
                        continue;
                    }
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, a.GetHashCode());

                    if (Core.savedSettings.SkeletalAnimation && tier != WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        WorldSphereMod.Rig.RigType rigType = ResolveRigType(a.asset.id);
                        if (rigType != WorldSphereMod.Rig.RigType.None)
                        {
                            Vector3 skPos = rd.positions[i];
                            Vector3 skRot = rd.rotations[i];
                            Vector3 skScl = rd.scales[i];
                            if (rd.flip_x_states[i]) skScl.x = -skScl.x;
                            WorldSphereMod.Rig.RigDriver.SubmitSkinnedActor(
                                a, skPos, Quaternion.Euler(0f, skRot.y, 0f), skScl, rd.colors[i], rigType);
                            rd.has_normal_render[i] = false;
                            continue;
                        }
                    }

                    Sprite sp = rd.main_sprites[i];
                    if (sp == null) continue;

                    if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        Mesh? im = WorldSphereMod.LOD.ImpostorBillboard.GetOrCreate(sp);
                        Material? imMat = WorldSphereMod.LOD.ImpostorBillboard.GetMaterial();
                        if (im == null || imMat == null) continue;
                        Vector3 imPos = rd.positions[i];
                        Vector3 imScl = rd.scales[i];
                        if (rd.flip_x_states[i]) imScl.x = -imScl.x;
                        Quaternion br = Tools.RotateToCamera(ref imPos);
                        Matrix4x4 imTrs = Matrix4x4.TRS(imPos, br, imScl);
                        MeshInstanceBatcher.Submit(im, imMat, imTrs, rd.colors[i]);
                        rd.has_normal_render[i] = false;
                        continue;
                    }

                    Mesh m = VoxelMeshCache.Get(sp);
                    if (m == null) continue;

                    Vector3 pos = rd.positions[i];
                    Vector3 rot = rd.rotations[i];
                    Vector3 scl = rd.scales[i];
                    if (rd.flip_x_states[i]) scl.x = -scl.x;
                    // Z/X axes encode sprite-billboard lean; on a 3D mesh they topple the body. Yaw only here; lean returns in Phase 6 as a spine-bone tilt.
                    Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), scl);
                    Submit(m, trs, rd.colors[i]);
                    // Hide the sprite quad for this actor — we drew the 3D mesh instead.
                    rd.has_normal_render[i] = false;
                }
            }

            // TODO Step 11: replace with Constants.ActorRigTypes registry lookup so quadrupeds
            // and rigless assets (birds, snakes) route correctly. Default-humanoid is fine for
            // Step 5 because every rig still resolves to the same bind-pose identity output.
            static WorldSphereMod.Rig.RigType ResolveRigType(string assetId)
            {
                return WorldSphereMod.Rig.RigType.Humanoid;
            }
        }

        // Phase 1 fallback for buildings. Phase 2's procgen building meshes override
        // this when SavedSettings.ProceduralBuildings flips on; until then, when the
        // player turns Voxel Entities on, voxelizing the building sprite is the best
        // we can do for 3D buildings without procgen.
        [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.precalculateRenderDataParallel))]
        public static class BuildingVoxelEmit
        {
            [HarmonyPostfix]
            public static void EmitVoxels(BuildingManager __instance)
            {
                if (!Core.IsWorld3D || !Core.savedSettings.VoxelEntities) return;
                if (Core.savedSettings.ProceduralBuildings) return;
                if (!EnsureMaterial()) return;

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
                        // See ActorVoxelEmit cull-skip: leave the building's
                        // scale alone on a cull miss. Vanilla rendering will
                        // not draw an offscreen building anyway, and zeroing
                        // scales[i] used to hide buildings entirely when the
                        // frustum probe was wrong.
                        continue;
                    }
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, b.GetHashCode());

                    Sprite sp = rd.main_sprites[i];
                    if (sp == null) continue;

                    if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        Mesh? im = WorldSphereMod.LOD.ImpostorBillboard.GetOrCreate(sp);
                        Material? imMat = WorldSphereMod.LOD.ImpostorBillboard.GetMaterial();
                        // Impostor mesh build failed: fall through to vanilla
                        // sprite (don't zero scales — that's the "hide the
                        // sprite because we drew our own mesh" path, which
                        // we didn't actually do here).
                        if (im == null || imMat == null) continue;
                        Vector3 imPos = rd.positions[i];
                        Vector3 imScl = rd.scales[i];
                        if (rd.flip_x_states[i]) imScl.x = -imScl.x;
                        Quaternion br = Tools.RotateToCamera(ref imPos);
                        Matrix4x4 imTrs = Matrix4x4.TRS(imPos, br, imScl);
                        MeshInstanceBatcher.Submit(im, imMat, imTrs, rd.colors[i]);
                        rd.scales[i] = Vector3.zero;
                        continue;
                    }

                    Mesh m = VoxelMeshCache.Get(sp);
                    if (m == null) continue;

                    Vector3 pos = rd.positions[i];
                    Vector3 rot = rd.rotations[i];
                    Vector3 scl = rd.scales[i];
                    if (rd.flip_x_states[i]) scl.x = -scl.x;
                    Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), scl);
                    Submit(m, trs, rd.colors[i]);
                    // BuildingRenderData has no has_normal_render. Suppressing via scales[i]=0
                    // hides the sprite quad without nulling main_sprites (downstream
                    // calculateColoredSprite() chokes on null). Shadow sprite still draws as a
                    // ground decal under the 3D mesh — fine until Phase 5 ships real shadows.
                    rd.scales[i] = Vector3.zero;
                }
            }
        }
    }

    /// <summary>
    /// MonoBehaviour driver that calls <see cref="VoxelRender.Flush"/> once per frame
    /// in <c>LateUpdate</c>, after every render-data calculation has run. Attached to
    /// the mod's root GameObject in <c>Core.Init</c>.
    /// </summary>
    public sealed class VoxelFrameDriver : MonoBehaviour
    {
        void LateUpdate()
        {
            WorldSphereMod.LOD.FrustumCuller.UpdatePlanes();
            VoxelRender.Flush();
            VoxelMeshCache.DrainPendingDestroy();
            WorldSphereMod.ProcGen.ProcGenCache.DrainPendingDestroy();
            WorldSphereMod.Water.WaterRender.UpdateLifecycle();
            WorldSphereMod.Lighting.SunDriver.Update();
            WorldSphereMod.Foliage.CrossedQuadMeshCache.DrainPendingDestroy();
            WorldSphereMod.Fx.DecalPool.Tick();
            WorldSphereMod.Fx.PostFxController.ApplySetting(Core.savedSettings.PostFX);
        }
    }
}
