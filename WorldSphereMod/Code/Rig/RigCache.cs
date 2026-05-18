using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Voxel;

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

        // Step 4: voxelize without greedy merging so each emitted vertex can be traced back
        // to a single source texel, then look up that texel's BoneId from
        // HumanoidRig.SegmentVoxels. Vertices whose texel index is out of range or maps to a
        // transparent pixel (BoneId.Root sentinel from SegmentVoxels) fall back to Spine.
        static SkinnedVoxelMesh BuildHumanoid(Sprite sprite)
        {
            BoneId[] segment = null;
            int segW = 0, segH = 0;
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
                segment = HumanoidRig.SegmentVoxels(w, h, sub);
                segW = w;
                segH = h;
            }

            var mesh = SpriteVoxelizer.BuildPerTexel(sprite, SpriteVoxelizer.DefaultDepth, out int[] vertexToTexel);
            var boneIndices = new byte[mesh.vertexCount];
            int segLen = segment != null ? segment.Length : 0;
            int vmapLen = vertexToTexel != null ? vertexToTexel.Length : 0;
            for (int i = 0; i < boneIndices.Length; i++)
            {
                BoneId bone = BoneId.Spine;
                if (segment != null && i < vmapLen)
                {
                    int t = vertexToTexel[i];
                    if (t >= 0 && t < segLen)
                    {
                        BoneId b = segment[t];
                        // SegmentVoxels uses Root=0 as the "unassigned / transparent" sentinel;
                        // remap to Spine so those verts don't anchor to the world origin.
                        bone = b == BoneId.Root ? BoneId.Spine : b;
                    }
                }
                boneIndices[i] = (byte)bone;
            }

            return new SkinnedVoxelMesh
            {
                BaseMesh = mesh,
                BoneIndices = boneIndices,
                RigType = RigType.Humanoid,
            };
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
            // Caller holds _lock. O(N) two-pass eviction — find frame range, drop bottom decile.
            if (_cache.Count == 0) return;
            ulong minFrame = ulong.MaxValue, maxFrame = 0;
            foreach (var v in _cache.Values)
            {
                if (v.LastFrame < minFrame) minFrame = v.LastFrame;
                if (v.LastFrame > maxFrame) maxFrame = v.LastFrame;
            }
            if (maxFrame == minFrame) return;
            ulong threshold = minFrame + (maxFrame - minFrame) / 10;
            var toRemove = new List<long>();
            foreach (var kv in _cache)
            {
                if (kv.Value.LastFrame <= threshold) toRemove.Add(kv.Key);
            }
            foreach (var key in toRemove)
            {
                if (_cache[key].Mesh.BaseMesh != null) _pendingDestroy.Enqueue(_cache[key].Mesh.BaseMesh);
                _cache.Remove(key);
            }
        }
    }
}
