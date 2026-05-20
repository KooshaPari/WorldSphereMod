using System;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.ProcGen;

namespace WorldSphereMod.Foliage
{
    /// <summary>
    /// LRU cache of crossed-quad foliage meshes. Mirrors <c>VoxelMeshCache</c>'s
    /// lock + deferred-destroy + 10% eviction pattern. Key includes the sprite,
    /// shape, asset profile, and sway amplitude so the same atlas frame can cache
    /// distinctly for oak/pine/palm variants or sway/no-sway paths.
    /// </summary>
    public static class CrossedQuadMeshCache
    {
        public static int Capacity = 1024;

        struct CacheKey
        {
            public int SpriteId;
            public BuildingShape Shape;
            public CrossedQuadVariant Variant;
            public int SwayBits;

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = SpriteId;
                    hash = (hash * 397) ^ (int)Shape;
                    hash = (hash * 397) ^ (int)Variant;
                    hash = (hash * 397) ^ SwayBits;
                    return hash;
                }
            }

            public override bool Equals(object obj)
            {
                if (obj is not CacheKey other) return false;
                return SpriteId == other.SpriteId
                    && Shape == other.Shape
                    && Variant == other.Variant
                    && SwayBits == other.SwayBits;
            }
        }

        struct Entry
        {
            public Mesh Mesh;
            public ulong LastFrame;
        }

        static readonly object _lock = new object();
        static readonly Dictionary<CacheKey, Entry> _cache = new Dictionary<CacheKey, Entry>(256);
        static readonly Queue<Mesh> _pendingDestroy = new Queue<Mesh>();
        static ulong _frame;

        public static int Count
        {
            get { lock (_lock) return _cache.Count; }
        }

        public static Mesh? GetOrBuild(Sprite sprite, BuildingShape shape, float swayAmplitude, string? assetId = null)
        {
            if (sprite == null) return null;
            CacheKey key = new CacheKey
            {
                SpriteId = sprite.GetInstanceID(),
                Shape = shape,
                Variant = ResolveVariant(assetId),
                SwayBits = System.BitConverter.ToInt32(System.BitConverter.GetBytes(swayAmplitude), 0),
            };

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
            Mesh m = CrossedQuadMesher.Build(sprite, shape, swayAmplitude, key.Variant);

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
            var toRemove = new List<CacheKey>();
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

        static CrossedQuadVariant ResolveVariant(string? assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return CrossedQuadVariant.Generic;

            string id = assetId!;
            if (id.IndexOf("palm", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CrossedQuadVariant.Palm;
            }
            if (id.IndexOf("pine", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CrossedQuadVariant.Pine;
            }
            if (id.IndexOf("oak", System.StringComparison.OrdinalIgnoreCase) >= 0
                || id.StartsWith("tree_", System.StringComparison.OrdinalIgnoreCase))
            {
                return CrossedQuadVariant.Oak;
            }
            return CrossedQuadVariant.Generic;
        }
    }
}
