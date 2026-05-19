using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.LOD
{
    public static class ImpostorBillboard
    {
        static readonly Dictionary<int, Mesh> _atlas = new Dictionary<int, Mesh>();
        static Material? _material;
        static bool _materialAttempted;

        public static Material? GetMaterial()
        {
            if (_material != null)
            {
                // Keep the same material instance for all impostors.
                if (MeshInstanceBatcher.UseFallbackPath && _material.enableInstancing)
                {
                    _material.enableInstancing = false;
                }
                return _material;
            }
            if (_materialAttempted) return null;
            _materialAttempted = true;

            string[] candidates =
            {
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Particles/Unlit",
                "Standard",
                "Sprites/Default",
            };

            foreach (var shaderName in candidates)
            {
                Shader? s = Shader.Find(shaderName);
                if (s == null) continue;
                var mat = new Material(s) { name = "WSM3D.Impostor" };
                mat.enableInstancing = true;
                if (!mat.enableInstancing)
                {
                    Object.Destroy(mat);
                    continue;
                }

                _material = mat;
                return _material;
            }

            Debug.LogWarning("[WSM3D] ImpostorBillboard material resolution failed; impostor rendering will stay sprite-only.");
            return null;
        }

        public static Mesh? GetOrCreate(Sprite sprite)
        {
            if (sprite == null) return null;
            int key = sprite.GetInstanceID();
            if (_atlas.TryGetValue(key, out var m) && m != null) return m;
            m = BuildQuad(sprite);
            m.RecalculateBounds();
            _atlas[key] = m;
            return m;
        }

        public static int Count => _atlas.Count;

        public static void Clear()
        {
            foreach (var m in _atlas.Values) if (m != null) Object.Destroy(m);
            _atlas.Clear();
        }

        public static void Reset()
        {
            if (_material != null) Object.Destroy(_material);
            _material = null;
            _materialAttempted = false;
        }

        static Mesh BuildQuad(Sprite sprite)
        {
            float w = sprite.rect.width / sprite.pixelsPerUnit;
            float h = sprite.rect.height / sprite.pixelsPerUnit;
            float hx = w * 0.5f;
            var verts = new List<Vector3>
            {
                new Vector3(-hx, 0, 0), new Vector3(hx, 0, 0),
                new Vector3(hx, h, 0), new Vector3(-hx, h, 0),
            };
            Vector2[] uvs = sprite.uv;
            if (uvs == null || uvs.Length != 4)
            {
                uvs = new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            }
            var mesh = new Mesh { name = "WSM3D.Impostor." + sprite.name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, new List<Vector2>(uvs));
            mesh.SetTriangles(new int[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
