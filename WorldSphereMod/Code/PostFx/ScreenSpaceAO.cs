using System.Collections;
using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.PostFx
{
    public sealed class ScreenSpaceAO : MonoBehaviour
    {
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

        static ScreenSpaceAO? _instance;
        static readonly Vector4[] Kernel = new Vector4[MaxSamples];
        Material? _material;
        bool _initializing;

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

        public static void EnsureCreated()
        {
            if (!Core.IsWorld3D || !Core.savedSettings.SSAOEnabled)
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
            ResourceRequest request = Resources.LoadAsync<Shader>(ShaderResourcePath);
            yield return request;
            Shader? shader = request.asset as Shader;
            shader ??= Shader.Find(ShaderFallbackName);
            if (shader == null)
            {
                Debug.LogWarning("[WSM3D] ScreenSpaceAO: shader not found (Resources/Shaders/ScreenSpaceAO or Hidden/ScreenSpaceAO)");
                _initializing = false;
                yield break;
            }
            _material = new Material(shader);
            _initializing = false;
        }

        void BuildKernel()
        {
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
            _material.SetFloat(BiasId, DefaultBias);
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
