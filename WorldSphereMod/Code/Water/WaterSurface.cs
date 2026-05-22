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
        static readonly Vector4 BaseWaveAmp = new Vector4(0.12f, 0.075f, 0.05f, 0f);
        static readonly Vector4 BaseWaveFreq = new Vector4(0.45f, 1.1f, 2.0f, 0f);
        static readonly Vector4 BaseWaveSpeed = new Vector4(1.0f, 1.6f, 2.4f, 0f);
        const float BobAmplitude = 0.05f;
        const float BobSpeed = 0.8f;

        static Material? _material;
        static bool _materialAttempted;

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
        readonly List<int> _trisScratch = new List<int>();
        readonly Dictionary<long, int> _cornerIndexScratch = new Dictionary<long, int>();

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

            var tiles = World.world.tiles_list;
            int tileCount = tiles.Length;
            int width = MapBox.width;
            int height = MapBox.height;
            float depthSum = 0f;
            int depthCount = 0;

            var vertices = _vertsScratch;
            var triangles = _trisScratch;
            var cornerIndex = _cornerIndexScratch;
            vertices.Clear();
            triangles.Clear();
            cornerIndex.Clear();
            float sea = WaterMaskBuffer.SeaLevel;

            // Vertex dedup at grid corners. cx is modulo width so the cylindrical X-wrap seam
            // collapses to one vertex per shared corner — eliminates the lighting stripe that
            // RecalculateNormals would otherwise produce from split duplicate verts.

            int GetCorner(int cx, int cy)
            {
                int wx = ((cx % width) + width) % width;
                long key = ((long)wx << 32) | (uint)cy;
                if (cornerIndex.TryGetValue(key, out int idx)) return idx;
                idx = vertices.Count;
                vertices.Add(Core.Sphere.SpherePos(cx, cy, sea));
                cornerIndex[key] = idx;
                return idx;
            }

            for (int i = 0; i < tileCount; i++)
            {
                WorldTile t = tiles[i];
                if (t == null) continue;
                if (!WaterMaskBuffer.IsWater(t.data.tile_id)) continue;
                depthSum += WaterMaskBuffer.DepthAt(t.data.tile_id);
                depthCount++;

                int x = t.x;
                int y = t.y;

                int i0 = GetCorner(x,     y);
                int i1 = GetCorner(x + 1, y);
                int i2 = GetCorner(x + 1, y + 1);
                int i3 = GetCorner(x,     y + 1);

                triangles.Add(i0); triangles.Add(i1); triangles.Add(i2);
                triangles.Add(i0); triangles.Add(i2); triangles.Add(i3);
            }

            _mesh.SetVertices(vertices);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            if (_instanceMaterial != null)
            {
                float avgDepth = depthCount > 0 ? depthSum / depthCount : 0f;
                _instanceMaterial.SetFloat("_WaterDepth", avgDepth);
            }
        }

        void LateUpdate()
        {
            _waveTime += Time.deltaTime;
            ApplyWaveProfile();
        }

        void ApplyWaveProfile()
        {
            float detail = Mathf.Clamp(Core.savedSettings.WaterDetail, 0f, 2f);
            float detail01 = detail * 0.5f;
            float ampScale = Mathf.Lerp(1.1f, 1.8f, detail01);
            float freqScale = Mathf.Lerp(0.95f, 1.1f, detail01);
            float speedScale = Mathf.Lerp(0.95f, 1.05f, detail01);

            // Keep the object itself in motion even when the shader falls back to a flat
            // material, so the surface still reads as water during runtime diagnostics.
            float bobScale = Mathf.Lerp(0.85f, 1.25f, detail01);
            float bob = Mathf.Sin(_waveTime * BobSpeed) * BobAmplitude * bobScale;
            transform.localPosition = _baseLocalPosition + new Vector3(0f, bob, 0f);

            // Write to the per-renderer instance material so we never mutate the shared template.
            if (_instanceMaterial == null) return;
            if (_instanceMaterial.HasProperty(WaveTimeId))
            {
                _instanceMaterial.SetFloat(WaveTimeId, _waveTime);
            }
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
        }

        static bool EnsureMaterial()
        {
            if (_material != null) return true;
            if (_materialAttempted) return false;
            _materialAttempted = true;

            Color waterTint = new Color(0.15f, 0.40f, 0.55f, 0.6f);
            int surfaceTypeId = Shader.PropertyToID("_Surface");
            int alphaClipId = Shader.PropertyToID("_AlphaClip");
            int baseColorId = Shader.PropertyToID("_BaseColor");
            int colorId = Shader.PropertyToID("_Color");
            int smoothnessId = Shader.PropertyToID("_Smoothness");
            int metallicId = Shader.PropertyToID("_Metallic");
            bool isLit = false;

            Shader? s = Resources.Load<Shader>("Shaders/ContinuumWaterGerstner");
            if (s == null) s = Resources.Load<Shader>("Shaders/WaterGerstner");
            if (s != null)
            {
                Material m = new Material(s) { name = "WSM3D.Water" };
                m.enableInstancing = true;
                if (m.enableInstancing)
                {
                    ConfigureWaterMaterial(m, waterTint, baseColorId, colorId, smoothnessId, metallicId, surfaceTypeId, alphaClipId);
                    _material = m;
                    Debug.Log($"[WSM3D] Water material resolved via '{s.name}' (transparent blue)");
                    return true;
                }
                Object.Destroy(m);
            }

            string[] candidates =
            {
                "Universal Render Pipeline/Lit",
                "Standard",
                "Universal Render Pipeline/Unlit",
            };
            foreach (var name in candidates)
            {
                s = Shader.Find(name);
                if (s == null) continue;
                Material m = new Material(s) { name = "WSM3D.Water" };
                m.enableInstancing = true;
                if (!m.enableInstancing)
                {
                    Object.Destroy(m);
                    continue;
                }
                isLit = name == "Universal Render Pipeline/Lit";
                ConfigureWaterMaterial(m, waterTint, baseColorId, colorId, smoothnessId, metallicId, surfaceTypeId, alphaClipId, isLit);
                _material = m;
                Debug.Log($"[WSM3D] Water material resolved via '{name}' (transparent blue)");
                return true;
            }
            if (_material == null)
            {
                Debug.LogWarning("[WSM3D] No water shader found; water disabled.");
                return false;
            }
            return true;
        }

        static void ConfigureWaterMaterial(Material material, Color waterTint,
            int baseColorId, int colorId, int smoothnessId, int metallicId, int surfaceTypeId, int alphaClipId, bool isUrpLit = false)
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

            // Force emission to the water tint -- Standard shader is lit; in this
            // scene the water layer receives ~0 directional/ambient light so the lit
            // base color computes to ~0 -> opaque black mesh. Adding emission ==
            // waterTint guarantees pixels light up regardless of scene lighting.
            try
            {
                material.EnableKeyword("_EMISSION");
                int emissionId = Shader.PropertyToID("_EmissionColor");
                if (material.HasProperty(emissionId))
                {
                    // Boost emission so water is visibly blue, not dim/black.
                    // waterTint = (0.15, 0.40, 0.55, 0.6). At 1.5x emission was
                    // (0.225, 0.60, 0.825) — still dark in zero-light scenes.
                    // Use a brighter ocean-blue baseline + 3x intensity so the
                    // water visibly self-emits even with no ambient/directional.
                    material.SetColor(emissionId, new Color(0.30f, 0.60f, 1.20f, 1f));
                }
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            catch { }

            // Opaque queue (was 3000 = Transparent). Standard shader fallback for water
            // renders OPAQUE BLACK at the Transparent queue without explicit blend mode
            // config -> the 'MeshWater creates blackworld' regression. Continuum/
            // WaterGerstner shaders handle their own alpha; built-in Standard does not.
            material.renderQueue = 2000;
        }
    }
}
