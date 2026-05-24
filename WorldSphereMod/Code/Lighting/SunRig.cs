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

        public static Color SunColor(float t) =>
            SampleSkyCurve(t,
                new Color(0.17f, 0.23f, 0.40f),
                new Color(1.0f, 0.61f, 0.31f),
                new Color(1.0f, 0.96f, 0.88f),
                new Color(1.0f, 0.42f, 0.21f));

        public static Color AmbientColor(float t)
        {
            Color c = SunColor(t);
            return new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.45f, 1f);
        }

        static readonly Color kHorizonNight = new Color(0.05f, 0.06f, 0.1f);
        static readonly Color kHorizonDawn = new Color(0.98f, 0.55f, 0.28f);
        static readonly Color kHorizonNoon = new Color(0.62f, 0.78f, 0.98f);
        static readonly Color kHorizonDusk = new Color(0.82f, 0.3f, 0.38f);

        /// <summary>Phase-8 horizon keyframe; shared with <see cref="ProceduralSky"/>.</summary>
        public static Color HorizonColor(float t) =>
            SampleSkyCurve(t, kHorizonNight, kHorizonDawn, kHorizonNoon, kHorizonDusk);

        /// <summary>Depth-fog tint aligned with the procedural skybox horizon line.</summary>
        public static Color FogColor(float t)
        {
            Color horizon = HorizonColor(t);
            Color ambient = AmbientColor(t);
            return Color.Lerp(ambient, horizon, 0.6f);
        }

        static Color SampleSkyCurve(float t, Color night, Color dawn, Color noon, Color dusk)
        {
            if (t < 0.25f) return Color.Lerp(night, dawn, t / 0.25f);
            if (t < 0.5f) return Color.Lerp(dawn, noon, (t - 0.25f) / 0.25f);
            if (t < 0.75f) return Color.Lerp(noon, dusk, (t - 0.5f) / 0.25f);
            return Color.Lerp(dusk, night, (t - 0.75f) / 0.25f);
        }
    }
}
