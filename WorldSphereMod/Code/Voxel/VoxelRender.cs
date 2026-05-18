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
                Debug.LogWarning("[WorldSphereMod3D] No voxel shader found; disabling voxel renderer.");
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

                    Sprite sp = rd.main_sprites[i];
                    if (sp == null) continue;
                    Mesh m = VoxelMeshCache.Get(sp);
                    if (m == null) continue;

                    Vector3 pos = rd.positions[i];
                    Vector3 rot = rd.rotations[i];
                    Vector3 scl = rd.scales[i];
                    if (rd.flip_x_states[i]) scl.x = -scl.x;
                    Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(rot), scl);
                    Submit(m, trs, rd.colors[i]);
                    // Hide the sprite quad for this actor — we drew the 3D mesh instead.
                    rd.has_normal_render[i] = false;
                }
            }
        }

        // Building voxelization deferred to Phase 2 — buildings get a dedicated
        // procedural mesh pipeline rather than a per-pixel cube voxel mesh.
        // BuildingManager.render_data may not share the actor render_data layout
        // 1:1 and we can't verify without a Unity build, so we leave buildings
        // as upstream sprite billboards in Phase 1.
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
            VoxelRender.Flush();
        }
    }
}
