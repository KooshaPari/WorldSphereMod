using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Rig
{
    /// <summary>
    /// LRU cache of rig-aware voxel meshes keyed by (<see cref="Sprite.GetInstanceID"/>, <see cref="RigType"/>).
    /// Humanoid entries keep a per-texel bone map; other rigs fall back to a static voxel
    /// mesh until their dedicated deformation code lands.
    /// </summary>
    public static class RigCache
    {
        public const int MAX_ENTRIES = 128;
        public static int Capacity => MAX_ENTRIES;

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
            if (sprite == null)
            {
                return new SkinnedVoxelMesh { RigType = RigType.None };
            }

            long key = ((long)(uint)sprite.GetInstanceID() << 8) | (byte)rigType;
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var e))
                {
                    e.LastFrame = _frame;
                    _cache[key] = e;
                    return e.Mesh;
                }
            }

            SkinnedVoxelMesh built = BuildMesh(sprite, rigType);

            lock (_lock)
            {
                _cache[key] = new Entry { Mesh = built, LastFrame = _frame };
                if (_cache.Count > Capacity)
                {
                    Evict();
                }
            }

            return built;
        }

        static SkinnedVoxelMesh BuildMesh(Sprite sprite, RigType rigType)
        {
            if (rigType == RigType.Humanoid || rigType == RigType.Quadruped)
            {
                return VoxelMeshCache.BuildWithBoneWeights(sprite, rigType);
            }

            Mesh mesh = SpriteVoxelizer.BuildPerTexel(sprite, SpriteVoxelizer.DefaultDepth, out _);
            return new SkinnedVoxelMesh
            {
                BaseMesh = mesh,
                BoneIndices = System.Array.Empty<byte>(),
                RigType = rigType == RigType.None ? RigType.Static : rigType,
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
                foreach (var kv in _cache)
                {
                    if (kv.Value.Mesh.BaseMesh != null)
                    {
                        Object.Destroy(kv.Value.Mesh.BaseMesh);
                    }
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
                    if (m != null)
                    {
                        Object.Destroy(m);
                    }
                }
            }
        }

        static void Evict()
        {
            if (_cache.Count <= MAX_ENTRIES)
            {
                return;
            }

            int toRemoveCount = _cache.Count - MAX_ENTRIES;
            while (_cache.Count > MAX_ENTRIES && toRemoveCount > 0)
            {
                long lruKey = -1;
                ulong lruFrame = ulong.MaxValue;
                foreach (var kv in _cache)
                {
                    if (kv.Value.LastFrame < lruFrame)
                    {
                        lruFrame = kv.Value.LastFrame;
                        lruKey = kv.Key;
                    }
                }

                if (lruKey < 0)
                {
                    break;
                }

                Entry lruEntry = _cache[lruKey];
                if (lruEntry.Mesh.BaseMesh != null)
                {
                    _pendingDestroy.Enqueue(lruEntry.Mesh.BaseMesh);
                }

                _cache.Remove(lruKey);
                toRemoveCount--;
            }
        }
    }
}
