using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Fx
{
    /// <summary>
    /// Burst shape used by <see cref="ParticleEffectLibrary"/> to derive per-particle
    /// initial velocity. Sphere = uniform sphere, Ring = horizontal XZ ring, Cone =
    /// upward cone with small lateral spread.
    /// </summary>
    public enum BurstShape
    {
        Sphere,
        Ring,
        Cone,
    }

    /// <summary>
    /// Immutable per-effect particle burst descriptor. Looked up by string key from
    /// <see cref="ParticleEffectLibrary"/>'s static table; values come from the
    /// Phase 9 design doc (see <c>docs/phase9-architecture.md</c> §3).
    /// </summary>
    public readonly struct ParticleBurst
    {
        public readonly string EffectId;
        public readonly int Count;
        public readonly float Speed;
        public readonly float Lifetime;
        public readonly float Size;
        public readonly BurstShape Shape;
        public readonly Color TintA;
        public readonly Color TintB;

        public ParticleBurst(string id, int c, float spd, float life, float sz, BurstShape sh, Color a, Color b)
        {
            EffectId = id;
            Count = c;
            Speed = spd;
            Lifetime = life;
            Size = sz;
            Shape = sh;
            TintA = a;
            TintB = b;
        }
    }

    /// <summary>
    /// Static data + pool layer for Phase 9 burst effects. <see cref="Init"/> populates
    /// the per-effect burst table, probes for the VFX Graph package (currently used
    /// only as a capability flag — actual VFX Graph routing is deferred), and builds
    /// a 16-system <see cref="ParticleSystem"/> pool under a parent
    /// <c>"WSM3D.ParticlePool"</c> GameObject. <see cref="Fire"/> looks up the burst,
    /// acquires the next available pooled system via round-robin, configures it via
    /// <see cref="ParticleSystem.EmitParams"/>, and emits. Overflow drops silently.
    /// </summary>
    public static class ParticleEffectLibrary
    {
        public static bool VfxGraphAvailable { get; private set; }
        public static int PoolSize = 16;

        static readonly Dictionary<string, ParticleBurst> _table = new Dictionary<string, ParticleBurst>(8);
        static readonly List<ParticleSystem> _pool = new List<ParticleSystem>(16);
        static GameObject _poolRoot;
        static Mesh _voxelCubeMesh;
        static int _nextSystemIndex;
        static bool _initialized;

        public static int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _pool.Count; i++)
                {
                    var ps = _pool[i];
                    if (ps != null && ps.IsAlive(true)) n++;
                }
                return n;
            }
        }

        public static void Init()
        {
            if (_initialized) return;

            // Capability probe — VFX Graph assembly may or may not be present at runtime.
            // We do not take a compile-time reference; resolve by qualified name only.
            VfxGraphAvailable = Type.GetType("UnityEngine.VFX.VisualEffect, Unity.VisualEffectGraph") != null;

            PopulateBurstTable();
            BuildVoxelCubeMesh();
            BuildPool();

            _initialized = true;
        }

        public static bool Fire(string effectId, Vector3 worldPos, float scale = 1.0f)
        {
            if (!_initialized) return false;
            if (effectId == null) return false;
            if (!_table.TryGetValue(effectId, out var burst)) return false;
            if (_pool.Count == 0) return false;

            // Round-robin acquire. Scan up to PoolSize entries to find one that
            // isn't still alive from a previous burst; drop on overflow.
            ParticleSystem ps = null;
            for (int i = 0; i < _pool.Count; i++)
            {
                int idx = (_nextSystemIndex + i) % _pool.Count;
                var candidate = _pool[idx];
                if (candidate == null) continue;
                if (!candidate.IsAlive(true))
                {
                    ps = candidate;
                    _nextSystemIndex = (idx + 1) % _pool.Count;
                    break;
                }
            }
            if (ps == null) return false;

            ps.transform.position = worldPos;
            // Clear stale particles from any prior emit. We're explicitly NOT calling
            // Play() because Emit() with EmitParams works regardless of play state and
            // we want fire-and-forget single bursts, not a continuous emission.
            ps.Clear(true);

            float invSqrt2 = 0.70710678f;

            for (int i = 0; i < burst.Count; i++)
            {
                Vector3 velocity;
                switch (burst.Shape)
                {
                    case BurstShape.Ring:
                    {
                        float a = UnityEngine.Random.value * Mathf.PI * 2f;
                        velocity = new Vector3(Mathf.Cos(a), 0.05f, Mathf.Sin(a)) * burst.Speed;
                        break;
                    }
                    case BurstShape.Cone:
                    {
                        float a = UnityEngine.Random.value * Mathf.PI * 2f;
                        float r = UnityEngine.Random.value * 0.5f;
                        velocity = new Vector3(Mathf.Cos(a) * r, 1f, Mathf.Sin(a) * r).normalized * burst.Speed;
                        break;
                    }
                    case BurstShape.Sphere:
                    default:
                    {
                        // UnityEngine.Random.insideUnitSphere — unit-or-less; normalise
                        // for a clean shell-like burst (matches design's "explosion" feel).
                        Vector3 dir = UnityEngine.Random.insideUnitSphere;
                        if (dir.sqrMagnitude < 0.0001f) dir = new Vector3(invSqrt2, 0f, invSqrt2);
                        else dir = dir.normalized;
                        velocity = dir * burst.Speed;
                        break;
                    }
                }

                var emitParams = new ParticleSystem.EmitParams
                {
                    position = Vector3.zero, // local-space; the system's transform is at worldPos
                    velocity = velocity,
                    startColor = Color.Lerp(burst.TintA, burst.TintB, UnityEngine.Random.value),
                    startSize = burst.Size * scale,
                    startLifetime = burst.Lifetime,
                };
                emitParams.applyShapeToPosition = false;

                ps.Emit(emitParams, 1);
            }

            return true;
        }

        public static void Tick()
        {
            // Particle expiry is driven by Unity's per-system lifetime; pool reclaim is
            // implicit via IsAlive checks in Fire(). Nothing to do here yet — this
            // method exists so the future FxFrameDriver can call it unconditionally.
        }

        public static void Clear()
        {
            if (_poolRoot != null)
            {
                UnityEngine.Object.Destroy(_poolRoot);
                _poolRoot = null;
            }
            _pool.Clear();
            _table.Clear();
            _voxelCubeMesh = null;
            _nextSystemIndex = 0;
            _initialized = false;
        }

        static void PopulateBurstTable()
        {
            _table.Clear();

            Color warmOrange = new Color(1.0f, 0.55f, 0.15f, 1f);
            Color deepRed    = new Color(0.85f, 0.15f, 0.05f, 1f);
            Add(new ParticleBurst("fx_meteorite", 24, 6f, 0.6f, 0.15f, BurstShape.Cone, warmOrange, deepRed));

            Color brightYellow = new Color(1.0f, 0.95f, 0.4f, 1f);
            Color hotOrange    = new Color(1.0f, 0.5f, 0.1f, 1f);
            Add(new ParticleBurst("fx_explosion_wave", 40, 8f, 0.4f, 0.2f, BurstShape.Ring, brightYellow, hotOrange));

            Color paleGrey = new Color(0.65f, 0.62f, 0.6f, 1f);
            Color darkGrey = new Color(0.25f, 0.23f, 0.22f, 1f);
            Add(new ParticleBurst("fx_fire_smoke", 20, 1.5f, 1.2f, 0.18f, BurstShape.Sphere, paleGrey, darkGrey));

            Color purple  = new Color(0.55f, 0.2f, 0.85f, 1f);
            Color magenta = new Color(0.95f, 0.25f, 0.85f, 1f);
            Add(new ParticleBurst("fx_antimatter_effect", 32, 4f, 0.8f, 0.16f, BurstShape.Sphere, purple, magenta));

            Color white     = new Color(1f, 1f, 1f, 1f);
            Color napOrange = new Color(1f, 0.6f, 0.2f, 1f);
            Add(new ParticleBurst("fx_napalm_flash", 28, 7f, 0.3f, 0.22f, BurstShape.Ring, white, napOrange));
        }

        static void Add(ParticleBurst b) => _table[b.EffectId] = b;

        static void BuildVoxelCubeMesh()
        {
            // 8 verts, 12 tris. Tiny unit cube centred at origin; per-particle size
            // scales it. Not greedy-meshed — at this scale the cost is negligible
            // vs. swapping in the Voxel/SpriteVoxelizer pipeline for a single quad.
            var m = new Mesh { name = "WSM3D.ParticleCube" };
            const float h = 0.5f;
            Vector3[] verts =
            {
                new Vector3(-h, -h, -h), new Vector3( h, -h, -h),
                new Vector3( h,  h, -h), new Vector3(-h,  h, -h),
                new Vector3(-h, -h,  h), new Vector3( h, -h,  h),
                new Vector3( h,  h,  h), new Vector3(-h,  h,  h),
            };
            int[] tris =
            {
                0,2,1, 0,3,2,   // back
                4,5,6, 4,6,7,   // front
                0,1,5, 0,5,4,   // bottom
                3,7,6, 3,6,2,   // top
                0,4,7, 0,7,3,   // left
                1,2,6, 1,6,5,   // right
            };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            _voxelCubeMesh = m;
        }

        static void BuildPool()
        {
            _pool.Clear();
            _poolRoot = new GameObject("WSM3D.ParticlePool");
            UnityEngine.Object.DontDestroyOnLoad(_poolRoot);

            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("WSM3D.Particle." + i);
                go.transform.SetParent(_poolRoot.transform, false);

                var ps = go.AddComponent<ParticleSystem>();
                // AddComponent<ParticleSystem>() also adds ParticleSystemRenderer.
                var renderer = go.GetComponent<ParticleSystemRenderer>();

                // ParticleSystem module structs are wrappers around the underlying
                // system, so assigning to their fields mutates the system. The
                // intermediate `var` is required because the module *property* is
                // read-only on the system itself.
                var main = ps.main;
                main.startSize = 0.15f;
                main.startLifetime = 1.0f;
                main.startSpeed = 0f;
                main.playOnAwake = false;
                main.loop = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 256;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;

                // Disable the shape module — we drive per-particle initial velocity
                // ourselves via EmitParams, so the built-in shape would only fight us.
                var shape = ps.shape;
                shape.enabled = false;

                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Mesh;
                    if (_voxelCubeMesh != null)
                    {
                        renderer.mesh = _voxelCubeMesh;
                    }
                    // Leave material at Unity default; Phase 9 Step 3+ may swap in
                    // a URP-lit material. The default-particle material renders
                    // unlit but visible, which is sufficient for the data layer.
                }

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _pool.Add(ps);
            }
        }
    }
}
