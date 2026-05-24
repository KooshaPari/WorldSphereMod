using HarmonyLib;
using UnityEngine;

namespace WorldSphereMod.Fx
{
    /// <summary>
    /// Phase 9 Step 4 lifecycle plumbing. Mirrors the Sphere.Begin/Finish Postfix/Prefix
    /// pair pattern used by <see cref="Water.WaterRender"/> — initializes the particle
    /// library + decal pool when a 3D world spins up, tears them down on Finish. The
    /// visual sprite-suppression hook is deferred to Step 5; this file is plumbing only.
    /// </summary>
    public static class EffectPatches9
    {
        static Transform? _root;

        [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.Begin))]
        public static class BeginPostfix
        {
            [HarmonyPostfix]
            public static void OnBegin()
            {
                if (!Core.IsWorld3D) return;
                if (Mod.Object == null) return;
                if (_root == null)
                {
                    _root = new GameObject("WSM3D.FxRoot").transform;
                    _root.SetParent(Mod.Object.transform, worldPositionStays: false);
                }
                // Particle library is unconditional — it always pools 16 systems; Fire bails on
                // unmapped effect IDs. Decals stay opt-in but Init the pool eagerly.
                ParticleEffectLibrary.Init();
                DecalPool.Init(_root);
            }
        }

        [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.Finish))]
        public static class FinishPrefix
        {
            [HarmonyPrefix]
            public static void OnFinish()
            {
                VoxelParticleBurst.Clear();
                ParticleEffectLibrary.Clear();
                DecalPool.Clear();
                if (_root != null) { Object.Destroy(_root.gameObject); _root = null; }
            }
        }
    }
}
