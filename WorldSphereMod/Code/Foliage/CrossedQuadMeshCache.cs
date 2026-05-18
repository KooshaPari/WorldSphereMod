using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.ProcGen;

namespace WorldSphereMod.Foliage
{
    /// <summary>
    /// LRU cache of crossed-quad foliage meshes. Mirrors <c>VoxelMeshCache</c>'s
    /// lock + deferred-destroy + 10% eviction pattern. Key folds the shape into
    /// the low byte so the same sprite can cache distinctly as CrossedQuad vs Single.
    /// </summary>
    public static class CrossedQuadMeshCache
    {
        public static int Capacity = 1024;

        struct Entry
        {
            public Mesh Mesh;
            public ulong LastFrame;
        }

        static readonly object _lock = new object();
        static readonly Dictionary<long, Entry> _cache = new Dictionary<long, Entry>(256);
        static readonly Queue<Mesh> _pendingDestroy = new Queue<Mesh>();
        static ulong _frame;

        public static int Count
        {
            get { lock (_lock) return _cache.Count; }
        }

        public static Mesh? GetOrBuild(Sprite sprite, BuildingShape shape, float swayAmplitude)
        {
            if (sprite == null) return null;
            long key = ((long)sprite.GetInstanceID() << 8) | (byte)shape;

            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var e))
                {
                    e.LastFrame = _frame;
                    _cache[key] = e;
                    return e.Mesh;
                }
            }

            // Build outside the lock — Unity Mesh construction is main-thread and
            // shouldn't be held under a lock, matching the VoxelMeshCache pattern.
            Mesh m = CrossedQuadMesher.Build(sprite, shape, swayAmplitude);

            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var existing))
                {
                    // Lost a race: another caller built first. Discard ours.
                    _pendingDestroy.Enqueue(m);
                    existing.LastFrame = _frame;
                    _cache[key] = existing;
                    return existing.Mesh;
                }

                _cache[key] = new Entry { Mesh = m, LastFrame = _frame };
                if (_cache.Count > Capacity) Evict();
                return m;
            }
        }

        // Reserved for future use; Step 2 doesn't invalidate by assetId yet because
        // the cache is keyed on sprite instance, not asset id. Kept in the surface
        // for parity with ProcGenCache.Invalidate so BuildingRulesRegistry can call
        // both once the asset→sprite map is wired.
        public static void Invalidate(string assetId)
        {
            _ = assetId;
        }

        public static void Clear()
        {
            lock (_lock)
            {
                foreach (var e in _cache.Values)
                {
                    if (e.Mesh != null) _pendingDestroy.Enqueue(e.Mesh);
                }
                _cache.Clear();
            }
        }

        public static void Tick()
        {
            lock (_lock) _frame++;
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
            // Caller holds _lock. Drop the oldest 10% of entries.
            int toDrop = Mathf.Max(1, _cache.Count / 10);
            var sorted = new List<KeyValuePair<long, Entry>>(_cache);
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
