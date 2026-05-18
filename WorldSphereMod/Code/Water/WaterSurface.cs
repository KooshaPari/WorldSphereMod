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
        MeshRenderer? _renderer;
        Mesh? _mesh;
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
            surface.RebuildMesh();

            Instance = surface;
            return surface;
        }

        public static void Destroy()
        {
            if (Instance == null) return;
            var go = Instance.gameObject;
            if (Instance._mesh != null) Object.Destroy(Instance._mesh);
            Instance = null;
            if (go != null) Object.Destroy(go);
        }

        public void RebuildMesh()
        {
            if (_mesh == null) return;
            _mesh.Clear();

            if (WaterMaskBuffer.Depths == null) return;

            var tiles = World.world.tiles_list;
            int tileCount = tiles.Length;

            var vertices = new List<Vector3>(tileCount);
            var triangles = new List<int>(tileCount * 6);
            float sea = WaterMaskBuffer.SeaLevel;

            for (int i = 0; i < tileCount; i++)
            {
                WorldTile t = tiles[i];
                if (t == null) continue;
                if (!WaterMaskBuffer.IsWater(t.data.tile_id)) continue;

                int x = t.x;
                int y = t.y;

                // CCW from above when viewed from +height: (x,y) -> (x+1,y) -> (x+1,y+1) -> (x,y+1).
                Vector3 v0 = Core.Sphere.SpherePos(x,     y,     sea);
                Vector3 v1 = Core.Sphere.SpherePos(x + 1, y,     sea);
                Vector3 v2 = Core.Sphere.SpherePos(x + 1, y + 1, sea);
                Vector3 v3 = Core.Sphere.SpherePos(x,     y + 1, sea);

                int baseIdx = vertices.Count;
                vertices.Add(v0);
                vertices.Add(v1);
                vertices.Add(v2);
                vertices.Add(v3);

                triangles.Add(baseIdx + 0);
                triangles.Add(baseIdx + 1);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx + 0);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx + 3);
            }

            _mesh.SetVertices(vertices);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        void LateUpdate()
        {
            _waveTime += Time.deltaTime;
            if (_renderer != null && _renderer.sharedMaterial != null)
            {
                _renderer.sharedMaterial.SetFloat("_WaveTime", _waveTime);
            }
        }

        static bool EnsureMaterial()
        {
            if (_material != null) return true;
            if (_materialAttempted) return false;
            _materialAttempted = true;

            // Placeholder shader fallback chain — Step 4 replaces this with WaterGerstner.shader.
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
                Debug.LogWarning("[WorldSphereMod3D] No water shader found; water disabled.");
                return false;
            }
            _material = new Material(s) { name = "WSM3D.Water.Placeholder" };
            _material.color = Color.white;
            return true;
        }
    }
}
