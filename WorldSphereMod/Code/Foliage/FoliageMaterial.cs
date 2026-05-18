using UnityEngine;

namespace WorldSphereMod.Foliage
{
    /// <summary>
    /// Owns the foliage <see cref="Material"/> handle. Tries the project-shipped
    /// FoliageWind shader (loaded out of Resources) and falls back to the built-in
    /// Sprites/Default so emit paths never hard-fail when the AssetBundle hasn't
    /// landed. Mirrors <c>VoxelRender.EnsureMaterial</c>.
    /// </summary>
    public static class FoliageMaterial
    {
        static Material? _material;
        static bool _attempted;

        public static bool EnsureMaterial()
        {
            if (_material != null) return true;
            if (_attempted) return false;
            _attempted = true;

            Shader? s = Resources.Load<Shader>("Shaders/FoliageWind");
            if (s == null) s = Shader.Find("Sprites/Default");
            if (s == null)
            {
                Debug.LogWarning("[WSM3D] No foliage shader found; disabling foliage renderer.");
                return false;
            }
            _material = new Material(s) { name = "WSM3D.Foliage.Placeholder", enableInstancing = true };
            return true;
        }

        public static Material? Get() => _material;

        public static void Reset()
        {
            if (_material != null) Object.Destroy(_material);
            _material = null;
            _attempted = false;
        }
    }
}
