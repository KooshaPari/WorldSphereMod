// Generated procedurally to avoid the AssetBundle.GetObject native crash
// that occurs when the worldsphere bundle's serialized Mesh data is
// incompatible with the running Unity version or the GPU.
// Source: golden-ratio icosphere, subdivisions=2, ~320 faces.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Assets
{
    /// <summary>
    /// Procedural compound sphere mesh data, baked at compile time.
    /// Replaces AssetBundle.GetObject<Mesh>("Assets/.../CompoundSphereMesh.asset").
    /// </summary>
    public static class CompoundSphereMeshData
    {
        // Icosahedron base vertices (golden ratio). 12 vertices.
        private static readonly Vector3[] baseVertices =
        {
            new Vector3(-1f, 1.618034f, 0f), new Vector3(1f, 1.618034f, 0f),
            new Vector3(-1f, -1.618034f, 0f), new Vector3(1f, -1.618034f, 0f),
            new Vector3(0f, -1f, 1.618034f), new Vector3(0f, 1f, 1.618034f),
            new Vector3(0f, -1f, -1.618034f), new Vector3(0f, 1f, -1.618034f),
            new Vector3(1.618034f, 0f, -1f), new Vector3(1.618034f, 0f, 1f),
            new Vector3(-1.618034f, 0f, -1f), new Vector3(-1.618034f, 0f, 1f)
        };

        // 20 faces of the icosahedron, CCW winding for outward normals.
        private static readonly int[] baseFaces =
        {
            0, 11, 5,  0, 5, 1,  0, 1, 7,  0, 7, 10,  0, 10, 11,
            1, 5, 9,  5, 11, 4,  11, 10, 2,  10, 7, 6,  7, 1, 8,
            3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
            4, 9, 5,  2, 4, 11,  6, 2, 10,  8, 6, 7,  9, 8, 1
        };

        /// Build the compound sphere mesh from procedural data.
        /// Returns a UnityEngine.Mesh that does not require AssetBundle.GetObject.
        /// Produces a smooth unit sphere (1280 triangles, ~642 vertices) suitable
        /// for rendering compound-sphere actors in WorldBox's 3D mode.
        /// </summary>
        public static Mesh BuildMesh()
        {
            Mesh mesh = new Mesh { name = "CompoundSphereMesh" };
            // 3 subdivisions: 20 -> 320 -> 1280 faces (642 vertices). Smooth sphere.
            int subdivisions = 3;
            var verts = new List<Vector3>(baseVertices);
            var tris = new List<int>(baseFaces);
            // Cache of midpoints keyed by edge tuple (smallerIndex, largerIndex).
            var midCache = new Dictionary<long, int>();
            for (int s = 0; s < subdivisions; s++)
            {
                var newTris = new List<int>(tris.Count * 4);
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Midpoint(a, b, verts, midCache);
                    int bc = Midpoint(b, c, verts, midCache);
                    int ca = Midpoint(c, a, verts, midCache);
                    newTris.Add(a); newTris.Add(ab); newTris.Add(ca);
                    newTris.Add(b); newTris.Add(bc); newTris.Add(ab);
                    newTris.Add(c); newTris.Add(ca); newTris.Add(bc);
                    newTris.Add(ab); newTris.Add(bc); newTris.Add(ca);
                }
                tris = newTris;
            }
            // Project every vertex to the unit sphere surface (normalized direction).
            // Keep the 3D sphere shape — do NOT flatten to XZ.
            for (int i = 0; i < verts.Count; i++)
            {
                verts[i] = verts[i].normalized;
            }
            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int Midpoint(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            Vector3 mid = (verts[a] + verts[b]) * 0.5f;
            idx = verts.Count;
            verts.Add(mid);
            cache[key] = idx;
            return idx;
        }
    }

    /// <summary>
    /// Procedural skybox material properties, baked at compile time.
    /// Replaces AssetBundle.GetObject<Material>("Assets/.../Skybox.mat").
    /// </summary>
    public static class SkyboxMaterialData
    {
        public static Color TopColor { get; } = new Color(0.45f, 0.65f, 0.95f, 1f);
        public static Color BottomColor { get; } = new Color(0.95f, 0.92f, 0.85f, 1f);
        public static float Exposure { get; } = 1.3f;
        public static string ShaderName { get; } = "Skybox/Procedural";
    }

    /// <summary>
    /// Procedural compound material properties, baked at compile time.
    /// Replaces AssetBundle.GetObject<Material>("Assets/.../CompoundSphereMaterial.mat").
    /// </summary>
    public static class CompoundSphereMaterialData
    {
        public static Color BaseColor { get; } = new Color(0.85f, 0.82f, 0.78f, 1f);
        public static float Metallic { get; } = 0.0f;
        public static float Smoothness { get; } = 0.4f;
        public static string ShaderName { get; } = "Standard";
    }
}