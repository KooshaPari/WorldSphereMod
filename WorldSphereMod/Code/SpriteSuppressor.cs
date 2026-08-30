using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Code
{
    /// <summary>
    /// Registers a Camera.onPreCull delegate that disables WorldBox's 2D
    /// rendering (SpriteRenderer + MeshRenderer) when IsWorld3D=true.
    /// </summary>
    public static class SpriteSuppressor
    {
        private static bool _registered;
        private static bool _suppressed;
        private static List<SpriteRenderer> _disabledSprites = new List<SpriteRenderer>();
        private static List<MeshRenderer> _disabledMeshRenderers = new List<MeshRenderer>();

        public static void Enable()
        {
            if (_registered) return;
            Camera.onPreCull += OnPreCullCallback;
            _registered = true;
        }

        public static void Disable()
        {
            if (!_registered) return;
            Camera.onPreCull -= OnPreCullCallback;
            _registered = false;
            ReenableAll();
        }

        private static void OnPreCullCallback(Camera camera)
        {
            if (!Core.IsWorld3D)
            {
                if (_suppressed) ReenableAll();
                return;
            }

            if (_suppressed) return;

            // Suppress SpriteRenderers (2D sprites)
            foreach (var sr in Object.FindObjectsOfType<SpriteRenderer>())
            {
                if (sr != null && sr.enabled)
                {
                    sr.enabled = false;
                    _disabledSprites.Add(sr);
                }
            }

            // Suppress MeshRenderers matching WorldBox 2D patterns
            foreach (var mr in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (mr != null && mr.enabled)
                {
                    string goName = mr.gameObject.name;
                    if (goName.Contains("TileMap") || goName.Contains("HeightField") ||
                        goName.Contains("WorldMap") || goName.Contains("Height") ||
                        goName.StartsWith("map_") || goName.StartsWith("tile_") ||
                        goName.Contains("Background") || goName.Contains("BG"))
                    {
                        mr.enabled = false;
                        _disabledMeshRenderers.Add(mr);
                    }
                }
            }

            _suppressed = true;
        }

        private static void ReenableAll()
        {
            foreach (var sr in _disabledSprites)
            {
                if (sr != null) sr.enabled = true;
            }
            _disabledSprites.Clear();

            foreach (var mr in _disabledMeshRenderers)
            {
                if (mr != null) mr.enabled = true;
            }
            _disabledMeshRenderers.Clear();

            _suppressed = false;
        }

        public static void InvalidateCache()
        {
            _disabledSprites.Clear();
            _disabledMeshRenderers.Clear();
            _suppressed = false;
        }
    }
}
