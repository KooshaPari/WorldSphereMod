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
        bool _applied;

        static Camera? MainCamera => Camera.main;

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
            ApplySkyboxCubemap();
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

        void ApplySkyboxCubemap()
        {
            Cubemap? skyCubemap = Resources.Load<Cubemap>(CubemapResourcePath);
            if (skyCubemap == null)
            {
                Debug.LogWarning($"[WSM3D] Cubemap '{CubemapResourcePath}' not found in Resources; HDR reflection probe skipped.");
                return;
            }

            CapturePreviousReflectionState();
            RenderSettings.customReflection = skyCubemap;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 1f;
            _applied = true;
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
