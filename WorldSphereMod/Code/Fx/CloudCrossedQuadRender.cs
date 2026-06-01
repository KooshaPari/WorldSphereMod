using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Effects;
using WorldSphereMod.Foliage;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Fx
{
    /// <summary>
    /// 3D cloud path. The cloud sprite is voxelized into a puffy
    /// <see cref="ShapeHint.OrganicBlob"/> volume (the same voxelization actors
    /// and foliage use) and submitted through <see cref="MeshInstanceBatcher"/>
    /// with <see cref="FoliageMaterial"/>. The former crossed-quad / billboard
    /// path is removed entirely — clouds are real 3D voxel puffs, never flat
    /// camera-facing quads. Activated for <see cref="EffectData.EmitCrossedQuad"/>
    /// (fx_cloud) in 3D. The runtime <see cref="SavedSettings.CrossedQuadFoliage"/>
    /// check was removed (voxel-or-invisible) so a stale/off flag can't resurrect the
    /// deprecated 2D cloud billboard.
    /// </summary>
    public static class CloudCrossedQuadRender
    {
        struct CloudState
        {
            public Mesh Mesh;
            public bool SpriteWasEnabled;
            public Color BaseTint;
            public Vector3 BaseScale;
        }

        static readonly Dictionary<int, CloudState> _active = new Dictionary<int, CloudState>(32);

        public static bool IsEnabled(EffectData data) =>
            Core.IsWorld3D && data.EmitCrossedQuad;

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

            // Voxelize the cloud sprite into a 3D puff (OrganicBlob), same as
            // actors/foliage. Async build → returns a placeholder until ready;
            // an empty mesh means "not built yet", so we bail and retry next tick.
            Mesh? mesh = VoxelMeshCache.Get(sprite, ShapeHint.OrganicBlob);
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
