using UnityEngine;

namespace WorldSphereMod.Code
{
    /// <summary>
    /// Camera.OnPreCull hook: disables all SpriteRenderer components in the active
    /// scene when the 3D mode gate (Core.IsWorld3D) is open. This stops WorldBox's
    /// 2D tilemap / actor sprite layer from being visible alongside our 3D voxels.
    ///
    /// Re-enabled when 3D mode is off so the regular 2D render still works.
    /// Component cache is invalidated when a new SpriteRenderer enters the scene.
    /// </summary>
    public static class SpriteSuppressor
    {
        private static SpriteRenderer[] _cache;
        private static bool _cacheValid;

        public static void OnCameraPreCull(Camera cam)
        {
            if (!Core.IsWorld3D)
            {
                if (!_cacheValid) return;
                for (int i = 0; i < _cache.Length; i++)
                {
                    if (_cache[i] != null) _cache[i].enabled = true;
                }
                return;
            }

            if (!_cacheValid)
            {
                _cache = Object.FindObjectsOfType<SpriteRenderer>(includeInactive: true);
                _cacheValid = true;
            }

            for (int i = 0; i < _cache.Length; i++)
            {
                var sr = _cache[i];
                if (sr == null) continue;
                if (sr.enabled) sr.enabled = false;
            }
        }

        public static void InvalidateCache()
        {
            _cache = null;
            _cacheValid = false;
        }
    }
}
