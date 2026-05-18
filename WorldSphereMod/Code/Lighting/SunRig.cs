using UnityEngine;

namespace WorldSphereMod.Lighting
{
    public static class SunRig
    {
        static Light? _sun;

        public static void Bind(Light sun) { _sun = sun; }

        public static void Drive(float t)
        {
            if (_sun == null) return;
            _sun.color = SunColor(t);
            RenderSettings.ambientLight = AmbientColor(t);
        }

        public static Color SunColor(float t)
        {
            Color night = new Color(0.17f, 0.23f, 0.40f);
            Color dawn  = new Color(1.0f, 0.61f, 0.31f);
            Color noon  = new Color(1.0f, 0.96f, 0.88f);
            Color dusk  = new Color(1.0f, 0.42f, 0.21f);
            if (t < 0.25f) return Color.Lerp(night, dawn, t / 0.25f);
            if (t < 0.5f)  return Color.Lerp(dawn,  noon, (t - 0.25f) / 0.25f);
            if (t < 0.75f) return Color.Lerp(noon,  dusk, (t - 0.5f) / 0.25f);
            return Color.Lerp(dusk, night, (t - 0.75f) / 0.25f);
        }

        public static Color AmbientColor(float t)
        {
            Color c = SunColor(t);
            return new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.45f, 1f);
        }
    }
}
