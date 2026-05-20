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
        static readonly HashSet<int> _diagnosedSprites = new HashSet<int>();
        // Evict() can't Destroy a mesh that may still be queued in the batcher for this frame;
        // queue it here and let VoxelFrameDriver drain after MeshInstanceBatcher.Flush().
        static readonly Queue<Mesh> _pendingDestroy = new Queue<Mesh>();
        static ulong _frame;
        static long _hits;
        static long _misses;

        /// <summary>Cumulative cache-hit count since process start (or last Clear).</summary>
        public static long HitCount => System.Threading.Interlocked.Read(ref _hits);
        /// <summary>Cumulative cache-miss count since process start (or last Clear).</summary>
        public static long MissCount => System.Threading.Interlocked.Read(ref _misses);

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
                    if (e.Mesh == null || e.Mesh.vertexCount == 0)
                    {
                        _cache.Remove(key);
                        return null;
                    }
                    e.LastFrame = _frame;
                    _cache[key] = e;
                    System.Threading.Interlocked.Increment(ref _hits);
                    return e.Mesh;
                }
            }
            // Build outside the lock — Mesh construction touches Unity APIs that
            // shouldn't be held under a lock, and Get() always runs on the main thread.
            System.Threading.Interlocked.Increment(ref _misses);
            Mesh m = SpriteVoxelizer.Build(sprite, depth);
            LogVoxelizedSprite(sprite, m);
            if (m == null || m.vertexCount == 0)
            {
                return null;
            }
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
                _diagnosedSprites.Clear();
                _pendingDestroy.Clear();
            }
            System.Threading.Interlocked.Exchange(ref _hits, 0);
            System.Threading.Interlocked.Exchange(ref _misses, 0);
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
            var toRemove = new List<int>();
            foreach (var kv in _cache)
            {
                if (kv.Value.LastFrame <= threshold) toRemove.Add(kv.Key);
            }
            foreach (var key in toRemove)
            {
                if (_cache[key].Mesh != null) _pendingDestroy.Enqueue(_cache[key].Mesh);
                _cache.Remove(key);
            }
        }

        static void LogVoxelizedSprite(Sprite sprite, Mesh mesh)
        {
            if (sprite == null || mesh == null) return;
            int key = sprite.GetInstanceID();
            lock (_lock)
            {
                if (!_diagnosedSprites.Add(key)) return;
            }

            int triCount = mesh.subMeshCount > 0 ? (int)(mesh.GetIndexCount(0) / 3) : 0;
            Debug.Log($"[WSM3D] Voxelized sprite \"{sprite.name}\" -> {mesh.vertexCount} verts, {triCount} tris, bounds={mesh.bounds}");
        }
    }
}
