using UnityEngine;

namespace WorldSphereMod.Voxel
{
    public static class ColorTonemap
    {
        public static Color32 Tonemap(Color32 color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            s = Mathf.Min(s, 0.85f);
            // Floor + ceil brightness so dark sprite pixels (tree shadows,
            // night-time actor sprites) don't bake into pitch-black voxels.
            // User reported black trees on bright green terrain despite
            // material emission fixes — root cause was vertex colors stored
            // dark green (v~0.05) from tree-canopy sprite pixels. Standard
            // shader with low-emission base * low vertex color = ~0 output.
            v = Mathf.Clamp(v, 0.40f, 0.92f);

            Color mapped = Color.HSVToRGB(h, s, v);
            return new Color32(
                (byte)(mapped.r * 255f),
                (byte)(mapped.g * 255f),
                (byte)(mapped.b * 255f),
                color.a);
        }
    }
}
