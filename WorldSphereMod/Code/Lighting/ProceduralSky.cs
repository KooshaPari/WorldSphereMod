using UnityEngine;
using UnityEngine.Rendering;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.Lighting
{
    public sealed class ProceduralSky : MonoBehaviour
    {
        static ProceduralSky? Instance;
        const string kSkyShaderPath = "Shaders/ContinuumSkybox";
        const string kSkyShaderFallbackPath = "Shaders/ProceduralSky";
        Material? _skyMat;
        RenderTexture? _skyCubemap;
        Camera? _bakeCamera;
        float _lastRenderedT = -1f;
        bool _addedCameraSkybox;

        const int kSkyCubemapSize = 128;
        const float kDirtyThreshold = 0.005f;

        static Material? s_previousSkybox;
        static bool s_previousSkyboxCaptured;
        static Material? s_previousCameraSkybox;
        static bool s_previousCameraSkyboxCaptured;
        static bool s_overrodeGlobalSkybox;

        static readonly int _zenith = Shader.PropertyToID("_ZenithColor");
        static readonly int _horizon = Shader.PropertyToID("_HorizonColor");
        static readonly int _ground = Shader.PropertyToID("_GroundColor");
        static readonly int _sunDir = Shader.PropertyToID("_SunDir");
        static readonly int _sunCol = Shader.PropertyToID("_SunColor");
        static readonly int _exposure = Shader.PropertyToID("_Exposure");
        static readonly int _sunGlow = Shader.PropertyToID("_SunGlow");

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

        public static void ApplySetting(bool enabled)
        {
            if (enabled)
            {
                EnsureCreated();
                return;
            }
            if (Instance != null)
            {
                Destroy(Instance);
            }
            else
            {
                RestoreVanillaSky();
            }
        }

        static Shader? ResolveSkyShader()
        {
            Shader? shader = Resources.Load<Shader>(kSkyShaderPath);
            if (shader != null)
            {
                Debug.Log($"[WSM3D] ProceduralSky shader resolved from {kSkyShaderPath}.");
                return shader;
            }
            Debug.LogWarning($"[WSM3D] ProceduralSky shader fallback: '{kSkyShaderPath}' missing, trying '{kSkyShaderFallbackPath}'.");
            shader = Resources.Load<Shader>(kSkyShaderFallbackPath);
            if (shader != null)
            {
                Debug.Log($"[WSM3D] ProceduralSky shader fallback resolved from {kSkyShaderFallbackPath}.");
            }
            return shader;
        }

        void Awake()
        {
            Instance = this;
            CaptureOriginalSkyboxState();

            Shader? shader = ResolveSkyShader();
            if (shader == null)
            {
                Debug.LogWarning("[WSM3D] ProceduralSky shader unresolved; falling back to vanilla skybox.");
                return;
            }

            _skyMat = new Material(shader) { name = "WSM3D.ProceduralSky" };
            s_overrodeGlobalSkybox = true;
            RenderSettings.skybox = _skyMat;
            SyncSkyboxComponent();
            EnsureCubemap();
        }

        void OnDestroy()
        {
            RestoreVanillaSky();
            if (Instance == this) Instance = null;
            if (_skyMat != null) Object.Destroy(_skyMat);
            if (_skyCubemap != null) Object.Destroy(_skyCubemap);
            if (_bakeCamera != null) Object.Destroy(_bakeCamera.gameObject);
        }

        static void CaptureOriginalSkyboxState()
        {
            if (!s_previousSkyboxCaptured)
            {
                s_previousSkybox = RenderSettings.skybox;
                s_previousSkyboxCaptured = true;
            }
        }

        static void RestoreVanillaSky()
        {
            if (s_overrodeGlobalSkybox)
            {
                RenderSettings.skybox = s_previousSkybox;
                s_overrodeGlobalSkybox = false;
            }
            Camera? mainCamera = CameraManager.MainCamera;
            if (mainCamera == null) return;
            Skybox? skybox = mainCamera.GetComponent<Skybox>();
            if (skybox == null) return;

            if (!s_previousCameraSkyboxCaptured)
            {
                return;
            }
            if (s_previousCameraSkybox == null && skybox != null && (skybox.name == "WSM3D.ProceduralSky" || skybox.material == null))
            {
                Object.Destroy(skybox);
            }
            else
            {
                skybox.material = s_previousCameraSkybox;
            }
        }

        static void CaptureOriginalCameraSkybox()
        {
            if (s_previousCameraSkyboxCaptured)
            {
                return;
            }
            if (CameraManager.MainCamera == null)
            {
                return;
            }
            Skybox? skybox = CameraManager.MainCamera.GetComponent<Skybox>();
            s_previousCameraSkybox = skybox?.material;
            s_previousCameraSkyboxCaptured = true;
        }

        void LateUpdate()
        {
            if (_skyMat == null) return;
            Apply(TimeOfDay.Current);
        }

        void Apply(float t)
        {
            if (_skyMat == null) return;
            SyncSkyboxComponent();

            Color sun = SunRig.SunColor(t);
            Color zenith = SampleSkyCurve(t, kZenithNight, kZenithDawn, kZenithNoon, kZenithDusk);
            Color horizon = SampleSkyCurve(t, kHorizonNight, kHorizonDawn, kHorizonNoon, kHorizonDusk);
            Color ground = new Color(0.1f, 0.1f, 0.1f);

            _skyMat.SetColor(_zenith, zenith);
            _skyMat.SetColor(_horizon, horizon);
            _skyMat.SetColor(_ground, ground);
            _skyMat.SetColor(_sunCol, sun);
            _skyMat.SetFloat(_exposure, 1.25f);
            _skyMat.SetFloat(_sunGlow, 0.9f);
            if (SunDriver.Sun != null)
            {
                _skyMat.SetVector(_sunDir, SunDriver.Sun.transform.forward * -1f);
            }
            if (ShouldRefreshCubemap(t))
            {
                BakeSkyCubemap();
                SyncReflections(sun, SunDriver.Sun != null ? -SunDriver.Sun.transform.forward : Vector3.down);
                _lastRenderedT = t;
            }
        }

        static Color SampleSkyCurve(float t, Color night, Color dawn, Color noon, Color dusk)
        {
            if (t < 0.25f) return Color.Lerp(night, dawn, t / 0.25f);
            if (t < 0.5f) return Color.Lerp(dawn, noon, (t - 0.25f) / 0.25f);
            if (t < 0.75f) return Color.Lerp(noon, dusk, (t - 0.5f) / 0.25f);
            return Color.Lerp(dusk, night, (t - 0.75f) / 0.25f);
        }

        void SyncSkyboxComponent()
        {
            if (CameraManager.MainCamera == null) return;
            CaptureOriginalCameraSkybox();
            Skybox? skybox = CameraManager.MainCamera.GetComponent<Skybox>();
            if (skybox == null)
            {
                skybox = CameraManager.MainCamera.gameObject.AddComponent<Skybox>();
                _addedCameraSkybox = true;
            }
            skybox.material = _skyMat;
        }

        void EnsureCubemap()
        {
            if (_skyCubemap != null) return;

            var desc = new RenderTextureDescriptor(kSkyCubemapSize, kSkyCubemapSize, RenderTextureFormat.ARGBHalf, 16)
            {
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = true,
                msaaSamples = 1,
                sRGB = false
            };
            _skyCubemap = new RenderTexture(desc)
            {
                name = "WSM3D.SkyCubemap",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _skyCubemap.Create();

            GameObject bakeGo = new GameObject("WSM3D.SkyCubemapCamera");
            bakeGo.hideFlags = HideFlags.HideAndDontSave;
            bakeGo.SetActive(false);
            _bakeCamera = bakeGo.AddComponent<Camera>();
            _bakeCamera.enabled = false;
            _bakeCamera.clearFlags = CameraClearFlags.Skybox;
            _bakeCamera.cullingMask = 0;
            _bakeCamera.allowHDR = true;
            _bakeCamera.allowMSAA = false;
            _bakeCamera.backgroundColor = Color.black;
            _bakeCamera.transform.position = Vector3.zero;
        }

        bool ShouldRefreshCubemap(float t)
        {
            if (_skyCubemap == null || _bakeCamera == null) return false;
            if (_lastRenderedT < 0f) return true;
            float delta = Mathf.Abs(Mathf.DeltaAngle(_lastRenderedT * 360f, t * 360f)) / 360f;
            return delta >= kDirtyThreshold;
        }

        void BakeSkyCubemap()
        {
            if (_skyCubemap == null || _bakeCamera == null) return;
            if (_skyMat == null) return;

            SyncSkyboxComponent();
            _bakeCamera.transform.position = CameraManager.MainCamera != null
                ? CameraManager.MainCamera.transform.position
                : Vector3.zero;

            if (!_bakeCamera.RenderToCubemap(_skyCubemap))
            {
                Debug.LogWarning("[WSM3D] ProceduralSky cubemap bake failed; keeping previous reflections.");
                return;
            }

            if (_skyMat.mainTexture != _skyCubemap)
            {
                _skyMat.mainTexture = _skyCubemap;
            }
        }

        void SyncReflections(Color sunColor, Vector3 sunDir)
        {
            if (_skyCubemap == null) return;
            var water = WorldSphereMod.Water.WaterSurface.Instance;
            if (water?._renderer != null)
            {
                Material mat = water._renderer.material;
                mat.SetTexture("_SkyCubemap", _skyCubemap);
                mat.SetVector("_SunDir", new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
                mat.SetColor("_SunColor", sunColor);
            }
        }
    }
}
