using System.Collections;
using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.PostFx
{
    public sealed class ScreenSpaceAO : MonoBehaviour
    {
        public enum Quality
        {
            Low = 0,
            Medium = 1,
            High = 2
        }

        public const string ShaderResourcePath = "Shaders/ScreenSpaceAO";
        public const int MaxSamples = 16;
        public const float DefaultRadius = 2.2f;
        public const float DefaultBias = 0.001f;
        public const float DefaultIntensity = 1.0f;
        public const string ShaderFallbackName = "Hidden/ScreenSpaceAO";

        static readonly int SamplesId = Shader.PropertyToID("_Samples");
        static readonly int SampleCountId = Shader.PropertyToID("_SampleCount");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int BiasId = Shader.PropertyToID("_Bias");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int[] SamplesByQuality = { 8, 12, MaxSamples };
        static readonly float[] RadiusByQuality = { 1.6f, 2.0f, 2.4f };
        static readonly float[] BiasByQuality = { 0.0015f, 0.0012f, 0.001f };
        static readonly float[] IntensityByQuality = { 0.8f, 1.0f, 1.1f };

        static ScreenSpaceAO? _instance;
        internal static readonly Vector4[] Kernel = new Vector4[MaxSamples];
        static bool _kernelBuilt;
        Material? _material;
        bool _initializing;
        bool _destroyed;

        static Camera? ResolveMainCamera()
        {
            return CameraManager.MainCamera != null ? CameraManager.MainCamera : null;
        }

        public static void ApplySetting(bool enabled)
        {
            Camera? mainCamera = ResolveMainCamera();
            if (mainCamera == null)
            {
                return;
            }

            if (!enabled)
            {
                var existing = mainCamera.GetComponent<ScreenSpaceAO>();
                if (existing != null)
                {
                    Destroy(existing);
                }
                _instance = null;
                return;
            }

            EnsureCreated();
        }

        public static void ApplyQualitySetting()
        {
            if (_instance != null)
            {
                _instance.UpdateSettings();
            }
        }

        public static void EnsureCreated()
        {
            if (!Core.IsWorld3D || Core.savedSettings == null || !Core.savedSettings.SSAOEnabled)
            {
                return;
            }

            Camera? mainCamera = ResolveMainCamera();
            if (mainCamera == null)
            {
                return;
            }

            if (mainCamera.GetComponent<ScreenSpaceAO>() == null)
            {
                mainCamera.AddComponent<ScreenSpaceAO>();
            }
        }

        void Awake()
        {
            if (!Core.IsWorld3D)
            {
                Destroy(this);
                return;
            }
            if (_instance != null)
            {
                Destroy(this);
                return;
            }
            _instance = this;
            BuildKernel();
            if (!_initializing)
            {
                StartCoroutine(ConfigureMaterialAsync());
            }
            EnableDepth();
        }

        void OnDestroy()
        {
            _destroyed = true;
            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
            if (_instance == this)
            {
                _instance = null;
            }
        }

        void EnableDepth()
        {
            Camera cam = GetComponent<Camera>();
            cam.depthTextureMode |= DepthTextureMode.Depth;
        }

        IEnumerator ConfigureMaterialAsync()
        {
            _initializing = true;

            Shader? shader = ResolveShader();
            if (shader == null)
            {
                ResourceRequest request = Resources.LoadAsync<Shader>(ShaderResourcePath);
                yield return request;
                if (_destroyed) { _initializing = false; yield break; }
                shader = request.asset as Shader;
            }
            shader ??= Shader.Find(ShaderFallbackName);
            if (shader == null)
            {
                Debug.LogWarning("[WSM3D] ScreenSpaceAO: shader unavailable (LoadedShaders, Shader.Find, Resources). " +
                    "SSAO pass will be skipped — no visual impact.");
                _initializing = false;
                yield break;
            }
            if (_destroyed) { _initializing = false; yield break; }
            _material = new Material(shader);
            Debug.Log($"[WSM3D] ScreenSpaceAO material created via shader '{shader.name}'.");
            _initializing = false;
        }

        static Shader? ResolveShader()
        {
            if (Core.IsWorld3D && Core.Sphere.LoadedShaders.TryGetValue("ScreenSpaceAO", out var bundled) && bundled != null)
            {
                Debug.Log("[WSM3D] ScreenSpaceAO shader resolved via Core.Sphere.LoadedShaders cache.");
                return bundled;
            }
            Shader? s = Shader.Find("WSM3D/ScreenSpaceAO");
            if (s != null)
            {
                Debug.Log("[WSM3D] ScreenSpaceAO shader resolved via Shader.Find('WSM3D/ScreenSpaceAO').");
                return s;
            }
            return null;
        }

        internal static void BuildKernelStatic()
        {
            if (_kernelBuilt) return;
            System.Random rng = new System.Random(1337);
            for (int i = 0; i < Kernel.Length; i++)
            {
                Vector2 sample;
                sample.x = Mathf.Lerp(-1f, 1f, (float)rng.NextDouble());
                sample.y = Mathf.Lerp(-1f, 1f, (float)rng.NextDouble());
                if (sample.sqrMagnitude < 0.0001f)
                {
                    sample = Vector2.right;
                }
                sample.Normalize();
                float scale = (i + 1f) / Kernel.Length;
                Kernel[i] = new Vector4(sample.x, sample.y, 0f, scale);
            }
            _kernelBuilt = true;
        }

        void BuildKernel() => BuildKernelStatic();

        void UpdateSettings()
        {
            if (_material == null)
            {
                return;
            }
            Quality quality = GetQualityProfile();
            int qualityIndex = (int)quality;
            int sampleCount = SamplesByQuality[Mathf.Clamp(qualityIndex, 0, SamplesByQuality.Length - 1)];
            float radius = RadiusByQuality[Mathf.Clamp(qualityIndex, 0, RadiusByQuality.Length - 1)];
            float bias = BiasByQuality[Mathf.Clamp(qualityIndex, 0, BiasByQuality.Length - 1)];
            float intensity = IntensityByQuality[Mathf.Clamp(qualityIndex, 0, IntensityByQuality.Length - 1)];

            _material.SetInt(SampleCountId, sampleCount);
            _material.SetVectorArray(SamplesId, Kernel);
            _material.SetFloat(RadiusId, radius);
            _material.SetFloat(BiasId, bias);
            _material.SetFloat(IntensityId, intensity);
        }

        static Quality GetQualityProfile()
        {
            if (Core.savedSettings == null)
            {
                return Quality.Medium;
            }

            return Core.savedSettings.SSAOQuality switch
            {
                SsaoQuality.Low => Quality.Low,
                SsaoQuality.High => Quality.High,
                _ => Quality.Medium
            };
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (Core.savedSettings == null || !Core.savedSettings.SSAOEnabled)
            {
                Graphics.Blit(source, destination);
                return;
            }

            if (_material == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            UpdateSettings();
            Graphics.Blit(source, destination, _material);
        }
    }
}
