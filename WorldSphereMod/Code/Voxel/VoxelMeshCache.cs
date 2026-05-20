using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using WorldSphereMod;
using Debug = UnityEngine.Debug;

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
        public static int Capacity = 8192;

        struct Entry
        {
            public Mesh Mesh;
            public ulong LastFrame;
        }

        static readonly object _lock = new object();
        static readonly Dictionary<int, Entry> _cache = new Dictionary<int, Entry>(1024);
        static readonly HashSet<int> _diagnosedSprites = new HashSet<int>();
        static readonly Queue<Sprite> _warmQueue = new Queue<Sprite>();
        static readonly HashSet<int> _warmQueuedSprites = new HashSet<int>();
        // Evict() can't Destroy a mesh that may still be queued in the batcher for this frame;
        // queue it here and let VoxelFrameDriver drain after MeshInstanceBatcher.Flush().
        static readonly Queue<Mesh> _pendingDestroy = new Queue<Mesh>();
        static ulong _frame;
        static int _warmBudgetMsPerFrame = 5;
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
        public static Mesh Get(Sprite sprite, int depth = -1)
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
            if (m != null && Core.savedSettings.VoxelMeshSmoothing)
            {
                // ADR-0008: Laplacian smoothing converts blocky voxel stair-steps
                // into a rounded 'blob' silhouette. Smooth returns a copy via
                // Object.Instantiate; destroy the raw mesh so we don't leak it.
                Mesh smoothed = MeshSmoother.Smooth(m, Core.savedSettings.SmoothingIterations);
                if (smoothed != null && !ReferenceEquals(smoothed, m))
                {
                    Object.Destroy(m);
                    m = smoothed;
                }
            }
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

        /// <summary>
        /// Queue sprites for budgeted voxel-mesh warmup. Work is drained from
        /// <see cref="VoxelFrameDriver.LateUpdate"/> via <see cref="DrainWarmCacheTick"/>.
        /// </summary>
        public static void WarmCacheAsync(IEnumerable<Sprite> sprites, int msBudgetPerFrame = 5)
        {
            if (sprites == null) return;

            lock (_lock)
            {
                _warmBudgetMsPerFrame = msBudgetPerFrame > 0 ? msBudgetPerFrame : 1;

                foreach (var sprite in sprites)
                {
                    if (!ShouldWarmSprite(sprite)) continue;

                    int key = sprite.GetInstanceID();
                    if (_cache.ContainsKey(key) || _warmQueuedSprites.Contains(key)) continue;

                    _warmQueue.Enqueue(sprite);
                    _warmQueuedSprites.Add(key);
                }
            }
        }

        /// <summary>Advance the frame counter; call once per render frame.</summary>
        public static void Tick()
        {
            lock (_lock) _frame++;
        }

        /// <summary>
        /// Drain queued warm-cache work for a bounded amount of time. Call once per
        /// frame from <see cref="VoxelFrameDriver.LateUpdate"/>.
        /// </summary>
        public static void DrainWarmCacheTick()
        {
            int budgetMs;
            lock (_lock)
            {
                budgetMs = _warmBudgetMsPerFrame;
            }

            if (budgetMs <= 0) return;

            Stopwatch sw = Stopwatch.StartNew();
            var batch = new List<Sprite>();
            while (sw.ElapsedMilliseconds < budgetMs)
            {
                Sprite sprite = null;

                lock (_lock)
                {
                    while (_warmQueue.Count > 0)
                    {
                        sprite = _warmQueue.Dequeue();
                        if (sprite == null) continue;

                        int key = sprite.GetInstanceID();
                        _warmQueuedSprites.Remove(key);

                        if (sprite.texture == null) { sprite = null; continue; }
                        if (IsPerpSprite(sprite)) { sprite = null; continue; }
                        if (_cache.ContainsKey(key)) { sprite = null; continue; }
                        break;
                    }

                    if (sprite == null)
                    {
                        break;
                    }
                }

                batch.Add(sprite);
            }

            if (batch.Count == 0) return;

            if (batch.Count == 1)
            {
                CacheWarmSprite(batch[0], SpriteVoxelizer.Build(batch[0]));
                return;
            }

            var built = new ConcurrentQueue<(Sprite Sprite, Mesh Mesh)>();
            System.Threading.Tasks.Parallel.ForEach(batch, sprite =>
            {
                built.Enqueue((sprite, SpriteVoxelizer.Build(sprite)));
            });

            while (built.TryDequeue(out var result))
            {
                CacheWarmSprite(result.Sprite, result.Mesh);
            }
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
                _warmQueue.Clear();
                _warmQueuedSprites.Clear();
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

        static bool IsPerpSprite(Sprite sprite)
        {
            if (sprite == null) return false;

            string name = sprite.name;
            return (!string.IsNullOrEmpty(name) && (Constants.PerpActors.ContainsKey(name) || Constants.PerpBuildings.ContainsKey(name)));
        }

        static bool ShouldWarmSprite(Sprite sprite)
        {
            return sprite != null && sprite.texture != null && !IsPerpSprite(sprite);
        }

        static void CacheWarmSprite(Sprite sprite, Mesh mesh)
        {
            LogVoxelizedSprite(sprite, mesh);
            if (sprite == null || mesh == null || mesh.vertexCount == 0)
            {
                return;
            }

            int warmKey = sprite.GetInstanceID();
            lock (_lock)
            {
                if (_cache.ContainsKey(warmKey))
                {
                    _pendingDestroy.Enqueue(mesh);
                    return;
                }

                _cache[warmKey] = new Entry { Mesh = mesh, LastFrame = _frame };
                if (_cache.Count > Capacity) Evict();
            }
        }
    }
}
