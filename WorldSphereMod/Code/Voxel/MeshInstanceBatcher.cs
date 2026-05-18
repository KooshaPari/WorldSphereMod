using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// GPU-instanced renderer for voxel/procgen meshes. Buckets submissions by
    /// (mesh, material) and flushes them via <see cref="Graphics.DrawMeshInstanced"/> —
    /// 1023 instances per call. The mod's compute-shader/instancing gate in
    /// <c>Mod.OnLoad</c> guarantees the GPU supports this path.
    ///
    /// Usage:
    /// <code>
    ///   MeshInstanceBatcher.Submit(mesh, material, matrix, color);
    ///   ...
    ///   MeshInstanceBatcher.Flush();   // call once per frame after render data is built
    /// </code>
    /// </summary>
    public static class MeshInstanceBatcher
    {
        struct Key
        {
            public Mesh Mesh;
            public Material Material;
            public override int GetHashCode()
            {
                int m = Mesh != null ? Mesh.GetInstanceID() : 0;
                int x = Material != null ? Material.GetInstanceID() : 0;
                return m * 397 ^ x;
            }
            public override bool Equals(object obj)
            {
                if (obj is Key k) return k.Mesh == Mesh && k.Material == Material;
                return false;
            }
        }

        class Bucket
        {
            public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>(1024);
            public readonly List<Vector4>   Colors   = new List<Vector4>(1024);
            public MaterialPropertyBlock    Block    = new MaterialPropertyBlock();
            // Scratch buffers reused across frames; grown (never shrunk) to current batch
            // size so DrawMeshInstanced gets a tight-fitting array without per-frame allocation.
            public Matrix4x4[] MatScratch = new Matrix4x4[kBatch];
            public Vector4[]   ColScratch = new Vector4[kBatch];
        }

        static readonly Dictionary<Key, Bucket> _buckets = new Dictionary<Key, Bucket>(128);
        static readonly int _colorProp = Shader.PropertyToID("_InstanceColor");
        const int kBatch = 1023;

        public static long FrameDrawCalls;
        public static long FrameInstances;

        public static void Submit(Mesh mesh, Material mat, Matrix4x4 matrix, Color tint)
        {
            if (mesh == null || mat == null) return;
            var k = new Key { Mesh = mesh, Material = mat };
            if (!_buckets.TryGetValue(k, out var b))
            {
                b = new Bucket();
                _buckets[k] = b;
            }
            b.Matrices.Add(matrix);
            b.Colors.Add(tint);
        }

        public static void Flush(int layer = 0, ShadowCastingMode shadows = ShadowCastingMode.On, bool receive = true)
        {
            FrameDrawCalls = 0;
            FrameInstances = 0;

            foreach (var kv in _buckets)
            {
                var bucket = kv.Value;
                int total = bucket.Matrices.Count;
                FrameInstances += total;
                int offset = 0;
                while (offset < total)
                {
                    int n = Mathf.Min(kBatch, total - offset);
                    // Reuse per-bucket scratch buffers; grow on demand so allocations only
                    // occur when the per-bucket high-water mark increases (typically once).
                    if (bucket.MatScratch.Length < n)
                    {
                        bucket.MatScratch = new Matrix4x4[n];
                        bucket.ColScratch = new Vector4[n];
                    }
                    bucket.Matrices.CopyTo(offset, bucket.MatScratch, 0, n);
                    bucket.Colors.CopyTo(offset, bucket.ColScratch, 0, n);
                    bucket.Block.Clear();
                    bucket.Block.SetVectorArray(_colorProp, bucket.ColScratch);
                    Graphics.DrawMeshInstanced(
                        kv.Key.Mesh, 0, kv.Key.Material,
                        bucket.MatScratch, n, bucket.Block,
                        shadows, receive, layer);
                    FrameDrawCalls++;
                    offset += n;
                }
                bucket.Matrices.Clear();
                bucket.Colors.Clear();
            }
        }

        public static void Reset()
        {
            _buckets.Clear();
            FrameDrawCalls = 0;
            FrameInstances = 0;
        }
    }
}
