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
            if (_cache.Count == 0)
            {
                return;
            }

            ulong minFrame = ulong.MaxValue, maxFrame = 0;
            foreach (var v in _cache.Values)
            {
                if (v.LastFrame < minFrame) minFrame = v.LastFrame;
                if (v.LastFrame > maxFrame) maxFrame = v.LastFrame;
            }
            if (maxFrame == minFrame)
            {
                return;
            }

            ulong threshold = minFrame + (maxFrame - minFrame) / 10;
            var toRemove = new List<long>();
            foreach (var kv in _cache)
            {
                if (kv.Value.LastFrame <= threshold)
                {
                    toRemove.Add(kv.Key);
                }
            }

            foreach (var key in toRemove)
            {
                if (_cache[key].Mesh.BaseMesh != null)
                {
                    _pendingDestroy.Enqueue(_cache[key].Mesh.BaseMesh);
                }
                _cache.Remove(key);
            }
        }
    }
}
