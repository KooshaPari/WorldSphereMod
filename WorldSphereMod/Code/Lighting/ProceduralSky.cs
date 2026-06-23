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
        bool _usingVanillaShader;

        const int kSkyCubemapSize = 128;
        const float kDirtyThreshold = 0.005f;

        static Material? s_previousSkybox;
        static bool s_previousSkyboxCaptured;
        static Material? s_previousCameraSkybox;
        static bool s_previousCameraSkyboxCaptured;
        static bool s_overrodeGlobalSkybox;
        static bool s_skyGateLogged;

        static readonly int _zenith = Shader.PropertyToID("_ZenithColor");
        static readonly int _horizon = Shader.PropertyToID("_HorizonColor");
        static readonly int _ground = Shader.PropertyToID("_GroundColor");
        static readonly int _sunDir = Shader.PropertyToID("_SunDir");
        static readonly int _sunCol = Shader.PropertyToID("_SunColor");
        static readonly int _exposure = Shader.PropertyToID("_Exposure");
        static readonly int _sunGlow = Shader.PropertyToID("_SunGlow");

        static readonly int _vanillaSkyTint = Shader.PropertyToID("_SkyTint");
        static readonly int _vanillaGroundColor = Shader.PropertyToID("_GroundColor");
        static readonly int _vanillaExposure = Shader.PropertyToID("_Exposure");
        static readonly int _vanillaAtmosphere = Shader.PropertyToID("_AtmosphereThickness");
        static readonly int _vanillaSunSize = Shader.PropertyToID("_SunSize");
        static readonly int _vanillaSunConvergence = Shader.PropertyToID("_SunSizeConvergence");

        static readonly Color kZenithNight = new Color(0.05f, 0.07f, 0.18f);
        static readonly Color kZenithDawn = new Color(0.18f, 0.22f, 0.35f);
        static readonly Color kZenithNoon = new Color(0.35f, 0.5f, 0.85f);
        static readonly Color kZenithDusk = new Color(0.14f, 0.11f, 0.24f);

        public static void EnsureCreated()
        {
            if (Instance != null) return;
            if (!Core.IsWorld3D) return;
            if (!Core.savedSettings.DayNightCycle && !Core.savedSettings.HdrSkybox) return;
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

        static Shader? ResolveSkyShader(out bool isVanilla)
        {
            isVanilla = false;
            // HdrSkybox REQUIRES the bundle-only ProceduralSky shader. On 60f1 the
            // bundle only deserializes OpaqueVertexColor, so when ProceduralSky is
            // absent we skip the bundle path and degrade to Unity's built-in
            // Skybox/Procedural gradient — never reach for a missing URP/Unlit sky.
            if (!s_skyGateLogged)
            {
                s_skyGateLogged = true;
                if (!WorldSphereMod.Core.Sphere.HasBundleShader("ProceduralSky")
                    && !WorldSphereMod.Core.Sphere.HasBundleShader("ContinuumSkybox"))
                {
                    Debug.Log("[WSM3D] HdrSkybox: ProceduralSky shader unavailable at runtime — using Skybox/Procedural degraded sky.");
                }
            }
            // First check Core.Sphere.LoadedShaders dict — that's the
            // direct reference stash from Core.LoadAssets. Shader.Find
            // does NOT see bundle-loaded shaders unless they're also
            // Always-Included in Graphics Settings.
            // Belt-and-suspenders: PostFxShaderBundleAvailable=false means the
            // ProceduralSky shader was never loaded into LoadedShaders (stub-baked,
            // native-crash on GetObject). Skip cache lookup; fall through to
            // Shader.Find / Skybox/Procedural vanilla fallback. (#204)
            bool postFxGated = !WorldSphereMod.Core.Sphere.PostFxShaderBundleAvailable
                && WorldSphereMod.Core.Sphere.PostFxShaderNames.Contains("ProceduralSky");
            if (!postFxGated
                && WorldSphereMod.Core.Sphere.LoadedShaders.TryGetValue("ContinuumSkybox", out var contShader) && contShader != null)
            {
                Debug.Log("[WSM3D] ProceduralSky shader resolved via Core.Sphere.LoadedShaders cache (ContinuumSkybox).");
                return contShader;
            }
            if (!postFxGated
                && WorldSphereMod.Core.Sphere.LoadedShaders.TryGetValue("ProceduralSky", out var bundledShader) && bundledShader != null)
            {
                Debug.Log("[WSM3D] ProceduralSky shader resolved via Core.Sphere.LoadedShaders cache.");
                return bundledShader;
            }
            if (postFxGated)
            {
                Debug.Log("[WSM3D] ProceduralSky: PostFxShaderBundleAvailable=false — skipping LoadedShaders lookup, degrading to Skybox/Procedural. (#204)");
            }
            Shader? shader = Shader.Find("WorldSphereMod3D/ContinuumSkybox");
            if (shader != null)
            {
                Debug.Log("[WSM3D] ProceduralSky shader resolved via Shader.Find('WorldSphereMod3D/ContinuumSkybox').");
                return shader;
            }
            shader = Shader.Find("WSM3D/ProceduralSky");
            if (shader != null)
            {
                Debug.Log("[WSM3D] ProceduralSky shader resolved via Shader.Find('WSM3D/ProceduralSky').");
                return shader;
            }
            shader = Shader.Find("WorldSphereMod3D/ProceduralSky");
            if (shader != null)
            {
                Debug.Log("[WSM3D] ProceduralSky shader resolved via Shader.Find('WorldSphereMod3D/ProceduralSky').");
                return shader;
            }
            shader = Resources.Load<Shader>(kSkyShaderPath);
            if (shader != null)
            {
                Debug.Log($"[WSM3D] ProceduralSky shader resolved from {kSkyShaderPath}.");
                return shader;
            }
            shader = Resources.Load<Shader>(kSkyShaderFallbackPath);
            if (shader != null)
            {
                Debug.Log($"[WSM3D] ProceduralSky shader resolved from {kSkyShaderFallbackPath}.");
                return shader;
            }
            // None of the custom shaders are available (bundle corrupted or
            // not baked yet). Fall back to Unity's built-in Skybox/Procedural
            // which ships with every Unity install and supports time-of-day
            // tinting via _SkyTint, _AtmosphereThickness, _Exposure.
            shader = Shader.Find("Skybox/Procedural");
            if (shader != null)
            {
                isVanilla = true;
                Debug.LogWarning("[WSM3D] ProceduralSky: custom shaders unavailable — using Unity built-in 'Skybox/Procedural' with enhanced tinting.");
                return shader;
            }
            Debug.LogError("[WSM3D] ProceduralSky: even Unity built-in 'Skybox/Procedural' not found.");
            return null;
        }

        void Awake()
        {
            Instance = this;

            // CubemapLighting wins the skybox slot when both are active —
            // ApplyRuntimeSkybox runs in a coroutine and would otherwise
            // race against ProceduralSky.Awake binding here. Yield priority
            // so the HDR cubemap path remains authoritative.
            if (CubemapLighting.IsActive)
            {
                Debug.Log("[WSM3D] ProceduralSky yielding skybox to CubemapLighting (HdrSkybox active).");
                Destroy(this);
                return;
            }

            CaptureOriginalSkyboxState();

            Shader? shader = ResolveSkyShader(out bool isVanilla);
            if (shader == null)
            {
                Debug.LogWarning("[WSM3D] ProceduralSky shader unresolved; falling back to vanilla skybox.");
                Destroy(this);
                return;
            }

            _usingVanillaShader = isVanilla;
            _skyMat = new Material(shader) { name = "WSM3D.ProceduralSky" };
            if (_usingVanillaShader)
            {
                // Boost the vanilla shader defaults so the gradient is
                // clearly visible instead of the near-invisible default.
                _skyMat.SetColor(_vanillaSkyTint, new Color(0.35f, 0.50f, 0.85f));
                _skyMat.SetColor(_vanillaGroundColor, new Color(0.22f, 0.20f, 0.18f));
                _skyMat.SetFloat(_vanillaAtmosphere, 1.4f);
                _skyMat.SetFloat(_vanillaExposure, 1.5f);
                _skyMat.SetFloat(_vanillaSunSize, 0.06f);
                _skyMat.SetFloat(_vanillaSunConvergence, 5f);
            }
            s_overrodeGlobalSkybox = true;
            Debug.Log($"[WSM3D] ProceduralSky binding RenderSettings.skybox to '{_skyMat.name}' (vanilla={_usingVanillaShader}, shader='{shader.name}').");
            RenderSettings.skybox = _skyMat;
            Debug.Log("[WSM3D] ProceduralSky bound RenderSettings.skybox successfully.");
            SyncSkyboxComponent();
            EnsureCubemap();
        }

        void OnDestroy()
        {
            RestoreVanillaSky();
            RestoreCameraSkybox();
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
        }

        void RestoreCameraSkybox()
        {
            if (CameraManager.MainCamera == null) return;
            Skybox? skybox = CameraManager.MainCamera.GetComponent<Skybox>();
            if (skybox == null) return;
            if (_addedCameraSkybox)
            {
                Object.Destroy(skybox);
                return;
            }
            if (!s_previousCameraSkyboxCaptured)
            {
                return;
            }
            skybox.material = s_previousCameraSkybox;
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
            Color horizon = SunRig.HorizonColor(t);
            Color ground = new Color(0.1f, 0.1f, 0.1f);

            if (_usingVanillaShader)
            {
                ApplyVanilla(t, zenith, horizon, sun);
            }
            else
            {
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
            }
            if (ShouldRefreshCubemap(t))
            {
                BakeSkyCubemap();
                SyncReflections(sun, SunDriver.Sun != null ? -SunDriver.Sun.transform.forward : Vector3.down);
                _lastRenderedT = t;
            }
        }

        void ApplyVanilla(float t, Color zenith, Color horizon, Color sun)
        {
            // Unity's Skybox/Procedural has _SkyTint (tints the Rayleigh
            // scattering color), _AtmosphereThickness (1.0 = Earth-like;
            // higher = more orange at horizon), and _Exposure. We drive
            // these from the same day/night curve to produce a vivid,
            // time-varying sky gradient even without our custom HLSL.
            Color tint = Color.Lerp(zenith, horizon, 0.35f);
            _skyMat.SetColor(_vanillaSkyTint, tint);
            _skyMat.SetColor(_vanillaGroundColor, new Color(
                Mathf.Lerp(0.15f, 0.30f, Mathf.Clamp01(sun.grayscale)),
                Mathf.Lerp(0.12f, 0.26f, Mathf.Clamp01(sun.grayscale)),
                Mathf.Lerp(0.10f, 0.22f, Mathf.Clamp01(sun.grayscale))));

            // Atmosphere: thicker at dawn/dusk for warm horizon glow.
            float duskFactor = 1f - Mathf.Abs(t - 0.5f) * 2f;
            _skyMat.SetFloat(_vanillaAtmosphere, Mathf.Lerp(0.8f, 2.0f, duskFactor));
            _skyMat.SetFloat(_vanillaExposure, Mathf.Lerp(0.8f, 1.6f, Mathf.Clamp01(sun.grayscale)));
            _skyMat.SetFloat(_vanillaSunSize, 0.06f);
            _skyMat.SetFloat(_vanillaSunConvergence, 5f);

            // Vanilla Skybox/Procedural uses the scene directional light
            // automatically, so no need to set _SunDir. But we can nudge
            // the ambient light to match the sky tint for coherence.
            // Skybox ambient mode IGNORES ambientLight — force Trilight so
            // the day/night cycle actually drives ambient shading.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientLight = Color.Lerp(zenith * 0.25f, horizon * 0.4f, 0.5f);
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

            if (!_usingVanillaShader && _skyMat.HasProperty("_MainTex"))
            {
                if (_skyMat.mainTexture != _skyCubemap)
                {
                    _skyMat.mainTexture = _skyCubemap;
                }
            }
        }

        void SyncReflections(Color sunColor, Vector3 sunDir)
        {
            if (_skyCubemap == null) return;
            var water = WorldSphereMod.Water.WaterSurface.Instance;
            if (water?._renderer != null)
            {
                Material mat = water._renderer.material;
                if (mat != null)
                {
                    if (mat.HasProperty("_Tex"))
                    {
                        mat.SetTexture("_Tex", _skyCubemap);
                    }
                    else if (mat.HasProperty("_Cube"))
                    {
                        mat.SetTexture("_Cube", _skyCubemap);
                    }
                    else if (mat.HasProperty("_MainTex"))
                    {
                        mat.SetTexture("_MainTex", _skyCubemap);
                    }
                    else if (mat.HasProperty("_Cubemap"))
                    {
                        mat.SetTexture("_Cubemap", _skyCubemap);
                    }
                    else if (mat.HasProperty("_SkyCubemap"))
                    {
                        mat.SetTexture("_SkyCubemap", _skyCubemap);
                    }
                }
                mat.SetVector("_SunDir", new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
                mat.SetColor("_SunColor", sunColor);
            }
        }
    }
}
