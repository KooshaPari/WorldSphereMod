using UnityEngine;
using UnityEngine.Rendering;
namespace WorldSphereMod.Lighting
{
    [Phase(nameof(SavedSettings.HdrSkybox))]
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
                Debug.LogWarning($"[WSM3D] Cubemap '{CubemapResourcePath}' not found in Resources; trying fallback paths.");
                skyCubemap = Resources.Load<Cubemap>("Cubemap/sky_default");
                skyCubemap ??= Resources.Load<Cubemap>("sky-default");
            }

            // Validate the loaded asset is actually a Cubemap. Resources.Load
            // can return a Texture2D if the asset was re-imported incorrectly,
            // and assigning a non-cubemap to RenderSettings.customReflection
            // throws ArgumentException and hangs the game (Responding=False).
            if (skyCubemap != null && skyCubemap.dimension != UnityEngine.Rendering.TextureDimension.Cube)
            {
                Debug.LogError($"[WSM3D] CubemapLighting: loaded texture '{skyCubemap.name}' has dimension {skyCubemap.dimension}, expected Cube. Treating as missing.");
                skyCubemap = null;
            }

            if (skyCubemap == null)
            {
                Debug.Log("[WSM3D] CubemapLighting: no custom cubemap found, applying skybox-derived ambient + reflection mode.");
                CapturePreviousReflectionState();
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                RenderSettings.reflectionIntensity = 1f;
                _applied = true;
                _loadInProgress = false;
                yield break;
            }

            Debug.Log($"[WSM3D] CubemapLighting loaded cubemap '{skyCubemap.name}' (dimension={skyCubemap.dimension}).");
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

            // Only restore customReflection if the previous value is a real
            // Cubemap. Assigning null or a non-cubemap texture triggers
            // ArgumentException ("RenderSettings.customReflection is currently
            // not referencing a cubemap") and hangs the game.
            if (_previousCustomReflection != null &&
                _previousCustomReflection.dimension == UnityEngine.Rendering.TextureDimension.Cube)
            {
                RenderSettings.customReflection = _previousCustomReflection;
            }
            else
            {
                // Fall back to skybox reflection instead of setting a bad value.
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            }
            RenderSettings.ambientMode = _previousAmbientMode;
            // Only restore the previous default reflection mode if we didn't
            // override it to Skybox above (i.e., the previous custom cubemap
            // was valid). When the previous mode was Custom but the cubemap is
            // null/invalid, forcing Custom would re-trigger the same error.
            if (_previousCustomReflection != null &&
                _previousCustomReflection.dimension == UnityEngine.Rendering.TextureDimension.Cube)
            {
                RenderSettings.defaultReflectionMode = _previousDefaultReflectionMode;
            }
            RenderSettings.reflectionIntensity = _previousReflectionIntensity;
        }

        static void CapturePreviousReflectionState()
        {
            if (_hasCapturedReflectionState)
            {
                return;
            }
            try { _previousCustomReflection = RenderSettings.customReflection; } catch { _previousCustomReflection = null; }
            _previousAmbientMode = RenderSettings.ambientMode;
            _previousDefaultReflectionMode = RenderSettings.defaultReflectionMode;
            _previousReflectionIntensity = RenderSettings.reflectionIntensity;
            _hasCapturedReflectionState = true;
        }
    }
}
