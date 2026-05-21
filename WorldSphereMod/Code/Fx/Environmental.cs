using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Fx
{
    public struct Burst
    {
        public Vector3 Position;
        public float StartedAt;
        public float Duration;
        public float GrowPortion;
        public float FadePortion;
        public float Scale;
        public Color StartTint;
        public Color EndTint;
    }

    [Phase(nameof(SavedSettings.ParticleEffects))]
    public static class Environmental
    {
        const int kMaxEnqueuePerFrame = 64;
        const int kMaxActiveBursts = 256;
        const int kFootstepFrames = 8;
        const float kLeafSpawnChance = 0.10f;
        const float kFireflySpawnChance = 0.0125f;

        const float kLeafDuration = 0.60f;
        const float kFireflyDuration = 1.20f;
        const float kFootstepDuration = 0.40f;
        const float kBloodDuration = 0.50f;

        const float kLeafGrow = 0.25f;
        const float kLeafFade = 0.70f;
        const float kFireflyGrow = 0.30f;
        const float kFireflyFade = 0.80f;
        const float kFootstepGrow = 0.20f;
        const float kFootstepFade = 0.70f;
        const float kBloodGrow = 0.20f;
        const float kBloodFade = 0.65f;

        const float kLeafScale = 0.045f;
        const float kFireflyScale = 0.035f;
        const float kFootstepScale = 0.04f;
        const float kBloodScale = 0.06f;

        static readonly Color kFireflyColor = new Color(1f, 0.95f, 0.28f, 1f);
        static readonly Color kLeafColor = new Color(0.95f, 0.95f, 0.65f, 1f);
        static readonly Color kDustColor = new Color(0.37f, 0.24f, 0.14f, 1f);
        static readonly Color kBloodColor = new Color(0.90f, 0.12f, 0.12f, 1f);

        static readonly ConcurrentQueue<Burst> _pending = new ConcurrentQueue<Burst>();
        static readonly List<Burst> _active = new List<Burst>(128);
        static readonly Dictionary<int, Vector3> _footLastPos = new Dictionary<int, Vector3>(256);
        static readonly Dictionary<int, int> _footLastFrame = new Dictionary<int, int>(256);

        static Material _voxelCubeMaterial;
        static Mesh _voxelCubeMesh;
        static bool _initialized;

        public static void EnqueueLeaf(Vector3 position)
        {
            if (!Core.IsWorld3D || !Core.savedSettings.ParticleEffects)
            {
                return;
            }

            if (Random.value > kLeafSpawnChance)
            {
                return;
            }

            Enqueue(new Burst
            {
                Position = position + new Vector3(Random.Range(-0.25f, 0.25f), 0.05f, Random.Range(-0.25f, 0.25f)),
                StartedAt = Time.time,
                Duration = kLeafDuration,
                GrowPortion = kLeafGrow,
                FadePortion = kLeafFade,
                Scale = kLeafScale * Random.Range(0.75f, 1.25f),
                StartTint = kLeafColor,
                EndTint = kLeafColor * new Color(1f, 1f, 1f, 0.0f),
            });
        }

        public static void EnqueueFirefly(Vector3 position)
        {
            if (!Core.IsWorld3D || !Core.savedSettings.ParticleEffects || !Core.savedSettings.DayNightCycle)
            {
                return;
            }

            if (!IsNight(WorldSphereMod.Lighting.TimeOfDay.Current))
            {
                return;
            }

            if (Random.value > kFireflySpawnChance)
            {
                return;
            }

            Enqueue(new Burst
            {
                Position = position + new Vector3(Random.Range(-0.2f, 0.2f), 0.25f, Random.Range(-0.2f, 0.2f)),
                StartedAt = Time.time,
                Duration = kFireflyDuration,
                GrowPortion = kFireflyGrow,
                FadePortion = kFireflyFade,
                Scale = kFireflyScale * Random.Range(0.7f, 1.4f),
                StartTint = kFireflyColor,
                EndTint = kFireflyColor * new Color(1f, 1f, 1f, 0.15f),
            });
        }

        public static void EnqueueFootstep(Actor actor)
        {
            if (!Core.IsWorld3D || !Core.savedSettings.ParticleEffects || actor == null)
            {
                return;
            }

            int actorId = actor.GetHashCode();
            Vector3 rawPos = actor.current_position;
            Vector3 tilePos = Tools.To3DTileHeight(rawPos, 0.015f);
            if (_footLastPos.TryGetValue(actorId, out var prev))
            {
                if ((tilePos - prev).sqrMagnitude < 0.02f)
                {
                    return;
                }
            }

            if (_footLastFrame.TryGetValue(actorId, out int lastFrame) && Time.frameCount - lastFrame < kFootstepFrames)
            {
                return;
            }

            _footLastPos[actorId] = tilePos;
            _footLastFrame[actorId] = Time.frameCount;

            Enqueue(new Burst
            {
                Position = tilePos + Vector3.up * 0.015f,
                StartedAt = Time.time,
                Duration = kFootstepDuration,
                GrowPortion = kFootstepGrow,
                FadePortion = kFootstepFade,
                Scale = kFootstepScale * Random.Range(0.65f, 1.35f),
                StartTint = kDustColor,
                EndTint = kDustColor * new Color(1f, 1f, 1f, 0.0f),
            });
        }

        public static void SpawnBlood(Actor actor)
        {
            if (!Core.IsWorld3D || !Core.savedSettings.ParticleEffects || actor == null)
            {
                return;
            }

            Vector3 pos = actor.current_position;
            BaseEffect bloodFx = EffectsLibrary.spawnAt("fx_explosion_wave", pos, 0.4f);
            if (bloodFx == null || bloodFx.sprite_renderer == null)
            {
                return;
            }

            bloodFx.sprite_renderer.color = kBloodColor;
            bloodFx.sprite_renderer.enabled = true;
            VoxelParticleBurst.TryStart(bloodFx);
        }

        public static void Tick()
        {
            if (!Core.IsWorld3D || !Core.savedSettings.ParticleEffects)
            {
                _active.Clear();
                while (_pending.TryDequeue(out _)) { }
                return;
            }

            if (!EnsureResources())
            {
                return;
            }

            int enqueueBudget = kMaxEnqueuePerFrame;
            while (enqueueBudget-- > 0 && _pending.TryDequeue(out var burst))
            {
                _active.Add(burst);
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Burst burst = _active[i];
                float elapsed = Time.time - burst.StartedAt;
                float t = Mathf.Clamp01(elapsed / burst.Duration);
                if (t >= 1f)
                {
                    _active.RemoveAt(i);
                    continue;
                }

                float grow = burst.GrowPortion > 0f
                    ? Mathf.Clamp01(t / burst.GrowPortion)
                    : 1f;
                float alphaFade = 1f;
                if (t >= burst.FadePortion)
                {
                    float fadeT = Mathf.InverseLerp(burst.FadePortion, 1f, t);
                    alphaFade = 1f - Mathf.SmoothStep(0f, 1f, fadeT);
                }

                float scale = Mathf.Lerp(0.1f, 1f, Mathf.SmoothStep(0.01f, 1f, grow)) * burst.Scale;
                Color tint = Color.Lerp(burst.StartTint, burst.EndTint, t);
                tint.a *= Mathf.Clamp01(alphaFade);

                Matrix4x4 trs = Matrix4x4.TRS(burst.Position, Quaternion.identity, Vector3.one * scale);
                MeshInstanceBatcher.Submit(_voxelCubeMesh, _voxelCubeMaterial, trs, tint);
            }

            if (_active.Count > kMaxActiveBursts)
            {
                _active.RemoveRange(0, _active.Count - kMaxActiveBursts);
            }
        }

        public static void Clear()
        {
            _active.Clear();
            while (_pending.TryDequeue(out _)) { }
            _footLastPos.Clear();
            _footLastFrame.Clear();
            _initialized = false;
            _voxelCubeMaterial = null;
            _voxelCubeMesh = null;
        }

        public static void Enqueue(Burst burst)
        {
            _pending.Enqueue(burst);
        }

        static bool EnsureResources()
        {
            if (_initialized)
            {
                return _voxelCubeMaterial != null && _voxelCubeMesh != null;
            }

            _voxelCubeMaterial = VoxelRender.GetResolvedMaterial();
            _voxelCubeMesh = BuildVoxelCube();
            _initialized = true;
            return _voxelCubeMaterial != null && _voxelCubeMesh != null;
        }

        static Mesh BuildVoxelCube()
        {
            var m = new Mesh { name = "WSM3D.Environ.Cube" };
            const float h = 0.5f;
            Vector3[] verts =
            {
                new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(-h, h, -h),
                new Vector3(-h, -h,  h), new Vector3(h, -h,  h), new Vector3(h,  h,  h), new Vector3(-h, h,  h),
            };
            int[] tris =
            {
                0,2,1, 0,3,2,
                4,5,6, 4,6,7,
                0,1,5, 0,5,4,
                3,7,6, 3,6,2,
                0,4,7, 0,7,3,
                1,2,6, 1,6,5,
            };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static bool IsNight(float t)
        {
            // night when near midnight or dusk/night transitions
            return t <= 0.20f || t >= 0.80f;
        }
    }

    [HarmonyPatch(typeof(Actor), "updateMovement")]
    [Phase(nameof(SavedSettings.ParticleEffects))]
    public static class ActorWalkDustPatch
    {
        static void Postfix(Actor __instance)
        {
            Environmental.EnqueueFootstep(__instance);
        }
    }

    [HarmonyPatch(typeof(Actor), "die")]
    [Phase(nameof(SavedSettings.ParticleEffects))]
    public static class ActorDieBloodPatch
    {
        static void Postfix(Actor __instance)
        {
            Environmental.SpawnBlood(__instance);
        }
    }
}
