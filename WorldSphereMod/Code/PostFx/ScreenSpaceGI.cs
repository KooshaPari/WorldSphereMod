using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.PostFx
{
    public sealed class ScreenSpaceGI : MonoBehaviour
    {
        public const string ShaderResourcePath = "Shaders/ScreenSpaceGI";
        public const int MaxSamples = 12;
        public const float DefaultRadius = 1.8f;
        public const float DefaultIntensity = 0.45f;
        public const string ShaderFallbackName = "Hidden/ScreenSpaceGI";

        static readonly int SamplesId = Shader.PropertyToID("_Samples");
        static readonly int SampleCountId = Shader.PropertyToID("_SampleCount");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        static ScreenSpaceGI? _instance;
        static readonly Vector4[] Kernel = new Vector4[MaxSamples];
        Material? _material;

        public static void ApplySetting(bool enabled)
        {
            Camera? mainCamera = CameraManager.MainCamera != null ? CameraManager.MainCamera : Camera.main;
            if (mainCamera == null)
            {
                return;
            }

            if (!enabled)
            {
                var existing = mainCamera.GetComponent<ScreenSpaceGI>();
                if (existing != null)
                {
                    Destroy(existing);
                }
                _instance = null;
                return;
            }

            EnsureCreated();
        }

        public static void EnsureCreated()
        {
            if (!Core.IsWorld3D || !Core.savedSettings.SSGIEnabled)
            {
                return;
            }

            Camera? mainCamera = CameraManager.MainCamera != null ? CameraManager.MainCamera : Camera.main;
            if (mainCamera == null)
            {
                return;
            }

            if (mainCamera.GetComponent<ScreenSpaceGI>() == null)
            {
                mainCamera.AddComponent<ScreenSpaceGI>();
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
            ConfigureMaterial();
            EnableDepth();
        }

        void OnDestroy()
        {
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

        void ConfigureMaterial()
        {
            Shader? shader = Resources.Load<Shader>(ShaderResourcePath);
            shader ??= Shader.Find(ShaderFallbackName);
            if (shader == null)
            {
                Debug.LogWarning("[WSM3D] ScreenSpaceGI: shader not found (Resources/Shaders/ScreenSpaceGI or Hidden/ScreenSpaceGI)");
                return;
            }
            _material = new Material(shader);
        }

        void BuildKernel()
        {
            System.Random rng = new System.Random(4242);
            for (int i = 0; i < Kernel.Length; i++)
            {
                Vector2 sample;
                sample.x = Mathf.Lerp(-1f, 1f, (float)rng.NextDouble());
                sample.y = Mathf.Lerp(-1f, 1f, (float)rng.NextDouble());
                if (sample.sqrMagnitude < 0.0001f)
                {
                    sample = Vector2.one;
                }
                sample.Normalize();
                Kernel[i] = new Vector4(sample.x, sample.y, 0f, 0f);
            }
        }

        void UpdateSettings()
        {
            if (_material == null)
            {
                return;
            }
            _material.SetInt(SampleCountId, Kernel.Length);
            _material.SetVectorArray(SamplesId, Kernel);
            _material.SetFloat(RadiusId, DefaultRadius);
            _material.SetFloat(IntensityId, DefaultIntensity);
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
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
