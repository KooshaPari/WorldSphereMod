using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.LOD
{
    public static class ImpostorBillboard
    {
        static readonly Dictionary<int, Mesh> _atlas = new Dictionary<int, Mesh>();
        static long _hits;
        static long _misses;

        /// <summary>Cumulative cache-hit count since process start (or last Clear).</summary>
        public static long HitCount => System.Threading.Interlocked.Read(ref _hits);
        /// <summary>Cumulative cache-miss count since process start (or last Clear).</summary>
        public static long MissCount => System.Threading.Interlocked.Read(ref _misses);
        static Material? _material;
        static bool _materialAttempted;
        static bool _materialDebugLogged;

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
                LogImpostorMaterialPassDetails(_material, shaderName);
                return _material;
            }

            Debug.LogWarning("[WSM3D] ImpostorBillboard material resolution failed; impostor rendering will stay sprite-only.");
            return null;
        }

        static void LogImpostorMaterialPassDetails(Material material, string shaderName)
        {
            if (_materialDebugLogged) return;
            _materialDebugLogged = true;

            if (material == null)
            {
                Debug.LogWarning("[WSM3D] Impostor material diagnostics skipped: material is null.");
                return;
            }

            string shaderNameSafe = material.shader != null ? material.shader.name : "<null shader>";
            string keywords = material.shaderKeywords != null && material.shaderKeywords.Length > 0
                ? string.Join(", ", material.shaderKeywords)
                : "<none>";

            Debug.Log($"[WSM3D][MATERIAL] IMPOSTOR sourceCandidate='{shaderName}' resolvedShader='{shaderNameSafe}' passCount={material.passCount} renderQueue={material.renderQueue} renderType={material.GetTag("RenderType", false, "<none>")} queueOverride={material.GetTag("Queue", false, "<none>")}");
            Debug.Log($"[WSM3D][MATERIAL] IMPOSTOR shaderKeywords=[{keywords}]");

            for (int pass = 0; pass < material.passCount; pass++)
            {
                string passName = material.GetPassName(pass);
                Debug.Log($"[WSM3D][MATERIAL] IMPOSTOR pass[{pass}] name='{passName}'");
            }
        }

        public static Mesh? GetOrCreate(Sprite sprite)
        {
            if (sprite == null) return null;
            int key = sprite.GetInstanceID();
            if (_atlas.TryGetValue(key, out var m) && m != null)
            {
                System.Threading.Interlocked.Increment(ref _hits);
                return m;
            }
            System.Threading.Interlocked.Increment(ref _misses);
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
            System.Threading.Interlocked.Exchange(ref _hits, 0);
            System.Threading.Interlocked.Exchange(ref _misses, 0);
        }

        public static void Reset()
        {
            if (_material != null) Object.Destroy(_material);
            _material = null;
            _materialAttempted = false;
            _materialDebugLogged = false;
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
