using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Water
{
    public sealed class WaterSurface : MonoBehaviour
    {
        public static WaterSurface? Instance;

        static Material? _material;
        static bool _materialAttempted;

        MeshFilter? _filter;
        internal MeshRenderer? _renderer;
        Mesh? _mesh;
        Material? _instanceMaterial;   // per-renderer copy of _material; we own SetFloat on this
        float _waveTime;

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

            var surface = go.AddComponent<WaterSurface>();
            surface._filter = filter;
            surface._renderer = renderer;
            surface._mesh = new Mesh { name = "WorldSphere.Water" };
            surface._mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            filter.sharedMesh = surface._mesh;
            // Touch renderer.material once to materialize the per-instance copy. We use it for
            // SetFloat in LateUpdate so we don't mutate the shared template asset.
            surface._instanceMaterial = renderer.material;
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

            var vertices = new List<Vector3>(tileCount);
            var triangles = new List<int>(tileCount * 6);
            float sea = WaterMaskBuffer.SeaLevel;

            // Vertex dedup at grid corners. cx is modulo width so the cylindrical X-wrap seam
            // collapses to one vertex per shared corner — eliminates the lighting stripe that
            // RecalculateNormals would otherwise produce from split duplicate verts.
            var cornerIndex = new Dictionary<long, int>(tileCount * 4);

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
        }

        void LateUpdate()
        {
            _waveTime += Time.deltaTime;
            // Write to the per-renderer instance material so we never mutate the shared template.
            if (_instanceMaterial != null)
            {
                _instanceMaterial.SetFloat("_WaveTime", _waveTime);
            }
        }

        static bool EnsureMaterial()
        {
            if (_material != null) return true;
            if (_materialAttempted) return false;
            _materialAttempted = true;

            // Prefer the WaterGerstner shader shipped under Resources/Shaders/. When the
            // AssetBundle ships the lit/baked version (Phase 5b), this Resources path still
            // works as a fallback because the shader source file resolves the same name.
            Shader? s = Resources.Load<Shader>("Shaders/WaterGerstner");
            if (s == null)
            {
                string[] candidates =
                {
                    "Universal Render Pipeline/Particles/Unlit",
                    "Particles/Standard Unlit",
                    "Sprites/Default",
                    "Hidden/Internal-Colored",
                };
                foreach (var name in candidates)
                {
                    s = Shader.Find(name);
                    if (s != null) break;
                }
            }
            if (s == null)
            {
                Debug.LogWarning("[WorldSphereMod3D] No water shader found; water disabled.");
                return false;
            }
            _material = new Material(s) { name = "WSM3D.Water" };
            _material.color = Color.white;
            return true;
        }
    }
}
