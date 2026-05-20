using UnityEngine;

namespace WorldSphereMod.Lighting
{
    public sealed class ProceduralSky : MonoBehaviour
    {
        static ProceduralSky? Instance;
        Material? _skyMat;

        static readonly int _zenith = Shader.PropertyToID("_ZenithColor");
        static readonly int _horizon = Shader.PropertyToID("_HorizonColor");
        static readonly int _ground = Shader.PropertyToID("_GroundColor");
        static readonly int _sunDir = Shader.PropertyToID("_SunDir");
        static readonly int _sunCol = Shader.PropertyToID("_SunColor");

        static readonly Color kZenithNight = new Color(0.05f, 0.07f, 0.18f);
        static readonly Color kZenithDawn = new Color(0.18f, 0.22f, 0.35f);
        static readonly Color kZenithNoon = new Color(0.35f, 0.5f, 0.85f);
        static readonly Color kZenithDusk = new Color(0.14f, 0.11f, 0.24f);

        static readonly Color kHorizonNight = new Color(0.05f, 0.06f, 0.1f);
        static readonly Color kHorizonDawn = new Color(0.98f, 0.55f, 0.28f);
        static readonly Color kHorizonNoon = new Color(0.62f, 0.78f, 0.98f);
        static readonly Color kHorizonDusk = new Color(0.82f, 0.3f, 0.38f);

        public static void EnsureCreated()
        {
            if (Instance != null) return;
            if (!Core.IsWorld3D || !Core.savedSettings.DayNightCycle) return;
            if (Mod.Object == null) return;
            Mod.Object.AddComponent<ProceduralSky>();
        }

        void Awake()
        {
            Instance = this;
            Shader? s = Resources.Load<Shader>("Shaders/ProceduralSky");
            if (s == null) { Debug.LogWarning("[WSM3D] ProceduralSky shader not found; skybox unchanged."); return; }
            _skyMat = new Material(s) { name = "WSM3D.ProceduralSky" };
            RenderSettings.skybox = _skyMat;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_skyMat != null) Object.Destroy(_skyMat);
        }

        void LateUpdate()
        {
            if (_skyMat == null) return;
            float t = TimeOfDay.Current;

            Color sun = SunRig.SunColor(t);
            Color zenith = SampleSkyCurve(t, kZenithNight, kZenithDawn, kZenithNoon, kZenithDusk);
            Color horizon = SampleSkyCurve(t, kHorizonNight, kHorizonDawn, kHorizonNoon, kHorizonDusk);
            Color ground = new Color(0.1f, 0.1f, 0.1f);

            _skyMat.SetColor(_zenith, zenith);
            _skyMat.SetColor(_horizon, horizon);
            _skyMat.SetColor(_ground, ground);
            _skyMat.SetColor(_sunCol, sun);
            if (SunDriver.Sun != null)
                _skyMat.SetVector(_sunDir, SunDriver.Sun.transform.forward * -1f);
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
