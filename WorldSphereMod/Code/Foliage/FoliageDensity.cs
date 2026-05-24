using UnityEngine;

namespace WorldSphereMod.Foliage
{
    internal static class FoliageDensity
    {
        public static bool ShouldRender(int x, int y, string? seed, float density)
        {
            density = Mathf.Clamp01(density);
            if (density >= 0.999f) return true;
            if (density <= 0f) return false;

            uint hash = Hash(x, y, seed);
            float sample = (hash & 0x00FFFFFFu) / 16777215f;
            return sample < density;
        }

        public static bool ShouldRender(Vector3 position, string? seed, float density)
        {
            return ShouldRender(
                Mathf.RoundToInt(position.x * 1000f),
                Mathf.RoundToInt(position.y * 1000f),
                seed,
                density);
        }

        static uint Hash(int x, int y, string? seed)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = Mix(hash, (uint)x);
                hash = Mix(hash, (uint)y);
                if (!string.IsNullOrEmpty(seed))
                {
                    for (int i = 0; i < seed!.Length; i++)
                    {
                        hash = Mix(hash, seed[i]);
                    }
                }
                return hash;
            }
        }

        static uint Mix(uint hash, uint value)
        {
            unchecked
            {
                hash ^= value;
                return hash * 16777619u;
            }
        }
    }
}
