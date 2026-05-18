using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Rig
{
    /// <summary>
    /// LRU cache of skinned voxel meshes keyed by (<see cref="Sprite.GetInstanceID"/>, <see cref="RigType"/>).
    /// Mirrors the shape of <see cref="WorldSphereMod.Voxel.VoxelMeshCache"/>: lock-wrapped,
    /// deferred destroy for meshes still in flight, 10% LRU eviction at capacity.
    ///
    /// Phase 6 Step 1: stub. <see cref="GetOrBuild"/> always returns a fallback
    /// <see cref="SkinnedVoxelMesh"/> with <see cref="RigType.None"/>. Real segmentation
    /// (HumanoidRig.SegmentVoxels etc.) lands in Step 3.
    /// </summary>
    public static class RigCache
    {
        public static int Capacity = 2048;

        struct Entry
        {
            public SkinnedVoxelMesh Mesh;
            public ulong LastFrame;
        }

        static readonly object _lock = new object();
        static readonly Dictionary<long, Entry> _cache = new Dictionary<long, Entry>(512);
        static readonly Queue<Mesh> _pendingDestroy = new Queue<Mesh>();
        static ulong _frame;

        public static int Count
        {
            get { lock (_lock) return _cache.Count; }
        }

        public static SkinnedVoxelMesh GetOrBuild(Sprite sprite, RigType rigType)
        {
            if (sprite == null) return new SkinnedVoxelMesh { RigType = RigType.None };
            long key = ((long)sprite.GetInstanceID() << 8) | (byte)rigType;
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var e))
                {
                    e.LastFrame = _frame;
                    _cache[key] = e;
                    return e.Mesh;
                }
            }

            SkinnedVoxelMesh built;
            if (rigType == RigType.Humanoid)
            {
                built = BuildHumanoid(sprite);
            }
            else
            {
                built = new SkinnedVoxelMesh
                {
                    BaseMesh = new Mesh { name = $"rig:stub:{sprite.name}" },
                    BoneIndices = System.Array.Empty<byte>(),
                    RigType = RigType.None,
                };
            }

            lock (_lock)
            {
                _cache[key] = new Entry { Mesh = built, LastFrame = _frame };
                if (_cache.Count > Capacity) Evict();
            }
            return built;
        }

        // Step 2: build a placeholder cube mesh + segmentation BoneId[]. The real per-voxel
        // skinned mesh (using the BoneId[] to drive vertex bone indices) is Step 3.
        static SkinnedVoxelMesh BuildHumanoid(Sprite sprite)
        {
            // Run segmentation against the sprite pixels when readable. The BoneId[] is
            // computed for its side-effect of validating the input pixel buffer; Step 3
            // consumes it to populate per-vertex bone indices once SpriteVoxelizer is
            // wired through this cache. For Step 2 the result is dropped after the call.
            if (sprite.texture != null && sprite.texture.isReadable)
            {
                Rect r = sprite.textureRect;
                int w = Mathf.Max(1, (int)r.width);
                int h = Mathf.Max(1, (int)r.height);
                int sx = (int)r.x;
                int sy = (int)r.y;
                Color32[] tex = sprite.texture.GetPixels32();
                int texW = sprite.texture.width;

                var sub = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    int dstRow = y * w;
                    int srcRow = (sy + y) * texW + sx;
                    for (int x = 0; x < w; x++)
                    {
                        sub[dstRow + x] = tex[srcRow + x];
                    }
                }
                _ = HumanoidRig.SegmentVoxels(w, h, sub);
            }

            var mesh = BuildPlaceholderCube($"rig:humanoid:{sprite.name}");
            var boneIndices = new byte[mesh.vertexCount];
            // All vertices placeholder-anchored to Root until Step 3 replaces with real per-voxel indices.
            for (int i = 0; i < boneIndices.Length; i++) boneIndices[i] = (byte)BoneId.Root;

            return new SkinnedVoxelMesh
            {
                BaseMesh = mesh,
                BoneIndices = boneIndices,
                RigType = RigType.Humanoid,
            };
        }

        static Mesh BuildPlaceholderCube(string name)
        {
            // 8 unique verts, unit cube centered at origin. 12 triangles, 36 indices.
            var verts = new Vector3[8]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f),
                new Vector3(-0.5f,  0.5f,  0.5f),
            };
            var tris = new int[]
            {
                0, 2, 1,  0, 3, 2, // -Z
                4, 5, 6,  4, 6, 7, // +Z
                0, 1, 5,  0, 5, 4, // -Y
                3, 7, 6,  3, 6, 2, // +Y
                0, 4, 7,  0, 7, 3, // -X
                1, 2, 6,  1, 6, 5, // +X
            };
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static void Tick()
        {
            lock (_lock) _frame++;
        }

        public static void Clear()
        {
            lock (_lock)
            {
                foreach (var e in _cache.Values)
                {
                    if (e.Mesh.BaseMesh != null) Object.Destroy(e.Mesh.BaseMesh);
                }
                _cache.Clear();
                _pendingDestroy.Clear();
            }
        }

        public static void DrainPendingDestroy()
        {
            lock (_lock)
            {
                while (_pendingDestroy.Count > 0)
                {
                    var m = _pendingDestroy.Dequeue();
                    if (m != null) Object.Destroy(m);
                }
            }
        }

        static void Evict()
        {
            int toDrop = Mathf.Max(1, _cache.Count / 10);
            var sorted = new List<KeyValuePair<long, Entry>>(_cache);
            sorted.Sort((a, b) => a.Value.LastFrame.CompareTo(b.Value.LastFrame));
            for (int i = 0; i < toDrop && i < sorted.Count; i++)
            {
                var kv = sorted[i];
                if (kv.Value.Mesh.BaseMesh != null) _pendingDestroy.Enqueue(kv.Value.Mesh.BaseMesh);
                _cache.Remove(kv.Key);
            }
        }
    }
}
