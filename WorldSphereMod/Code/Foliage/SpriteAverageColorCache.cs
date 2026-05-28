using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Foliage
{
    /// <summary>
    /// Per-sprite average-opaque-pixel-color cache. Foliage materials need a
    /// per-instance tint because the mesh path is shape-only (crossed quads
    /// with white vertex colors + sway-amplitude in alpha); without a sampled
    /// tint every oak/pine/palm/bush draws the same color. This is the
    /// foliage analog of BuildingMeshGen.AverageColor — pulled into a shared
    /// keyed-by-sprite-instance cache because FoliageTileRender hits the
    /// same atlas frame hundreds of times per frame.
    /// </summary>
    internal static class SpriteAverageColorCache
    {
        const byte AlphaThreshold = 8;
        static readonly Dictionary<int, Color> _cache = new Dictionary<int, Color>(256);
        static readonly Color _fallback = new Color(0.45f, 0.6f, 0.32f, 1f); // sober foliage green

        public static Color Sample(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return _fallback;
            int id = sprite.GetInstanceID();
            if (_cache.TryGetValue(id, out var cached)) return cached;

            Color result = _fallback;
            try
            {
                if (!sprite.texture.isReadable)
                {
                    _cache[id] = result;
                    return result;
                }

                Rect tr = sprite.textureRect;
                int w = Mathf.Max(1, (int)tr.width);
                int h = Mathf.Max(1, (int)tr.height);
                int x0 = (int)tr.x;
                int y0 = (int)tr.y;
                int texW = sprite.texture.width;

                Color32[] full = WorldSphereMod.Voxel.SpriteVoxelizer.GetPixelsCached(sprite.texture);
                if (full == null)
                {
                    _cache[id] = result;
                    return result;
                }

                float r = 0f, g = 0f, b = 0f;
                int count = 0;
                for (int y = 0; y < h; y++)
                {
                    int row = (y0 + y) * texW + x0;
                    for (int x = 0; x < w; x++)
                    {
                        Color32 c = full[row + x];
                        if (c.a <= AlphaThreshold) continue;
                        r += c.r; g += c.g; b += c.b;
                        count++;
                    }
                }
                if (count > 0)
                {
                    float inv = 1f / (count * 255f);
                    result = new Color(r * inv, g * inv, b * inv, 1f);
                }
            }
            catch
            {
                // Atlas eviction or unreadable texture — keep the fallback.
            }
            _cache[id] = result;
            return result;
        }

        public static void Clear() => _cache.Clear();
    }
}
