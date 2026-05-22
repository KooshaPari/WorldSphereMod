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
            // Belt+suspenders config matching VoxelRender.EnsureMaterial (commits
            // 426b8e8 white-MainTex + cb6852d emission 1.5). Without these, the
            // Sprites/Default fallback renders foliage voxels PITCH BLACK because
            // _MainTex is empty (alpha=0 → alpha-test discards) AND _EmissionColor
            // is black (Standard lit shader gets no scene light = black).
            try
            {
                _material.SetTexture("_MainTex", Texture2D.whiteTexture);
                _material.SetColor("_Color", Color.white);
                _material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                _material.SetColor("_BaseColor", Color.white);
                _material.DisableKeyword("_ALPHATEST_ON");
                _material.SetFloat("_Cutoff", 0f);
                _material.EnableKeyword("_EMISSION");
                _material.SetColor("_EmissionColor", new Color(1.5f, 1.5f, 1.5f, 1f));
                _material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                _material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1;
            }
            catch { }
            Debug.Log($"[WSM3D] Foliage material resolved via '{s.name}' with white+EMISSION belt-suspenders.");
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
