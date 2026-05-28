using System.Collections;
using System.IO;
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
            _lutTexture = TryLoadLutFromDisk("LUT/default.png");
            if (_lutTexture == null)
            {
                ResourceRequest textureRequest = Resources.LoadAsync<Texture2D>(LutTextureResourcePath);
                yield return textureRequest;
                _lutTexture = textureRequest.asset as Texture2D;
            }
            if (_lutTexture == null)
            {
                Debug.LogWarning($"[WSM3D] ColorGradingLUT texture not found (disk '{Mod.ModDirectory}/Resources/LUT/default.png' or Resources '{LutTextureResourcePath}'); color grading skipped.");
                _initializing = false;
                yield break;
            }
            if (_lutTexture.width != 256 || _lutTexture.height != 16)
            {
                Debug.LogWarning($"[WSM3D] ColorGradingLUT expects 256x16 texture, found {_lutTexture.width}x{_lutTexture.height}; skipping effect.");
                _initializing = false;
                yield break;
            }

            Shader? shader = null;
            if (WorldSphereMod.Core.Sphere.LoadedShaders.TryGetValue("ColorGradingLUT", out var bundledLut) && bundledLut != null)
            {
                shader = bundledLut;
                Debug.Log("[WSM3D] ColorGradingLUT shader resolved via Core.Sphere.LoadedShaders cache.");
            }
            if (shader == null)
            {
                shader = Shader.Find("WSM3D/ColorGradingLUT");
                if (shader != null) Debug.Log("[WSM3D] ColorGradingLUT shader resolved via Shader.Find('WSM3D/ColorGradingLUT').");
            }
            if (shader == null)
            {
                ResourceRequest shaderRequest = Resources.LoadAsync<Shader>(LutShaderResourcePath);
                yield return shaderRequest;
                shader = shaderRequest.asset as Shader;
            }
            shader ??= Shader.Find("Hidden/ColorGradingLUT");
            if (shader == null)
            {
                Debug.LogWarning("[WSM3D] ColorGradingLUT: shader unavailable (LoadedShaders, Shader.Find, Resources). " +
                    "Color grading pass will be skipped — no visual impact.");
                _initializing = false;
                yield break;
            }
            Debug.Log($"[WSM3D] ColorGradingLUT shader resolved: '{shader.name}'.");

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

        static Texture2D? TryLoadLutFromDisk(string relativePath)
        {
            try
            {
                string modDir = Mod.ModDirectory;
                if (string.IsNullOrEmpty(modDir)) return null;
                string fullPath = Path.Combine(modDir, "Resources", relativePath);
                if (!File.Exists(fullPath)) return null;
                byte[] bytes = File.ReadAllBytes(fullPath);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                tex.name = Path.GetFileNameWithoutExtension(relativePath);
                var miInstance = typeof(Texture2D).GetMethod(
                    "LoadImage",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    null,
                    new System.Type[] { typeof(byte[]) },
                    null);
                if (miInstance != null)
                {
                    object result = miInstance.Invoke(tex, new object[] { bytes });
                    if (result is bool b && !b) { Destroy(tex); return null; }
                }
                else
                {
                    var icType = typeof(Texture2D).Assembly.GetType("UnityEngine.ImageConversion");
                    var miStatic = icType?.GetMethod(
                        "LoadImage",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                        null,
                        new System.Type[] { typeof(Texture2D), typeof(byte[]) },
                        null);
                    if (miStatic != null)
                    {
                        object result = miStatic.Invoke(null, new object[] { tex, bytes });
                        if (result is bool b && !b) { Destroy(tex); return null; }
                    }
                    else
                    {
                        Destroy(tex);
                        return null;
                    }
                }
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                Debug.Log($"[WSM3D] ColorGradingLUT loaded from disk: {fullPath} ({tex.width}x{tex.height})");
                return tex;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[WSM3D] ColorGradingLUT disk load failed for '{relativePath}': {ex.Message}");
                return null;
            }
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
