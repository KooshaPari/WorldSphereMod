using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Water
{
    public sealed class WaterSurface : MonoBehaviour
    {
        public static WaterSurface? Instance;

        static readonly int WaveTimeId = Shader.PropertyToID("_WaveTime");
        static readonly int WaveAmpId = Shader.PropertyToID("_WaveAmp");
        static readonly int WaveFreqId = Shader.PropertyToID("_WaveFreq");
        static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
        static readonly Vector4 BaseWaveAmp = new Vector4(0.04f, 0.025f, 0.015f, 0f);
        static readonly Vector4 BaseWaveFreq = new Vector4(0.45f, 1.1f, 2.0f, 0f);
        static readonly Vector4 BaseWaveSpeed = new Vector4(1.0f, 1.6f, 2.4f, 0f);
        static readonly int WaveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
        // Bob disabled: on a sphere, translating the GO in local-Y shifts the
        // mesh tangentially on the top face and radially on the sides, making it
        // "float 1 ft above" from most camera angles and only visible at edges.
        // Vertex-based wave displacement belongs in the shader (GerstnerWater).
        const float BobAmplitude = 0f;
        const float BobSpeed = 0.8f;

        static Material? _material;
        static bool _materialAttempted;
        static bool _emissionDiagnosticsLogged;

        MeshFilter? _filter;
        internal MeshRenderer? _renderer;
        Mesh? _mesh;
        Material? _instanceMaterial;   // per-renderer copy of _material; we own SetFloat on this
        Vector3 _baseLocalPosition;
        float _waveTime;

        // Reusable scratch buffers for RebuildMesh. Cleared instead of freshly allocated each
        // rebuild — RebuildMesh runs on world load and on every tile change, so the dropped
        // allocations add up across a long session.
        readonly List<Vector3> _vertsScratch = new List<Vector3>();
        readonly List<Color> _colorsScratch = new List<Color>();
        readonly List<int> _trisScratch = new List<int>();
        readonly Dictionary<long, int> _cornerIndexScratch = new Dictionary<long, int>();
        readonly Dictionary<long, (float depthSum, int count)> _cornerDepthScratch = new Dictionary<long, (float, int)>();

        public static WaterSurface? Create(Transform parent)
        {
            if (Instance != null) Destroy();
            if (!EnsureMaterial()) return null;

            var go = new GameObject("WorldSphere Water");
            go.transform.SetParent(parent, worldPositionStays: false);

            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (RenderSettings.skybox != null && RenderSettings.skybox.mainTexture is Cubemap skyCubemap)
            {
                renderer.sharedMaterial.SetTexture("_SkyCubemap", skyCubemap);
            }

            // Defensive MaterialPropertyBlock push, mirroring the slope-mesh fix.
            // Even though GerstnerWater's _Color/_DeepColor are plain uniforms (no
            // UNITY_DEFINE_INSTANCED_PROP), enableInstancing=true + a future
            // shader change to per-instance buffers would silently zero these and
            // render water black. Pushing through MPB stays correct in both modes.
            // High-contrast shallow/deep so depth gradient is unmistakable in-game.
            // Previous values (0.22/0.65/0.70 vs 0.08/0.22/0.45) blended into a flat
            // mid-blue at strategy-view altitude; user reported water as "flat blue".
            Color waterShallowColor = new Color(0.40f, 0.70f, 0.85f, 0.55f);
            Color waterDeepColor = new Color(0.01f, 0.05f, 0.14f, 0.96f);
            Color waterFoamColor = new Color(0.92f, 0.95f, 1.00f, 1f);
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", waterShallowColor);
            mpb.SetColor("_DeepColor", waterDeepColor);
            mpb.SetColor("_Foam", waterFoamColor);
            mpb.SetColor("_EmissionColor", new Color(0.05f, 0.1f, 0.15f, 1f));
            renderer.SetPropertyBlock(mpb);

            var surface = go.AddComponent<WaterSurface>();
            surface._filter = filter;
            surface._renderer = renderer;
            surface._mesh = new Mesh { name = "WorldSphere.Water" };
            surface._mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            filter.sharedMesh = surface._mesh;
            // Touch renderer.material once to materialize the per-instance copy. We use it for
            // per-frame shader parameter updates so we don't mutate the shared template asset.
            surface._instanceMaterial = renderer.material;
            surface._baseLocalPosition = go.transform.localPosition;
            surface.ApplyWaveProfile();
            surface.RebuildMesh();

            Instance = surface;
            return surface;
        }

        public static void Destroy()
        {
            if (Instance == null) return;
            var go = Instance.gameObject;
            if (Instance._mesh != null) Object.Destroy(Instance._mesh);
            if (Instance._instanceMaterial != null) Object.Destroy(Instance._instanceMaterial);
            Instance = null;
            if (go != null) Object.Destroy(go);
            // Destroy the shared template too so a subsequent Create reallocates against the
            // current Unity state — otherwise a world reload that invalidates the shader would
            // resurface a stale Material handle.
            if (_material != null) Object.Destroy(_material);
            _material = null;
            _materialAttempted = false;
        }

        public void RebuildMesh()
        {
            if (_mesh == null) return;
            _mesh.Clear();

            if (WaterMaskBuffer.Depths == null) return;

            WorldTile[] tiles = World.world.tiles_list;
            int tileCount = tiles.Length;
            int width = MapBox.width;
            int height = MapBox.height;
            float maxDepth = WaterMaskBuffer.MaxDepth();

            var vertices = _vertsScratch;
            var colors = _colorsScratch;
            var triangles = _trisScratch;
            var cornerIndex = _cornerIndexScratch;
            var cornerDepth = _cornerDepthScratch;
            vertices.Clear();
            colors.Clear();
            triangles.Clear();
            cornerIndex.Clear();
            cornerDepth.Clear();
            float sea = WaterMaskBuffer.SeaLevel;

            int GetCorner(int cx, int cy, float tileDepth)
            {
                int wx = ((cx % width) + width) % width;
                long key = ((long)wx << 32) | (uint)cy;
                if (cornerDepth.TryGetValue(key, out var prev))
                {
                    cornerDepth[key] = (prev.depthSum + tileDepth, prev.count + 1);
                }
                else
                {
                    cornerDepth[key] = (tileDepth, 1);
                }
                if (cornerIndex.TryGetValue(key, out int idx)) return idx;
                idx = vertices.Count;
                vertices.Add(Core.Sphere.SpherePos(cx, cy, sea));
                colors.Add(Color.black);
                cornerIndex[key] = idx;
                return idx;
            }

            for (int i = 0; i < tileCount; i++)
            {
                WorldTile t = tiles[i];
                if (t == null) continue;
                if (!WaterMaskBuffer.IsWater(t.data.tile_id)) continue;
                float depth = WaterMaskBuffer.DepthAt(t.data.tile_id);

                int x = t.x;
                int y = t.y;

                int i0 = GetCorner(x,     y,     depth);
                int i1 = GetCorner(x + 1, y,     depth);
                int i2 = GetCorner(x + 1, y + 1, depth);
                int i3 = GetCorner(x,     y + 1, depth);

                triangles.Add(i0); triangles.Add(i1); triangles.Add(i2);
                triangles.Add(i0); triangles.Add(i2); triangles.Add(i3);
            }

            float safeMax = maxDepth > 0.001f ? maxDepth : 1f;
            foreach (var kvp in cornerIndex)
            {
                long key = kvp.Key;
                int idx = kvp.Value;
                if (cornerDepth.TryGetValue(key, out var d))
                {
                    float avgDepth = d.depthSum / d.count;
                    float depthFrac = Mathf.Clamp01(avgDepth / safeMax);
                    colors[idx] = new Color(depthFrac, depthFrac, depthFrac, 1f);
                }
            }

            // Diagnose vertex color distribution (depth fraction stored in R channel).
            // If all R values are near 1.0, all water renders as _DeepColor (very dark).
            if (colors.Count > 0)
            {
                float minR = 1f, maxR = 0f, sumR = 0f;
                for (int ci = 0; ci < colors.Count; ci++)
                {
                    float r = colors[ci].r;
                    if (r < minR) minR = r;
                    if (r > maxR) maxR = r;
                    sumR += r;
                }
                Debug.Log($"[WSM3D] Water mesh: {vertices.Count} verts, {triangles.Count / 3} tris, " +
                    $"maxDepth={maxDepth:F2}, depthFrac R range=[{minR:F3}, {maxR:F3}] avg={sumR / colors.Count:F3}");
            }

            _mesh.SetVertices(vertices);
            _mesh.SetColors(colors);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            if (_instanceMaterial != null)
            {
                _instanceMaterial.SetFloat("_WaterDepth", maxDepth);
                _instanceMaterial.SetFloat("_MaxDepth", safeMax);
            }
        }

        void Update()
        {
            _waveTime = Time.time;
            ApplyWaveProfile();
        }

        void ApplyWaveProfile()
        {
            float detail = Mathf.Clamp(Core.savedSettings.WaterDetail, 0f, 2f);
            float detail01 = detail * 0.5f;
            float ampScale = Mathf.Lerp(1.1f, 1.8f, detail01);
            float freqScale = Mathf.Lerp(0.95f, 1.1f, detail01);
            float speedScale = Mathf.Lerp(0.95f, 1.05f, detail01);

            // Bob removed: on a sphere, shifting localPosition in Y moves vertices
            // tangentially, not radially. The mesh must stay at (0,0,0) so SpherePos
            // vertices land exactly on the sphere surface. Wave motion is the shader's job.
            transform.localPosition = _baseLocalPosition;

            // Write to the per-renderer instance material so we never mutate the shared template.
            if (_instanceMaterial == null) return;
            _instanceMaterial.SetFloat(WaveTimeId, _waveTime);
            if (_instanceMaterial.HasProperty(WaveAmpId))
            {
                _instanceMaterial.SetVector(WaveAmpId, BaseWaveAmp * ampScale);
            }
            if (_instanceMaterial.HasProperty(WaveFreqId))
            {
                _instanceMaterial.SetVector(WaveFreqId, BaseWaveFreq * freqScale);
            }
            if (_instanceMaterial.HasProperty(WaveSpeedId))
            {
                _instanceMaterial.SetVector(WaveSpeedId, BaseWaveSpeed * speedScale);
            }
            if (_instanceMaterial.HasProperty(WaveAmplitudeId))
            {
                // Visible Gerstner displacement: 0.05 was sub-pixel at strategy-view
                // altitude; 0.25 base puts crests at ~0.27-0.45 m which reads clearly.
                _instanceMaterial.SetFloat(WaveAmplitudeId, 0.25f * ampScale);
            }
        }

        static bool EnsureMaterial()
        {
            if (_material != null) return true;
            if (_materialAttempted) return false;
            _materialAttempted = true;

            Color waterTint = new Color(0.22f, 0.65f, 0.70f, 0.75f);
            int surfaceTypeId = Shader.PropertyToID("_Surface");
            int alphaClipId = Shader.PropertyToID("_AlphaClip");
            int baseColorId = Shader.PropertyToID("_BaseColor");
            int colorId = Shader.PropertyToID("_Color");
            int smoothnessId = Shader.PropertyToID("_Smoothness");
            int metallicId = Shader.PropertyToID("_Metallic");
            int emissionId = Shader.PropertyToID("_EmissionColor");

            Shader? s = null;
            // MeshWater should only resolve through the bundled GerstnerWater
            // shader now that the bundle fallback is fixed to Diffuse.
            const bool kGerstnerKnownBroken = false;
            if (!kGerstnerKnownBroken)
            {
                if (WorldSphereMod.Core.Sphere.LoadedShaders.TryGetValue("GerstnerWater", out var bundledWater) && bundledWater != null)
                {
                    s = bundledWater;
                    Debug.Log("[WSM3D] Water material resolved via Core.Sphere.LoadedShaders cache.");
                }
                if (s == null)
                {
                    s = Shader.Find("WSM3D/GerstnerWater");
                    if (s != null) Debug.Log("[WSM3D] Water material resolved via Shader.Find('WSM3D/GerstnerWater').");
                }
            }

            if (s == null)
            {
                Debug.LogWarning("[WSM3D] No bundled GerstnerWater shader found; water disabled.");
                return false;
            }

            // Detect shader fallback: if GerstnerWater's main pass failed to
            // compile, Unity silently activates the Fallback "Diffuse" shader.
            // Diffuse is LIT and renders black in WorldBox's unlit scene.
            // The passCount heuristic: GerstnerWater has 1 pass, Diffuse has 2+.
            Debug.Log($"[WSM3D] Water shader: name='{s.name}' supported={s.isSupported} passCount={s.passCount}");
            if (s.passCount == 0)
            {
                Debug.LogError("[WSM3D] GerstnerWater shader has 0 passes — bundle asset is corrupted. Water disabled.");
                return false;
            }

            Material m = new Material(s) { name = "WSM3D.Water" };
            m.enableInstancing = true;
            // GerstnerWater may not have #pragma multi_compile_instancing;
            // instancing is nice-to-have, not load-bearing.  Always configure
            // the material so water is visible.
            ConfigureWaterMaterial(m, waterTint, baseColorId, colorId, smoothnessId, metallicId, surfaceTypeId, alphaClipId, emissionId);

            // Diagnostic: dump all material color properties to catch
            // mis-set tints that produce black output.
            Debug.Log($"[WSM3D] Water material final: shader='{m.shader.name}' " +
                $"_Color={(m.HasProperty(colorId) ? m.GetColor(colorId).ToString() : "N/A")} " +
                $"_DeepColor={(m.HasProperty("_DeepColor") ? m.GetColor("_DeepColor").ToString() : "N/A")} " +
                $"renderQueue={m.renderQueue}");

            _material = m;
            Debug.Log($"[WSM3D] Water material resolved via '{s.name}' (bundled transparent blue, instancing={m.enableInstancing})");
            return true;
        }

        static void ConfigureWaterMaterial(Material material, Color waterTint,
            int baseColorId, int colorId, int smoothnessId, int metallicId, int surfaceTypeId, int alphaClipId, int emissionId, bool isUrpLit = false, string shaderName = "")
        {
            if (material.HasProperty(baseColorId))
            {
                material.SetColor(baseColorId, waterTint);
            }
            else if (material.HasProperty(colorId))
            {
                material.SetColor(colorId, waterTint);
            }
            else
            {
                material.color = waterTint;
            }

            if (material.HasProperty(metallicId))
            {
                material.SetFloat(metallicId, 0.0f);
            }

            // Explicitly set _DeepColor so deep water is a visible dark-blue
            // instead of the shader default (0.04, 0.12, 0.30) which looks
            // almost black in unlit WorldBox scenes.
            int deepColorId = Shader.PropertyToID("_DeepColor");
            if (material.HasProperty(deepColorId))
            {
                // Near-black deep blue; combined with shallow (0.40,0.70,0.85) this
                // produces a visible shoreline-to-deepwater gradient on the sphere.
                material.SetColor(deepColorId, new Color(0.05f, 0.15f, 0.25f, 0.92f));
            }

            if (isUrpLit)
            {
                if (material.HasProperty(smoothnessId))
                {
                    material.SetFloat(smoothnessId, 0.85f);
                }
                if (material.HasProperty(surfaceTypeId))
                {
                    material.SetFloat(surfaceTypeId, 1f);
                }
                if (material.HasProperty(alphaClipId))
                {
                    material.SetFloat(alphaClipId, 0f);
                }
            }
            else if (shaderName == "Standard")
            {
                SetStandardTransparentMode(material);
            }
            else
            {
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 1);
                material.renderQueue = 3000;
            }

            // Keep the fallback readable even if the scene has almost no lighting.
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty(emissionId))
            {
                material.SetColor(emissionId, new Color(0.08f, 0.15f, 0.25f, 1f));
            }

            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (!_emissionDiagnosticsLogged)
            {
                _emissionDiagnosticsLogged = true;
                Color emissionColor = material.HasProperty(emissionId) ? material.GetColor(emissionId) : default;
                Debug.Log(
                    "[WSM3D] Water emission setup: _EmissionColor=" + FormatColor(emissionColor) +
                    " _EMISSION=" + material.IsKeywordEnabled("_EMISSION") +
                    " GI=" + material.globalIlluminationFlags);
            }
        }

        static string FormatColor(Color color)
        {
            return $"({color.r:0.###}, {color.g:0.###}, {color.b:0.###}, {color.a:0.###})";
        }

        static void SetStandardTransparentMode(Material material)
        {
            // MeshWater creates blackworld when the Standard fallback uses Transparent queue:
            // alpha-blended surfaces with waterTint alpha 0.55 and no real scene lighting
            // blend toward black. Use opaque mode so the emission self-illumination dominates.
            // _Mode=0 is Standard shader's Opaque mode — _Mode=3 (Transparent) was wrong here
            // because it changes which shader passes are active, even when blend/queue are
            // overridden to opaque values; the result is invisible geometry.
            material.SetFloat("_Mode", 0f);
            material.SetOverrideTag("RenderType", "Opaque");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 2000;
        }
    }
}
