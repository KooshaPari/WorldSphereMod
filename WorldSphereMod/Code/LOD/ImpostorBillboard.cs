using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.LOD
{
    public static class ImpostorBillboard
    {
        static readonly Dictionary<int, Mesh> _atlas = new Dictionary<int, Mesh>();
        static Material? _material;

        public static Material? GetMaterial()
        {
            if (_material != null) return _material;
            Shader? s = Shader.Find("Sprites/Default");
            if (s == null) return null;
            _material = new Material(s) { name = "WSM3D.Impostor" };
            return _material;
        }

        public static Mesh? GetOrCreate(Sprite sprite)
        {
            if (sprite == null) return null;
            int key = sprite.GetInstanceID();
            if (_atlas.TryGetValue(key, out var m)) return m;
            m = BuildQuad(sprite);
            _atlas[key] = m;
            return m;
        }

        public static int Count => _atlas.Count;

        public static void Clear()
        {
            foreach (var m in _atlas.Values) if (m != null) Object.Destroy(m);
            _atlas.Clear();
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
