using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// GPU renderer for voxel/procgen meshes. Buckets submissions by
    /// (mesh, material) and flushes them via <see cref="Graphics.DrawMeshInstanced"/> by default,
    /// with automatic fallback to per-instance <see cref="Graphics.DrawMesh"/> if
    /// the build does not expose instancing shader variants.
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
            public readonly List<Vector4>   Colors = new List<Vector4>(1024);
            public MaterialPropertyBlock Block = new MaterialPropertyBlock();
            // Scratch buffers reused across frames; grown (never shrunk) to current batch
            // size for DrawMeshInstanced fast-path arrays.
            public Matrix4x4[] MatScratch = new Matrix4x4[kBatch];
            public Vector4[] ColScratch = new Vector4[kBatch];
        }

        static readonly Dictionary<Key, Bucket> _buckets = new Dictionary<Key, Bucket>(128);
        static readonly int _colorProp = Shader.PropertyToID("_InstanceColor");
        static readonly int _baseColorProp = Shader.PropertyToID("_BaseColor");
        static readonly int _colorPropUnlit = Shader.PropertyToID("_Color");
        const int kBatch = 1023;

        public static long FrameDrawCalls;
        public static long FrameInstances;
        public static bool UseFallbackPath => _useFallbackPath;
        public static bool InstancingBroken => _instancingErrorLogged;

        static bool _instancingErrorLogged;
        static bool _useFallbackPath;

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
                if (_useFallbackPath)
                {
                    DrawFallbackPath(kv.Key, bucket, total, layer);
                    bucket.Matrices.Clear();
                    bucket.Colors.Clear();
                    continue;
                }

                int offset = 0;
                while (offset < total)
                {
                    int n = Mathf.Min(kBatch, total - offset);
                    if (bucket.MatScratch.Length < n)
                    {
                        bucket.MatScratch = new Matrix4x4[n];
                        bucket.ColScratch = new Vector4[n];
                    }
                    bucket.Matrices.CopyTo(offset, bucket.MatScratch, 0, n);
                    bucket.Colors.CopyTo(offset, bucket.ColScratch, 0, n);
                    bucket.Block.Clear();
                    bucket.Block.SetVectorArray(_colorProp, bucket.ColScratch);
                    try
                    {
                        Graphics.DrawMeshInstanced(
                            kv.Key.Mesh, 0, kv.Key.Material,
                            bucket.MatScratch, n, bucket.Block,
                            shadows, receive, layer);
                        FrameDrawCalls++;
                        offset += n;
                    }
                    catch (System.InvalidOperationException)
                    {
                        if (!_instancingErrorLogged)
                        {
                            _instancingErrorLogged = true;
                            string matName = kv.Key.Material != null ? kv.Key.Material.shader.name : "<null>";
                            Debug.LogError($"[WSM3D] DrawMeshInstanced rejected material; falling back to per-instance Graphics.DrawMesh. Voxel render perf is degraded but visible.");
                        }

                        _useFallbackPath = true;
                        DrawFallbackPath(kv.Key, bucket, total, layer, offset);
                        break;
                    }
                }

                bucket.Matrices.Clear();
                bucket.Colors.Clear();
            }
        }

        static void DrawFallbackPath(Key key, Bucket bucket, int total, int layer, int start = 0)
        {
            int end = Mathf.Min(bucket.Matrices.Count, start + total);
            for (int i = start; i < end; i++)
            {
                bucket.Block.Clear();
                Vector4 tint = bucket.Colors[i];
                bucket.Block.SetVector(_colorProp, tint);
                bucket.Block.SetColor(_baseColorProp, tint);
                bucket.Block.SetColor(_colorPropUnlit, tint);
                Graphics.DrawMesh(key.Mesh, bucket.Matrices[i], key.Material, layer, null, 0, bucket.Block);
                FrameDrawCalls++;
            }
        }

        public static void Reset()
        {
            _buckets.Clear();
            _useFallbackPath = false;
            _instancingErrorLogged = false;
            FrameDrawCalls = 0;
            FrameInstances = 0;
        }
    }
}
