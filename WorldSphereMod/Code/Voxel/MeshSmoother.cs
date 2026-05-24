using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// ADR-0008 skeleton for optional post-mesh Laplacian smoothing.
    /// </summary>
    public static class MeshSmoother
    {
        public static Mesh Smooth(Mesh input, int iterations = 1, float lambda = 0.5f)
        {
            if (!Core.savedSettings.VoxelMeshSmoothing) return input;

            if (input == null) return input;

            var output = Object.Instantiate(input);
            output.name = input.name + ":smoothed";

            Vector3[] vertices = output.vertices;
            int[] indices = output.triangles;
            if (vertices.Length == 0 || indices.Length < 3)
            {
                return output;
            }

            var neighbors = new HashSet<int>[vertices.Length];
            for (int i = 0; i < neighbors.Length; i++)
            {
                neighbors[i] = new HashSet<int>();
            }

            var edgeUse = new Dictionary<EdgeKey, int>();
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int a = indices[i];
                int b = indices[i + 1];
                int c = indices[i + 2];

                if (!IsValidIndex(a, vertices.Length) ||
                    !IsValidIndex(b, vertices.Length) ||
                    !IsValidIndex(c, vertices.Length))
                {
                    continue;
                }

                AddNeighbor(neighbors, a, b);
                AddNeighbor(neighbors, b, c);
                AddNeighbor(neighbors, c, a);

                AddEdge(edgeUse, a, b);
                AddEdge(edgeUse, b, c);
                AddEdge(edgeUse, c, a);
            }

            bool[] boundary = new bool[vertices.Length];
            foreach (KeyValuePair<EdgeKey, int> kv in edgeUse)
            {
                if (kv.Value == 1)
                {
                    boundary[kv.Key.A] = true;
                    boundary[kv.Key.B] = true;
                }
            }

            int passCount = Mathf.Max(0, iterations);
            float weight = Mathf.Clamp01(lambda);
            for (int pass = 0; pass < passCount; pass++)
            {
                Vector3[] next = (Vector3[])vertices.Clone();
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (boundary[i] || neighbors[i].Count == 0) continue;

                    Vector3 average = Vector3.zero;
                    foreach (int neighbor in neighbors[i])
                    {
                        average += vertices[neighbor];
                    }
                    average /= neighbors[i].Count;
                    next[i] = Vector3.Lerp(vertices[i], average, weight);
                }
                vertices = next;
            }

            output.vertices = vertices;
            output.RecalculateNormals();
            output.RecalculateBounds();
            return output;
        }

        static bool IsValidIndex(int index, int vertexCount)
        {
            return index >= 0 && index < vertexCount;
        }

        static void AddNeighbor(HashSet<int>[] neighbors, int a, int b)
        {
            if (a == b) return;
            neighbors[a].Add(b);
            neighbors[b].Add(a);
        }

        static void AddEdge(Dictionary<EdgeKey, int> edgeUse, int a, int b)
        {
            var key = new EdgeKey(a, b);
            int count;
            edgeUse.TryGetValue(key, out count);
            edgeUse[key] = count + 1;
        }

        struct EdgeKey
        {
            public readonly int A;
            public readonly int B;

            public EdgeKey(int a, int b)
            {
                if (a < b)
                {
                    A = a;
                    B = b;
                }
                else
                {
                    A = b;
                    B = a;
                }
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey other && A == other.A && B == other.B;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (A * 397) ^ B;
                }
            }
        }
    }
}
