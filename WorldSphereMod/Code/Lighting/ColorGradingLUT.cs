using System.Collections;
using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.Lighting
{
    public sealed class ColorGradingLUT : MonoBehaviour
    {
        public const string LutTextureResourcePath = "LUT/default";
        public const string LutShaderResourcePath = "Shaders/ColorGradingLUT";

        static ColorGradingLUT? _instance;
        static readonly int LutTexId = Shader.PropertyToID("_LutTex");
        static readonly int LookupTexId = Shader.PropertyToID("_LookupTex");
        static readonly int LutParamsId = Shader.PropertyToID("_LutParams");

        Material? _lutMaterial;
        Texture2D? _lutTexture;
        bool _hasApplied;
        bool _materialReady;
        bool _initializing;

        static Camera? ResolveMainCamera()
        {
            return CameraManager.MainCamera != null ? CameraManager.MainCamera : null;
        }

        public static void EnsureCreated()
        {
            if (!Core.IsWorld3D || !Core.savedSettings.ColorGradingLut)
            {
                return;
            }
            if (_instance != null)
            {
                return;
            }

            Camera? mainCamera = ResolveMainCamera();
            if (mainCamera == null)
            {
                Debug.LogWarning("[WSM3D] ColorGradingLUT deferred: main camera not ready yet.");
                return;
            }
            if (mainCamera.GetComponent<ColorGradingLUT>() == null)
            {
                mainCamera.AddComponent<ColorGradingLUT>();
            }
            _instance = mainCamera.GetComponent<ColorGradingLUT>();
        }

        public static void ApplySetting(bool enabled)
        {
            if (enabled)
            {
                EnsureCreated();
                return;
            }

            Camera? mainCamera = ResolveMainCamera();
            if (mainCamera == null)
            {
                return;
            }
            var existing = mainCamera.GetComponent<ColorGradingLUT>();
            if (existing != null)
            {
                Destroy(existing);
            }
            _instance = null;
        }

        void Awake()
        {
            if (_instance != null)
            {
                Destroy(this);
                return;
            }
            _instance = this;
            if (!_initializing)
            {
                StartCoroutine(InitializeAsync());
            }
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        IEnumerator InitializeAsync()
        {
            _initializing = true;
            ResourceRequest textureRequest = Resources.LoadAsync<Texture2D>(LutTextureResourcePath);
            yield return textureRequest;
            _lutTexture = textureRequest.asset as Texture2D;
            if (_lutTexture == null)
            {
                Debug.LogWarning($"[WSM3D] ColorGradingLUT texture '{LutTextureResourcePath}' not found in Resources; color grading skipped.");
                _initializing = false;
                yield break;
            }
            if (_lutTexture.width != 256 || _lutTexture.height != 16)
            {
                Debug.LogWarning($"[WSM3D] ColorGradingLUT expects 256x16 texture, found {_lutTexture.width}x{_lutTexture.height}; skipping effect.");
                _initializing = false;
                yield break;
            }

            ResourceRequest shaderRequest = Resources.LoadAsync<Shader>(LutShaderResourcePath);
            yield return shaderRequest;
            Shader? shader = shaderRequest.asset as Shader;
            shader ??= Shader.Find("Hidden/ColorGradingLUT");
            if (shader == null)
            {
                Debug.LogWarning("[WSM3D] ColorGradingLUT shader not found; check Resources/Shaders/ColorGradingLUT and skip grading.");
                _initializing = false;
                yield break;
            }

            _lutMaterial = new Material(shader) { name = "WSM3D.ColorGradingLUT" };

            if (_lutMaterial.HasProperty(LutTexId))
            {
                _lutMaterial.SetTexture(LutTexId, _lutTexture);
            }
            else if (_lutMaterial.HasProperty(LookupTexId))
            {
                _lutMaterial.SetTexture(LookupTexId, _lutTexture);
            }

            if (_lutMaterial.HasProperty(LutParamsId))
            {
                _lutMaterial.SetVector(LutParamsId, new Vector4(16f / 256f, 1f / 16f, 1f, 0f));
            }

            _hasApplied = true;
            _materialReady = _lutMaterial != null;
            _initializing = false;
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!_hasApplied || _lutMaterial == null || !_materialReady)
            {
                Graphics.Blit(source, destination);
                return;
            }
            Graphics.Blit(source, destination, _lutMaterial);
        }
    }
}
