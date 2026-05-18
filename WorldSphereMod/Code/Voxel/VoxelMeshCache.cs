using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// LRU cache of voxelized meshes keyed by <see cref="Sprite.GetInstanceID"/>. Survives
    /// world rebuilds (entries live in the static dictionary), but evicts on capacity.
    ///
    /// The cache is the only allocation site for voxel meshes — every render pass that
    /// previously assigned a <see cref="Sprite"/> to a quad should call <see cref="Get"/>
    /// instead and feed the result to <see cref="MeshInstanceBatcher"/>.
    /// </summary>
    public static class VoxelMeshCache
    {
        public static int Capacity = 4096;

        struct Entry
        {
            public Mesh Mesh;
            public ulong LastFrame;
        }

        static readonly object _lock = new object();
        static readonly Dictionary<int, Entry> _cache = new Dictionary<int, Entry>(1024);
        // Evict() can't Destroy a mesh that may still be queued in the batcher for this frame;
        // queue it here and let VoxelFrameDriver drain after MeshInstanceBatcher.Flush().
        static readonly Queue<Mesh> _pendingDestroy = new Queue<Mesh>();
        static ulong _frame;

        /// <summary>Total number of meshes currently held.</summary>
        public static int Count
        {
            get { lock (_lock) return _cache.Count; }
        }

        /// <summary>Return the cached voxel mesh for <paramref name="sprite"/>, building one if missing.</summary>
        public static Mesh Get(Sprite sprite, int depth = SpriteVoxelizer.DefaultDepth)
        {
            if (sprite == null) return null;
            int key = sprite.GetInstanceID();
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var e))
                {
                    e.LastFrame = _frame;
                    _cache[key] = e;
                    return e.Mesh;
                }
            }
            // Build outside the lock — Mesh construction touches Unity APIs that
            // shouldn't be held under a lock, and Get() always runs on the main thread.
            Mesh m = SpriteVoxelizer.Build(sprite, depth);
            lock (_lock)
            {
                _cache[key] = new Entry { Mesh = m, LastFrame = _frame };
                if (_cache.Count > Capacity) Evict();
            }
            return m;
        }

        /// <summary>Advance the frame counter; call once per render frame.</summary>
        public static void Tick()
        {
            lock (_lock) _frame++;
        }

        /// <summary>Wipe everything. Call when the world reloads.</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                foreach (var e in _cache.Values)
                {
                    if (e.Mesh != null) Object.Destroy(e.Mesh);
                }
                _cache.Clear();
                _pendingDestroy.Clear();
            }
        }

        /// <summary>Destroy meshes queued by <see cref="Evict"/>. Call once per frame after the batcher flushes.</summary>
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
            // Caller holds _lock. Drop the oldest 10% of entries.
            int toDrop = Mathf.Max(1, _cache.Count / 10);
            var sorted = new List<KeyValuePair<int, Entry>>(_cache);
            sorted.Sort((a, b) => a.Value.LastFrame.CompareTo(b.Value.LastFrame));
            for (int i = 0; i < toDrop && i < sorted.Count; i++)
            {
                var kv = sorted[i];
                if (kv.Value.Mesh != null) _pendingDestroy.Enqueue(kv.Value.Mesh);
                _cache.Remove(kv.Key);
            }
        }
    }
}
