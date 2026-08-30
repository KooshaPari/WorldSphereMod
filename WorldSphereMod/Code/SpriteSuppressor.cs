using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Code
{
    /// <summary>
    /// Suppresses WorldBox's 2D rendering (SpriteRenderer + non-WSM3D MeshRenderer)
    /// when IsWorld3D=true. Uses Camera.onPreCull delegate for per-frame suppression,
    /// plus LateUpdate fallback for timing when the camera fires before scene objects exist.
    /// </summary>
    public static class SpriteSuppressor
    {
        private static bool _registered;
        private static bool _suppressed;
        private static bool _lateUpdateActive;
        private static int _frameCount;
        private static List<SpriteRenderer> _disabledSprites = new List<SpriteRenderer>();
        private static List<MeshRenderer> _disabledMeshRenderers = new List<MeshRenderer>();

        public static void Enable()
        {
            if (_registered) return;
            Camera.onPreCull += OnPreCullCallback;
            _registered = true;
            _lateUpdateActive = true;
            _frameCount = 0;
        }

        public static void Disable()
        {
            if (!_registered) return;
            Camera.onPreCull -= OnPreCullCallback;
            _registered = false;
            _lateUpdateActive = false;
            ReenableAll();
        }

        /// <summary>
        /// Called every LateUpdate when IsWorld3D=true. Handles the case where
        /// Camera.onPreCull fires before scene objects exist (first-frame timing).
        /// </summary>
        public static void Tick()
        {
            if (!_lateUpdateActive) return;

            _frameCount++;

            if (!Core.IsWorld3D)
            {
                if (_suppressed) ReenableAll();
                return;
            }

            // On the first few frames after world load, re-scan for renderers
            // (they may not have existed when OnPreCull first fired)
            if (_frameCount <= 5)
            {
                SuppressAll();
            }
        }

        private static void OnPreCullCallback(Camera camera)
        {
            if (!Core.IsWorld3D)
            {
                if (_suppressed) ReenableAll();
                return;
            }

            SuppressAll();
        }

        private static void SuppressAll()
        {
            // Suppress SpriteRenderers (2D sprites)
            foreach (var sr in Object.FindObjectsOfType<SpriteRenderer>())
            {
                if (sr != null && sr.enabled)
                {
                    sr.enabled = false;
                    _disabledSprites.Add(sr);
                }
            }

            // Suppress non-WSM3D MeshRenderers (WorldBox heightfield tilemap, etc.)
            // Only suppress renderers whose GameObject name does NOT start with "WSM3D"
            foreach (var mr in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (mr != null && mr.enabled)
                {
                    string goName = mr.gameObject.name;
                    if (!goName.StartsWith("WSM3D"))
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
    }
}
