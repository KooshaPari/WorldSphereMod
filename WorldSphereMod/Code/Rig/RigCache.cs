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
            var stub = new SkinnedVoxelMesh
            {
                BaseMesh = new Mesh { name = $"rig:stub:{sprite.name}" },
                BoneIndices = System.Array.Empty<byte>(),
                RigType = RigType.None,
            };
            lock (_lock)
            {
                _cache[key] = new Entry { Mesh = stub, LastFrame = _frame };
                if (_cache.Count > Capacity) Evict();
            }
            return stub;
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
