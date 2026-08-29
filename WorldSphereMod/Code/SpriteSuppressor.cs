using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace WorldSphereMod.Code
{
    /// <summary>
    /// Camera.OnPreCull hook: disables SpriteRenderer, MeshRenderer (non-WSM3D),
    /// and TilemapRenderer components in the active scene when 3D mode is on.
    /// This stops WorldBox's 2D HeightField cylinder + actor sprites from rendering
    /// on top of our 3D voxel/sphere meshes.
    ///
    /// Cache is invalidated when scene reloads. WSM3D-owned renderers are skipped
    /// so the procedural sphere and 3D actor meshes remain visible.
    /// </summary>
    [HarmonyPatch(typeof(Camera))]
    public static class SpriteSuppressor
    {
        private static List<SpriteRenderer> _sprites = new List<SpriteRenderer>();
        private static List<MeshRenderer> _meshRenderers = new List<MeshRenderer>();
        private static List<TilemapRenderer> _tilemapRenderers = new List<TilemapRenderer>();
        private static bool _scanned;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Camera))]
        public static void OnPreCull_Postfix(Camera __instance)
        {
            if (Core.savedSettings == null) return;
            if (!Core.IsWorld3D) return;
            // Only operate on the main camera (the viewport that contains WSM3D's 3D output)
            if (__instance != Camera.main && __instance.name != "Main Camera") return;

            if (!_scanned)
            {
                _sprites.Clear();
                _meshRenderers.Clear();
                _tilemapRenderers.Clear();
                Scan();
                _scanned = true;
            }

            // Disable 2D sprites and HeightField cylinder mesh in one pass
            for (int i = 0; i < _sprites.Count; i++)
                if (_sprites[i] != null && _sprites[i].enabled) _sprites[i].enabled = false;
            for (int i = 0; i < _tilemapRenderers.Count; i++)
                if (_tilemapRenderers[i] != null && _tilemapRenderers[i].enabled) _tilemapRenderers[i].enabled = false;
            for (int i = 0; i < _meshRenderers.Count; i++)
            {
                var mr = _meshRenderers[i];
                if (mr == null || !mr.enabled) continue;
                // Skip WSM3D-managed meshes (sphere, actors)
                var go = mr.gameObject;
                if (go != null && (go.name.Contains("WSM3D") || go.name.Contains("CompoundSphere"))) continue;
                mr.enabled = false;
            }
        }

        private static void Scan()
        {
            // SpriteRenderers = actor billboards, world UI sprites
            var sprites = Object.FindObjectsOfType<SpriteRenderer>(includeInactive: true);
            for (int i = 0; i < sprites.Length; i++)
            {
                var sr = sprites[i];
                if (sr == null) continue;
                var name = sr.gameObject.name;
                if (name.Contains("WSM3D") || name.Contains("CompoundSphere")) continue;
                _sprites.Add(sr);
            }

            // TilemapRenderers = WorldBox's HeightField tiles (the cylinder)
            var tilemaps = Object.FindObjectsOfType<TilemapRenderer>(includeInactive: true);
            for (int i = 0; i < tilemaps.Length; i++)
            {
                if (tilemaps[i] != null) _tilemapRenderers.Add(tilemaps[i]);
            }

            // MeshRenderers not owned by WSM3D — covers HeightField + any other non-mod meshes
            var meshes = Object.FindObjectsOfType<MeshRenderer>(includeInactive: true);
            for (int i = 0; i < meshes.Length; i++)
            {
                var mr = meshes[i];
                if (mr == null) continue;
                var go = mr.gameObject;
                if (go == null) continue;
                if (go.name.Contains("WSM3D") || go.name.Contains("CompoundSphere")) continue;
                _meshRenderers.Add(mr);
            }
        }

        public static void InvalidateCache()
        {
            _scanned = false;
            _sprites.Clear();
            _meshRenderers.Clear();
            _tilemapRenderers.Clear();
        }
    }

    [HarmonyPatch(typeof(MapBox), "finishMakingWorld")]
    public static class SpriteSuppressor_WorldReload
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            SpriteSuppressor.InvalidateCache();
        }
    }
}
