using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Code
{
    /// <summary>
    /// Registers a Camera.onPreCull delegate that disables WorldBox's 2D
    /// SpriteRenderer components when IsWorld3D=true.
    ///
    /// Camera.onPreCull is a PUBLIC STATIC Camera.CameraCallback delegate
    /// - it can be registered directly via C# delegate += (unlike
    /// Camera.OnPreCull which is a Unity MESSAGE and cannot be Harmony-patched).
    /// </summary>
    public static class SpriteSuppressor
    {
        private static bool _registered;
        private static bool _suppressed;
        private static List<SpriteRenderer> _disabledSprites = new List<SpriteRenderer>();

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

            foreach (var sr in Object.FindObjectsOfType<SpriteRenderer>())
            {
                if (sr != null && sr.enabled)
                {
                    sr.enabled = false;
                    _disabledSprites.Add(sr);
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
            _suppressed = false;
        }

        public static void InvalidateCache()
        {
            _disabledSprites.Clear();
            _suppressed = false;
        }
    }
}
