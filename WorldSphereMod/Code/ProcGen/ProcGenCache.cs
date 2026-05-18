using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.ProcGen
{
    public static class ProcGenCache
    {
        public static int Capacity = 512;

        struct Entry
        {
            public Mesh Mesh;
            public ulong LastFrame;
        }

        static readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>(256);
        static readonly Queue<Mesh> _pendingDestroy = new Queue<Mesh>();
        static ulong _frame;

        public static int Count
        {
            get
            {
                lock (_cache)
                {
                    return _cache.Count;
                }
            }
        }

        public static Mesh? GetOrGenerate(BuildingAsset asset, BuildingRules rules)
        {
            if (asset == null) return null;
            // Cache key is asset.id only. WorldBox doesn't hot-reload building sprites
            // mid-session; manual invalidation is exposed via Invalidate().
            string key = asset.id ?? "null";

            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var e))
                {
                    e.LastFrame = _frame;
                    _cache[key] = e;
                    return e.Mesh;
                }
            }

            Mesh m = BuildingMeshGen.Generate(asset, rules);
            // Don't cache a null result — Generate returns null for blank construction
            // frames; we want to retry on the next call when the sprite is non-blank
            // rather than poisoning the cache with a permanent fallback.
            if (m == null) return null;

            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var existing))
                {
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

        public static void Invalidate(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return;
            lock (_cache)
            {
                if (_cache.TryGetValue(assetId, out var entry))
                {
                    if (entry.Mesh != null) _pendingDestroy.Enqueue(entry.Mesh);
                    _cache.Remove(assetId);
                }
            }
        }

        public static void Clear()
        {
            // Route everything through _pendingDestroy so the actual Object.Destroy
            // calls happen on the main thread via DrainPendingDestroy, not under the
            // lock. Object.Destroy is main-thread-only — see Unity docs.
            lock (_cache)
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
            lock (_cache)
            {
                _frame++;
            }
        }

        public static void DrainPendingDestroy()
        {
            lock (_cache)
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
            // Caller holds _cache lock. O(N) two-pass eviction — find frame range, drop bottom decile.
            if (_cache.Count == 0) return;
            ulong minFrame = ulong.MaxValue, maxFrame = 0;
            foreach (var v in _cache.Values)
            {
                if (v.LastFrame < minFrame) minFrame = v.LastFrame;
                if (v.LastFrame > maxFrame) maxFrame = v.LastFrame;
            }
            if (maxFrame == minFrame) return;
            ulong threshold = minFrame + (maxFrame - minFrame) / 10;
            var toRemove = new List<string>();
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
    }
}
