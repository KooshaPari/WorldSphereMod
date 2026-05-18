using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Fx
{
    /// <summary>
    /// Three logical decal categories. Pool sizes and default TTL policy live in
    /// <see cref="DecalPool"/>; the caller picks a channel per emit.
    /// </summary>
    public enum DecalChannel
    {
        Footprint,
        Scorch,
        Blood,
    }

    /// <summary>
    /// Phase 9 Step 2 decal lifecycle layer. Three preallocated sub-pools of flat XZ
    /// quad GameObjects (placeholder stand-in for URP <c>DecalProjector</c>, which
    /// requires the URP package reference upstream doesn't carry yet — the visual
    /// remains a flat-on-ground quad until a future phase upgrades the asset path).
    /// Drop-on-overflow when the free queue is empty; <see cref="Tick"/> reclaims
    /// expired entries every frame from <c>VoxelFrameDriver.LateUpdate</c>.
    ///
    /// Footprint channel uses <see cref="float.PositiveInfinity"/> TTL by design —
    /// actor lifetime is managed by the caller, not by the timer.
    /// </summary>
    public static class DecalPool
    {
        const int FootprintPoolSize = 32;
        const int ScorchPoolSize = 16;
        const int BloodPoolSize = 32;

        struct ActiveEntry
        {
            public GameObject Obj;
            public float Expiry;
        }

        static readonly Queue<GameObject>[] _free = new Queue<GameObject>[3];
        static readonly List<ActiveEntry>[] _active = new List<ActiveEntry>[3];
        static readonly GameObject?[] _pools = new GameObject?[3];
        static bool _initialized;

        public static void Init(Transform parent)
        {
            if (_initialized) Clear();

            CreateChannel(DecalChannel.Footprint, FootprintPoolSize, parent);
            CreateChannel(DecalChannel.Scorch, ScorchPoolSize, parent);
            CreateChannel(DecalChannel.Blood, BloodPoolSize, parent);
            _initialized = true;
        }

        public static void Emit(DecalChannel channel, Vector3 worldPos, Quaternion rot, float ttl)
        {
            if (!_initialized) return;

            int idx = (int)channel;
            var free = _free[idx];
            if (free.Count == 0) return;

            var obj = free.Dequeue();
            var t = obj.transform;
            t.position = worldPos;
            t.rotation = rot;
            obj.SetActive(true);

            float expiry = channel == DecalChannel.Footprint ? float.PositiveInfinity : Time.time + ttl;
            _active[idx].Add(new ActiveEntry { Obj = obj, Expiry = expiry });
        }

        public static void Tick()
        {
            if (!_initialized) return;

            float now = Time.time;
            for (int c = 0; c < 3; c++)
            {
                var active = _active[c];
                var free = _free[c];
                for (int i = active.Count - 1; i >= 0; i--)
                {
                    var entry = active[i];
                    if (now >= entry.Expiry)
                    {
                        entry.Obj.SetActive(false);
                        free.Enqueue(entry.Obj);
                        active.RemoveAt(i);
                    }
                }
            }
        }

        public static void Clear()
        {
            for (int c = 0; c < 3; c++)
            {
                if (_pools[c] != null)
                {
                    Object.Destroy(_pools[c]);
                    _pools[c] = null;
                }
                _free[c] = null!;
                _active[c] = null!;
            }
            _initialized = false;
        }

        static void CreateChannel(DecalChannel channel, int count, Transform parent)
        {
            int idx = (int)channel;
            var pool = new GameObject("WSM3D.DecalPool." + channel);
            pool.transform.SetParent(parent, worldPositionStays: false);
            _pools[idx] = pool;

            var free = new Queue<GameObject>(count);
            for (int i = 0; i < count; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Decal_" + channel + "_" + i;
                var col = quad.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                var tr = quad.transform;
                tr.SetParent(pool.transform, worldPositionStays: false);
                tr.localRotation = Quaternion.Euler(90f, 0f, 0f);
                tr.localScale = Vector3.one;

                var mr = quad.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var shader = Shader.Find("Sprites/Default");
                    if (shader != null)
                    {
                        mr.sharedMaterial = new Material(shader);
                    }
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }

                quad.SetActive(false);
                free.Enqueue(quad);
            }
            _free[idx] = free;
            _active[idx] = new List<ActiveEntry>(count);
        }
    }
}
