using UnityEngine;

namespace WorldSphereMod.Water
{
    // RETIRED (ADR-fork-terrain-water-slope P2): water is now a corner-averaged
    // sub-mesh inside the Compound-Spheres fork (HeightFieldRenderer.ConfigureWater),
    // not a main-mod billboard overlay. This class is gutted to a no-op stub; the
    // public surface (Instance/_renderer/Destroy/Create/RequestRebuild) is kept only
    // so the few remaining references (ProceduralSky.SyncReflections, WaterRender)
    // compile. Instance is always null, so those branches are dead.
    public sealed class WaterSurface : MonoBehaviour
    {
        public static WaterSurface? Instance => null;
        internal MeshRenderer? _renderer;

        public static WaterSurface? Create(Transform parent) => null;
        public static void Destroy() { }
        public static void RequestRebuild() { }
    }
}
