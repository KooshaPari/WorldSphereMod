using UnityEngine;
using UnityEngine.Rendering;

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

            // RenderSettings.ambientLight is IGNORED when ambientMode is
            // Trilight or Skybox — writing it leaves entity shading frozen
            // at neutral white. Drive the Trilight bands directly so the
            // day/night curve actually modulates ambient SH lighting.
            Color zenith = ZenithColor(t);
            Color horizon = HorizonColor(t);
            Color ground = new Color(horizon.r * 0.4f, horizon.g * 0.4f, horizon.b * 0.4f, 1f);
            float intensity = Mathf.Lerp(0.10f, 0.50f, Mathf.Clamp01(SunColor(t).grayscale));

            if (RenderSettings.ambientMode != AmbientMode.Trilight)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
            }
            RenderSettings.ambientSkyColor = zenith;
            RenderSettings.ambientEquatorColor = horizon;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientIntensity = intensity;
        }

        static readonly Color kZenithNight = new Color(0.05f, 0.07f, 0.18f);
        static readonly Color kZenithDawn = new Color(0.18f, 0.22f, 0.35f);
        static readonly Color kZenithNoon = new Color(0.35f, 0.5f, 0.85f);
        static readonly Color kZenithDusk = new Color(0.14f, 0.11f, 0.24f);

        public static Color ZenithColor(float t) =>
            SampleSkyCurve(t, kZenithNight, kZenithDawn, kZenithNoon, kZenithDusk);

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
