using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Foliage;
using WorldSphereMod.Effects;
using WorldSphereMod.ProcGen;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Fx
{
    /// <summary>
    /// Phase 3 cloud path: two perpendicular quads from the cloud sprite, submitted
    /// through <see cref="MeshInstanceBatcher"/> with <see cref="FoliageMaterial"/>.
    /// Gated on <see cref="SavedSettings.CrossedQuadFoliage"/> and
    /// <see cref="EffectData.EmitCrossedQuad"/> (fx_cloud).
    /// </summary>
    public static class CloudCrossedQuadRender
    {
        const float CloudSwayAmplitude = 0.12f;

        struct CloudState
        {
            public Mesh Mesh;
            public bool SpriteWasEnabled;
            public Color BaseTint;
            public Vector3 BaseScale;
        }

        static readonly Dictionary<int, CloudState> _active = new Dictionary<int, CloudState>(32);

        public static bool IsEnabled(EffectData data) =>
            Core.IsWorld3D && Core.savedSettings.CrossedQuadFoliage && data.EmitCrossedQuad;

        public static bool IsActive(BaseEffect effect) =>
            effect != null && _active.ContainsKey(effect.GetInstanceID());

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

            int key = effect.GetInstanceID();
            if (_active.TryGetValue(key, out CloudState state))
            {
                RestoreSprite(effect, state);
                _active.Remove(key);
            }
        }

        static Transform GetRenderTransform(BaseEffect effect)
        {
            if (effect != null && effect.sprite_renderer != null
                && effect.sprite_renderer.gameObject != effect.gameObject)
            {
                return effect.sprite_renderer.transform;
            }

            return effect != null ? effect.transform : null;
        }

        static void RestoreSprite(BaseEffect effect, CloudState state)
        {
            if (!state.SpriteWasEnabled || effect?.sprite_renderer == null)
            {
                return;
            }

            effect.sprite_renderer.enabled = true;
        }

        static bool SuppressSprite(BaseEffect effect, out bool wasEnabled)
        {
            wasEnabled = false;
            if (effect?.sprite_renderer == null)
            {
                return false;
            }

            wasEnabled = effect.sprite_renderer.enabled;
            effect.sprite_renderer.enabled = false;
            return true;
        }

        static Sprite? ResolveSprite(BaseEffect effect)
        {
            if (effect?.sprite_renderer != null && effect.sprite_renderer.sprite != null)
            {
                return effect.sprite_renderer.sprite;
            }

            if (effect?.controller?.prefab != null)
            {
                SpriteRenderer prefabRenderer = effect.controller.prefab.GetComponent<SpriteRenderer>();
                if (prefabRenderer != null)
                {
                    return prefabRenderer.sprite;
                }
            }

            return null;
        }

        public static bool TryStart(BaseEffect effect, EffectData data)
        {
            if (!IsEnabled(data) || effect == null)
            {
                return false;
            }

            int key = effect.GetInstanceID();
            if (_active.ContainsKey(key))
            {
                return true;
            }

            Sprite? sprite = ResolveSprite(effect);
            if (sprite == null)
            {
                return false;
            }

            if (!FoliageMaterial.EnsureMaterial())
            {
                return false;
            }

            Material? material = FoliageMaterial.Get();
            if (material == null)
            {
                return false;
            }

            Mesh? mesh = CrossedQuadMeshCache.GetOrBuild(sprite, BuildingShape.CrossedQuad, CloudSwayAmplitude, "fx_cloud");
            if (mesh == null || mesh.vertexCount == 0)
            {
                return false;
            }

            Transform renderTransform = GetRenderTransform(effect);
            if (renderTransform == null)
            {
                return false;
            }

            SuppressSprite(effect, out bool spriteWasEnabled);

            _active[key] = new CloudState
            {
                Mesh = mesh,
                SpriteWasEnabled = spriteWasEnabled,
                BaseTint = effect.sprite_renderer != null ? effect.sprite_renderer.color : Color.white,
                BaseScale = renderTransform.localScale,
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
            if (!_active.TryGetValue(key, out CloudState state))
            {
                return;
            }

            Transform renderTransform = GetRenderTransform(effect);
            Material? material = FoliageMaterial.Get();
            if (renderTransform == null || state.Mesh == null || material == null)
            {
                RestoreSprite(effect, state);
                _active.Remove(key);
                return;
            }

            Color tint = state.BaseTint;
            if (effect.sprite_renderer != null)
            {
                tint = effect.sprite_renderer.color;
            }

            Vector3 scale = state.BaseScale;
            if (renderTransform.localScale.sqrMagnitude > 0.0001f)
            {
                scale = renderTransform.localScale;
            }

            Matrix4x4 trs = Matrix4x4.TRS(renderTransform.position, renderTransform.rotation, scale);
            MeshInstanceBatcher.Submit(state.Mesh, material, trs, tint);
        }
    }
}
