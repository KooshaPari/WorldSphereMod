using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.PostFx
{
    public sealed class WSM3DPostStack : MonoBehaviour
    {
        static WSM3DPostStack _instance;
        static readonly int BloomTexId = Shader.PropertyToID("_BloomTex");
        static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        Material _ssaoMat;
        Material _ssgiMat;
        Material _bloomMat;
        Material _acesMat;
        Material _lutMat;
        Texture2D _lutTexture;
        RenderTexture _ping;
        bool _initialized;

        public static void EnsureCreated()
        {
            if (!Core.IsWorld3D) return;
            if (Core.savedSettings == null || !Core.savedSettings.PostFX) return;

            Camera cam = ResolveMainCamera();
            if (cam == null) return;

            if (cam.GetComponent<WSM3DPostStack>() == null)
                cam.gameObject.AddComponent<WSM3DPostStack>();
        }

        public static void ApplySetting(bool enabled)
        {
            Camera cam = ResolveMainCamera();
            if (cam == null) return;

            if (!enabled)
            {
                var existing = cam.GetComponent<WSM3DPostStack>();
                if (existing != null) Destroy(existing);
                _instance = null;
                return;
            }

            EnsureCreated();
        }

        public static void RefreshMaterials()
        {
            if (_instance != null) _instance.InitMaterials();
        }

        static Camera ResolveMainCamera()
        {
            return CameraManager.MainCamera != null ? CameraManager.MainCamera : null;
        }

        void Awake()
        {
            if (_instance != null)
            {
                Destroy(this);
                return;
            }
            _instance = this;

            Camera cam = GetComponent<Camera>();
            if (cam != null) cam.depthTextureMode |= DepthTextureMode.Depth;

            RemoveLegacyPasses();
            InitMaterials();
        }

        void OnDestroy()
        {
            ReleaseMaterials();
            ReleasePingPong();
            _initialized = false;
            if (_instance == this) _instance = null;
        }

        void RemoveLegacyPasses()
        {
            Camera cam = GetComponent<Camera>();
            if (cam == null) return;

            var ssao = cam.GetComponent<ScreenSpaceAO>();
            if (ssao != null) Destroy(ssao);

            var ssgi = cam.GetComponent<ScreenSpaceGI>();
            if (ssgi != null) Destroy(ssgi);

            var lut = cam.GetComponent<Lighting.ColorGradingLUT>();
            if (lut != null) Destroy(lut);
        }

        void InitMaterials()
        {
            ReleaseMaterials();
            _ssaoMat = TryLoadMaterial("Shaders/ScreenSpaceAO", "Hidden/ScreenSpaceAO");
            _ssgiMat = TryLoadMaterial("Shaders/ScreenSpaceGI", "Hidden/ScreenSpaceGI");
            _bloomMat = TryLoadMaterial("Shaders/BrpBloom", "Hidden/WSM3D/BrpBloom");
            _acesMat = TryLoadMaterial("Shaders/BrpACES", "Hidden/WSM3D/BrpACES");

            Shader lutShader = null;
            if (Core.IsWorld3D && Core.Sphere.LoadedShaders.TryGetValue("ColorGradingLUT", out var bundled) && bundled != null)
            {
                lutShader = bundled;
                Debug.Log("[WSM3D] PostStack LUT shader resolved via Core.Sphere.LoadedShaders cache.");
            }
            lutShader ??= Shader.Find("WSM3D/ColorGradingLUT");
            lutShader ??= Resources.Load<Shader>("Shaders/ColorGradingLUT");
            lutShader ??= Shader.Find("Hidden/ColorGradingLUT");
            if (lutShader != null)
            {
                _lutMat = new Material(lutShader) { name = "WSM3D.PostStack.LUT" };
                _lutTexture = Resources.Load<Texture2D>("LUT/default");
                if (_lutTexture == null)
                {
                    _lutTexture = Resources.Load<Texture2D>("LUT/lut_default");
                    _lutTexture ??= Resources.Load<Texture2D>("lut_default");
                }
                if (_lutTexture != null)
                {
                    if (_lutMat.HasProperty("_LutTex"))
                        _lutMat.SetTexture("_LutTex", _lutTexture);
                    else if (_lutMat.HasProperty("_LookupTex"))
                        _lutMat.SetTexture("_LookupTex", _lutTexture);
                    else if (_lutMat.HasProperty("_LUT_Tex2D"))
                        _lutMat.SetTexture("_LUT_Tex2D", _lutTexture);
                    Debug.Log($"[WSM3D] PostStack LUT texture loaded: {_lutTexture.name} ({_lutTexture.width}x{_lutTexture.height}).");
                }
                else
                {
                    Debug.LogWarning("[WSM3D] PostStack LUT texture not found in Resources (LUT/default, LUT/lut_default, lut_default). Color grading pass will be skipped at render time.");
                }
                if (_lutMat.HasProperty("_LutParams"))
                    _lutMat.SetVector("_LutParams", new Vector4(16f / 256f, 1f / 16f, 1f, 0f));
            }
            else
            {
                Debug.LogWarning("[WSM3D] PostStack LUT shader not found in any resolution path.");
            }

            if (_ssaoMat != null)
            {
                ScreenSpaceAO.BuildKernelStatic();
                ApplySSAOParams();
            }

            if (_ssgiMat != null)
            {
                ScreenSpaceGI.BuildKernelStatic();
                ApplySSGIParams();
            }

            bool anyMaterial = _ssaoMat != null || _ssgiMat != null || _bloomMat != null || _acesMat != null || _lutMat != null;
            if (!anyMaterial)
            {
                Debug.LogWarning("[WSM3D] WSM3DPostStack: no shaders resolved — destroying PostStack component to avoid black camera.");
                _initialized = false;
                if (_instance == this) _instance = null;
                Destroy(this);
                return;
            }

            _initialized = true;
            Debug.Log($"[WSM3D] WSM3DPostStack initialized: SSAO={_ssaoMat != null} SSGI={_ssgiMat != null} Bloom={_bloomMat != null} ACES={_acesMat != null} LUT={_lutMat != null}");
        }

        void ReleaseMaterials()
        {
            if (_ssaoMat != null) { Destroy(_ssaoMat); _ssaoMat = null; }
            if (_ssgiMat != null) { Destroy(_ssgiMat); _ssgiMat = null; }
            if (_bloomMat != null) { Destroy(_bloomMat); _bloomMat = null; }
            if (_acesMat != null) { Destroy(_acesMat); _acesMat = null; }
            if (_lutMat != null) { Destroy(_lutMat); _lutMat = null; }
            if (_lutTexture != null) { _lutTexture = null; }
        }

        static Material TryLoadMaterial(string resourcePath, string fallbackName)
        {
            string cacheKey = System.IO.Path.GetFileNameWithoutExtension(resourcePath);
            Shader shader = null;

            // Step 1: bundle cache (populated by Core.LoadAssets from wsm3d-shaders bundle)
            if (Core.IsWorld3D && Core.Sphere.LoadedShaders.TryGetValue(cacheKey, out var bundled) && bundled != null)
            {
                shader = bundled;
                Debug.Log($"[WSM3D] PostStack shader '{cacheKey}' resolved via Core.Sphere.LoadedShaders cache.");
            }

            // Step 2: Shader.Find with WSM3D/ prefix (AssetBundle-declared names)
            shader ??= Shader.Find("WSM3D/" + cacheKey);

            // Step 3: Resources.Load (only works inside Unity project Assets/Resources)
            shader ??= Resources.Load<Shader>(resourcePath);

            // Step 4: Shader.Find with exact fallback name (Hidden/* declared names)
            shader ??= Shader.Find(fallbackName);

            // Step 5: try Hidden/ + cacheKey in case the shader was registered
            // under its Resources/Shaders declared name (e.g. Hidden/ScreenSpaceGI)
            if (shader == null && !fallbackName.StartsWith("Hidden/" + cacheKey, System.StringComparison.Ordinal))
            {
                shader = Shader.Find("Hidden/" + cacheKey);
            }

            if (shader != null)
            {
                Debug.Log($"[WSM3D] PostStack material '{cacheKey}' created via shader '{shader.name}'.");
            }
            else
            {
                bool hasBundleCache = Core.IsWorld3D && Core.Sphere.LoadedShaders.Count > 0;
                Debug.LogWarning($"[WSM3D] PostStack shader '{cacheKey}' not found in any resolution path " +
                    $"(LoadedShaders[count={Core.Sphere.LoadedShaders.Count},hasBundleCache={hasBundleCache}], " +
                    $"Shader.Find('WSM3D/{cacheKey}'), Resources.Load('{resourcePath}'), " +
                    $"Shader.Find('{fallbackName}')). " +
                    "The wsm3d-shaders AssetBundle likely failed to load — rebake with Unity 2022.3 to match WorldBox runtime.");
            }
            return shader != null ? new Material(shader) : null;
        }

        void ApplySSAOParams()
        {
            if (_ssaoMat == null) return;
            int qi = Core.savedSettings != null ? (int)Core.savedSettings.SSAOQuality : 1;
            int[] samples = { 8, 12, 16 };
            float[] radii = { 1.6f, 2.0f, 2.4f };
            float[] biases = { 0.0015f, 0.0012f, 0.001f };
            float[] intensities = { 0.8f, 1.0f, 1.1f };
            qi = Mathf.Clamp(qi, 0, 2);

            _ssaoMat.SetInt("_SampleCount", samples[qi]);
            _ssaoMat.SetVectorArray("_Samples", ScreenSpaceAO.Kernel);
            _ssaoMat.SetFloat("_Radius", radii[qi]);
            _ssaoMat.SetFloat("_Bias", biases[qi]);
            _ssaoMat.SetFloat("_Intensity", intensities[qi]);
        }

        void ApplySSGIParams()
        {
            if (_ssgiMat == null) return;
            _ssgiMat.SetInt("_SampleCount", ScreenSpaceGI.Kernel.Length);
            _ssgiMat.SetVectorArray("_Samples", ScreenSpaceGI.Kernel);
            _ssgiMat.SetFloat("_Radius", ScreenSpaceGI.DefaultRadius);
            _ssgiMat.SetFloat("_Intensity", ScreenSpaceGI.DefaultIntensity);
        }

        void EnsurePingPong(RenderTexture src)
        {
            if (_ping != null && _ping.width == src.width && _ping.height == src.height) return;
            ReleasePingPong();
            _ping = RenderTexture.GetTemporary(src.descriptor);
        }

        void ReleasePingPong()
        {
            if (_ping != null) { RenderTexture.ReleaseTemporary(_ping); _ping = null; }
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (src == null || dst == null)
            {
                return;
            }
            if (!_initialized || Core.savedSettings == null || !Core.savedSettings.PostFX)
            {
                Graphics.Blit(src, dst);
                return;
            }

            bool anyPass = false;
            anyPass |= Core.savedSettings.SSAOEnabled && _ssaoMat != null;
            anyPass |= Core.savedSettings.SSGIEnabled && _ssgiMat != null;
            anyPass |= Core.savedSettings.BloomEnabled && _bloomMat != null;
            anyPass |= Core.savedSettings.ACESTonemapping && _acesMat != null;
            anyPass |= Core.savedSettings.ColorGradingLut && _lutMat != null;

            if (!anyPass)
            {
                Graphics.Blit(src, dst);
                return;
            }

            EnsurePingPong(src);
            try
            {
                RenderTexture cur = src;
                RenderTexture next = _ping;

                // Pass 1: SSAO
                if (Core.savedSettings.SSAOEnabled && _ssaoMat != null)
                {
                    Graphics.Blit(cur, next, _ssaoMat);
                    Swap(ref cur, ref next);
                }

                // Pass 2: SSGI
                if (Core.savedSettings.SSGIEnabled && _ssgiMat != null)
                {
                    Graphics.Blit(cur, next, _ssgiMat);
                    Swap(ref cur, ref next);
                }

                // Pass 3: Bloom (threshold → blur H → blur V → composite)
                if (Core.savedSettings.BloomEnabled && _bloomMat != null)
                {
                    int w = Mathf.Max(1, src.width / 4);
                    int h = Mathf.Max(1, src.height / 4);
                    RenderTexture bloomA = RenderTexture.GetTemporary(w, h, 0, src.format);
                    RenderTexture bloomB = RenderTexture.GetTemporary(w, h, 0, src.format);

                    try
                    {
                        Graphics.Blit(cur, bloomA, _bloomMat, 0); // threshold
                        Graphics.Blit(bloomA, bloomB, _bloomMat, 1); // blur H
                        Graphics.Blit(bloomB, bloomA, _bloomMat, 2); // blur V

                        _bloomMat.SetTexture(BloomTexId, bloomA);
                        Graphics.Blit(cur, next, _bloomMat, 3); // composite
                        Swap(ref cur, ref next);
                    }
                    finally
                    {
                        _bloomMat?.SetTexture(BloomTexId, null);
                        RenderTexture.ReleaseTemporary(bloomA);
                        RenderTexture.ReleaseTemporary(bloomB);
                    }
                }

                // Pass 4: ACES tonemap
                if (Core.savedSettings.ACESTonemapping && _acesMat != null)
                {
                    _acesMat.SetFloat(ExposureId, 1.0f);
                    Graphics.Blit(cur, next, _acesMat);
                    Swap(ref cur, ref next);
                }

                // Pass 5: LUT color grading (final)
                if (Core.savedSettings.ColorGradingLut && _lutMat != null && _lutTexture != null)
                {
                    Graphics.Blit(cur, dst, _lutMat);
                }
                else
                {
                    Graphics.Blit(cur, dst);
                }
            }
            finally
            {
                ReleasePingPong();
            }
        }

        static void Swap(ref RenderTexture a, ref RenderTexture b)
        {
            (a, b) = (b, a);
        }
    }
}
