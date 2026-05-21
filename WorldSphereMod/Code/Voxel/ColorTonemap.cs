using UnityEngine;

namespace WorldSphereMod.Voxel
{
    public static class ColorTonemap
    {
        public static Color32 Tonemap(Color32 color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            s = Mathf.Min(s, 0.85f);
            v = Mathf.Min(v, 0.92f);

            Color mapped = Color.HSVToRGB(h, s, v);
            return new Color32(
                (byte)(mapped.r * 255f),
                (byte)(mapped.g * 255f),
                (byte)(mapped.b * 255f),
                color.a);
        }
    }
}
