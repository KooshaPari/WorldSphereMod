using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Fx
{
    /// <summary>
    /// Short-lived voxel burst state for effect sprites. Spawn hooks capture the
    /// sprite once, cache the voxel mesh via <see cref="VoxelMeshCache.Get"/>, hide
    /// the original billboard, and then resubmit the mesh each update with a
    /// spawn-grow-fade envelope until the effect deactivates.
    /// </summary>
    public static class VoxelParticleBurst
    {
        const float DefaultDurationSeconds = 1.5f;
        const float ScaleMultiplier = 2.0f;

        struct BurstProfile
        {
            public readonly float Duration;
            public readonly float GrowPortion;
            public readonly float FadePortion;

            public BurstProfile(float duration, float growPortion, float fadePortion)
            {
                Duration = duration;
                GrowPortion = growPortion;
                FadePortion = fadePortion;
            }
        }

        struct BurstState
        {
            public Mesh Mesh;
            public Material Material;
            public Vector3 BaseScale;
            public Color BaseTint;
            public float StartedAt;
            public BurstProfile Profile;
        }

        static readonly Dictionary<int, BurstState> _active = new Dictionary<int, BurstState>(16);
        static readonly Dictionary<string, BurstProfile> _profiles = new Dictionary<string, BurstProfile>
        {
            ["fx_meteorite"] = new BurstProfile(DefaultDurationSeconds, 0.18f, 0.70f),
            ["fx_explosion_wave"] = new BurstProfile(DefaultDurationSeconds, 0.15f, 0.60f),
            ["fx_fire_smoke"] = new BurstProfile(DefaultDurationSeconds, 0.20f, 0.58f),
            ["fx_antimatter_effect"] = new BurstProfile(DefaultDurationSeconds, 0.18f, 0.66f),
            ["fx_napalm_flash"] = new BurstProfile(DefaultDurationSeconds, 0.12f, 0.52f),
            ["fx_cloud"] = new BurstProfile(DefaultDurationSeconds, 0.18f, 0.62f),
        };

        static Transform GetRenderTransform(BaseEffect effect)
        {
            if (effect != null && effect.sprite_renderer != null && effect.sprite_renderer.gameObject != effect.gameObject)
            {
                return effect.sprite_renderer.transform;
            }

            return effect != null ? effect.transform : null;
        }

        public static void Clear()
        {
            _active.Clear();
        }

        public static void Clear(BaseEffect effect)
        {
            if (effect == null)
            {
                return;
            }

            _active.Remove(effect.GetInstanceID());
        }

        public static bool TryStart(BaseEffect effect)
        {
            if (!Core.IsWorld3D || !Core.savedSettings.ParticleEffects)
            {
                return false;
            }

            if (effect == null || effect.controller == null || effect.controller.asset == null)
            {
                return false;
            }

            string effectId = effect.controller.asset.id;
            if (string.IsNullOrEmpty(effectId) || !_profiles.TryGetValue(effectId, out BurstProfile profile))
            {
                return false;
            }

            Sprite sprite = effect.sprite_renderer != null ? effect.sprite_renderer.sprite : null;
            if (sprite == null && effect.controller.prefab != null)
            {
                SpriteRenderer prefabRenderer = effect.controller.prefab.GetComponent<SpriteRenderer>();
                if (prefabRenderer != null)
                {
                    sprite = prefabRenderer.sprite;
                }
            }

            Mesh mesh = VoxelMeshCache.Get(sprite);
            if (mesh == null || mesh.vertexCount == 0)
            {
                return false;
            }

            Material material = VoxelRender.GetResolvedMaterial();
            if (material == null)
            {
                return false;
            }

            Transform renderTransform = GetRenderTransform(effect);
            if (renderTransform == null)
            {
                return false;
            }

            int key = effect.GetInstanceID();
            _active[key] = new BurstState
            {
                Mesh = mesh,
                Material = material,
                BaseScale = renderTransform.localScale * ScaleMultiplier,
                BaseTint = effect.sprite_renderer != null ? effect.sprite_renderer.color : Color.white,
                StartedAt = Time.time,
                Profile = profile,
            };

            return true;
        }

        public static void Update(BaseEffect effect)
        {
            if (effect == null)
            {
                return;
            }

            int key = effect.GetInstanceID();
            if (!_active.TryGetValue(key, out BurstState state))
            {
                return;
            }

            Transform renderTransform = GetRenderTransform(effect);
            if (renderTransform == null || state.Mesh == null || state.Material == null)
            {
                _active.Remove(key);
                return;
            }

            float elapsed = Time.time - state.StartedAt;
            float duration = Mathf.Max(0.01f, state.Profile.Duration > 0f ? state.Profile.Duration : DefaultDurationSeconds);
            float t = Mathf.Clamp01(elapsed / duration);

            if (t >= 1f)
            {
                _active.Remove(key);
                return;
            }

            float growT = state.Profile.GrowPortion > 0f
                ? Mathf.Clamp01(t / state.Profile.GrowPortion)
                : 1f;
            float grow = Mathf.SmoothStep(0.12f, 1f, growT);

            float fade = 1f;
            if (t >= state.Profile.FadePortion)
            {
                float fadeT = Mathf.InverseLerp(state.Profile.FadePortion, 1f, t);
                fade = 1f - Mathf.SmoothStep(0f, 1f, fadeT);
            }

            float alpha = Mathf.Clamp01(grow * fade);
            Vector3 scale = state.BaseScale * Mathf.Max(0.05f, grow);
            Color tint = state.BaseTint;
            tint.a *= alpha;

            Matrix4x4 trs = Matrix4x4.TRS(renderTransform.position, renderTransform.rotation, scale);
            MeshInstanceBatcher.Submit(state.Mesh, state.Material, trs, tint);
        }
    }
}
