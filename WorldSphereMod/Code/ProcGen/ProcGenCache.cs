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

            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var existing))
                {
                    if (m != null) _pendingDestroy.Enqueue(m);
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
            lock (_cache)
            {
                foreach (var e in _cache.Values)
                {
                    if (e.Mesh != null) Object.Destroy(e.Mesh);
                }
                _cache.Clear();
                while (_pendingDestroy.Count > 0)
                {
                    var m = _pendingDestroy.Dequeue();
                    if (m != null) Object.Destroy(m);
                }
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
            int toDrop = Mathf.Max(1, _cache.Count / 10);
            var sorted = new List<KeyValuePair<string, Entry>>(_cache);
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
