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
            Color amb = SunRig.AmbientColor(t);

            Color zenith = Color.Lerp(new Color(0.05f, 0.07f, 0.18f), new Color(0.35f, 0.5f, 0.85f), Mathf.Sin(t * Mathf.PI));
            Color horizon = Color.Lerp(sun * 0.5f, sun * 0.9f, Mathf.Sin(t * Mathf.PI));
            Color ground = new Color(0.1f, 0.1f, 0.1f);

            _skyMat.SetColor(_zenith, zenith);
            _skyMat.SetColor(_horizon, horizon);
            _skyMat.SetColor(_ground, ground);
            _skyMat.SetColor(_sunCol, sun);
            if (SunDriver.Sun != null)
                _skyMat.SetVector(_sunDir, SunDriver.Sun.transform.forward * -1f);
        }
    }
}
