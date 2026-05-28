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

            // FoliageWind isn't currently in the bundled-shaders list; check
            // cache for completeness then fall back. Worth adding to the bake
            // pass in Tools/Unity-Bake-Project/Assets/Editor/BakeShaders.cs.
            // PRIORITY: OpaqueVertexColor (bundle) BEFORE Sprites/Default. The
            // sprite fallback is alpha-blended + lit-by-no-lights → renders
            // either pitch-black or emissive-white (when emission is forced on
            // as belt-suspenders), neither of which honors the per-instance
            // tint passed via Submit's color arg. OpaqueVertexColor multiplies
            // vertex.color × _Color so per-instance MaterialPropertyBlock tints
            // (from sampled sprite average) come through as the actual foliage
            // color.
            Shader? s = null;
            if (WorldSphereMod.Core.Sphere.LoadedShaders.TryGetValue("FoliageWind", out var bundledFoliage) && bundledFoliage != null)
            {
                s = bundledFoliage;
                Debug.Log("[WSM3D] Foliage material resolved via Core.Sphere.LoadedShaders cache (FoliageWind).");
            }
            if (s == null)
            {
                s = Shader.Find("WSM3D/FoliageWind");
                if (s != null) Debug.Log("[WSM3D] Foliage material resolved via Shader.Find('WSM3D/FoliageWind').");
            }
            if (s == null) s = Resources.Load<Shader>("Shaders/FoliageWind");
            // OpaqueVertexColor from bundle — opaque, vertex-color aware, no
            // emissive-white blowout.
            if (s == null && WorldSphereMod.Core.Sphere.LoadedShaders.TryGetValue("OpaqueVertexColor", out var bundledOVC) && bundledOVC != null)
            {
                s = bundledOVC;
                Debug.Log("[WSM3D] Foliage material resolved via Core.Sphere.LoadedShaders cache (OpaqueVertexColor fallback).");
            }
            if (s == null)
            {
                s = Shader.Find("WSM3D/OpaqueVertexColor");
                if (s != null) Debug.Log("[WSM3D] Foliage material resolved via Shader.Find('WSM3D/OpaqueVertexColor').");
            }
            // Last-resort fallback — Sprites/Default still gets us pixels on
            // screen even if emissive-white. Keep this AFTER OpaqueVertexColor
            // so the bundle path wins when available.
            if (s == null) s = Shader.Find("Sprites/Default");
            if (s == null)
            {
                Debug.LogWarning("[WSM3D] No foliage shader found; disabling foliage renderer.");
                return false;
            }
            bool isOpaqueVertexColor = s.name != null && s.name.IndexOf("OpaqueVertexColor", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool isFoliageWind = s.name != null && s.name.IndexOf("FoliageWind", System.StringComparison.OrdinalIgnoreCase) >= 0;
            _material = new Material(s) { name = "WSM3D.Foliage.Placeholder", enableInstancing = true };
            try
            {
                _material.SetTexture("_MainTex", Texture2D.whiteTexture);
                _material.SetColor("_Color", Color.white);
                _material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                _material.SetColor("_BaseColor", Color.white);
                _material.DisableKeyword("_ALPHATEST_ON");
                _material.SetFloat("_Cutoff", 0f);
                _material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1;
                if (isOpaqueVertexColor || isFoliageWind)
                {
                    // Bundled shaders are opaque + vertex-color aware. NO emission
                    // boost — the per-instance tint (sampled sprite color) carried
                    // via MeshInstanceBatcher.Submit's color arg + the mesh's per-
                    // vertex colors render correctly under scene lighting.
                    _material.DisableKeyword("_EMISSION");
                    _material.SetColor("_EmissionColor", Color.black);
                }
                else
                {
                    // Sprites/Default / Standard fallback — without emission boost
                    // these render pitch-black against the dark grass terrain.
                    // Kept here only as the last-resort path when neither
                    // FoliageWind nor OpaqueVertexColor is in the bundle.
                    _material.EnableKeyword("_EMISSION");
                    _material.SetColor("_EmissionColor", new Color(0.6f, 0.7f, 0.5f, 1f));
                    _material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }
            catch { }
            Debug.Log($"[WSM3D] Foliage material resolved via '{s.name}' (opaqueVertexColor={isOpaqueVertexColor}, foliageWind={isFoliageWind}).");
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
