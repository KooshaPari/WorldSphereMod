using UnityEngine;
using UnityEngine.Rendering;
namespace WorldSphereMod.Lighting
{
    public sealed class CubemapLighting : MonoBehaviour
    {
        public const string CubemapResourcePath = "Cubemap/sky-default";

        static CubemapLighting? _instance;
        static Cubemap? _previousCustomReflection;
        static AmbientMode _previousAmbientMode;
        static DefaultReflectionMode _previousDefaultReflectionMode;
        static float _previousReflectionIntensity;
        static bool _hasCapturedReflectionState;
        static bool _loadInProgress;
        bool _applied;

        public static void EnsureCreated()
        {
            if (!Core.IsWorld3D || !Core.savedSettings.HdrSkybox)
            {
                return;
            }
            if (Mod.Object == null)
            {
                return;
            }
            if (_instance != null)
            {
                return;
            }
            Mod.Object.AddComponent<CubemapLighting>();
        }

        public static void ApplySetting(bool enabled)
        {
            if (enabled)
            {
                EnsureCreated();
                return;
            }
            if (_instance != null)
            {
                Destroy(_instance);
            }
        }

        void Awake()
        {
            if (_instance != null)
            {
                Destroy(this);
                return;
            }
            _instance = this;
            if (_loadInProgress)
            {
                return;
            }
            StartCoroutine(ApplySkyboxCubemapAsync());
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            RestoreRenderSettings();
            Debug.Log("[WSM3D] CubemapLighting disabled (custom reflection reset).");
        }

        System.Collections.IEnumerator ApplySkyboxCubemapAsync()
        {
            _loadInProgress = true;
            ResourceRequest request = Resources.LoadAsync<Cubemap>(CubemapResourcePath);
            yield return request;
            Cubemap? skyCubemap = request.asset as Cubemap;
            if (skyCubemap == null)
            {
                Debug.LogWarning($"[WSM3D] Cubemap '{CubemapResourcePath}' not found in Resources; HDR reflection probe skipped.");
                _loadInProgress = false;
                yield break;
            }

            CapturePreviousReflectionState();
            RenderSettings.customReflection = skyCubemap;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 1f;
            _applied = true;
            _loadInProgress = false;
        }

        void RestoreRenderSettings()
        {
            if (!_applied || !_hasCapturedReflectionState)
            {
                return;
            }

            RenderSettings.customReflection = _previousCustomReflection;
            RenderSettings.ambientMode = _previousAmbientMode;
            RenderSettings.defaultReflectionMode = _previousDefaultReflectionMode;
            RenderSettings.reflectionIntensity = _previousReflectionIntensity;
        }

        static void CapturePreviousReflectionState()
        {
            if (_hasCapturedReflectionState)
            {
                return;
            }
            _previousCustomReflection = RenderSettings.customReflection;
            _previousAmbientMode = RenderSettings.ambientMode;
            _previousDefaultReflectionMode = RenderSettings.defaultReflectionMode;
            _previousReflectionIntensity = RenderSettings.reflectionIntensity;
            _hasCapturedReflectionState = true;
        }
    }
}
