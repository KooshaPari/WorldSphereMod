using UnityEngine;
using UnityEngine.Rendering;
namespace WorldSphereMod.Lighting
{
    [Phase(nameof(SavedSettings.HdrSkybox))]
    public sealed class CubemapLighting : MonoBehaviour
    {
        public const string CubemapResourcePath = "Cubemap/sky-default";
        const int kProceduralCubemapSize = 128;

        static CubemapLighting? _instance;
        static Cubemap? _previousCustomReflection;
        static Material? _previousSkybox;
        static Material? _runtimeSkyboxMaterial;
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
                skyCubemap = CreateProceduralCubemap();
                CapturePreviousReflectionState();
                CapturePreviousSkyboxState();
                ApplyRuntimeSkybox(skyCubemap);
                RenderSettings.customReflection = skyCubemap;
                ApplyNeutralAmbient();
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.reflectionIntensity = 0.3f;
                _applied = true;
                _loadInProgress = false;
                yield break;
            }

            Debug.Log($"[WSM3D] CubemapLighting loaded cubemap '{skyCubemap.name}' (dimension={skyCubemap.dimension}).");
            CapturePreviousReflectionState();
            CapturePreviousSkyboxState();
            ApplyRuntimeSkybox(skyCubemap);
            RenderSettings.customReflection = skyCubemap;
            ApplyNeutralAmbient();
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0.3f;
            _applied = true;
            _loadInProgress = false;
        }

        void RestoreRenderSettings()
        {
            if (!_applied || !_hasCapturedReflectionState)
            {
                return;
            }

            if (_previousSkybox != null)
            {
                RenderSettings.skybox = _previousSkybox;
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

        static void ApplyNeutralAmbient()
        {
            // Skybox-mode SH probe samples the pale-blue horizon and tints
            // everything blue. Trilight with neutral sky/equator/ground keeps
            // shading directional without the blue cast.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.95f, 0.95f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.7f, 0.7f, 0.7f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.3f, 0.3f);
            RenderSettings.ambientIntensity = 0.5f;
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

        static void CapturePreviousSkyboxState()
        {
            if (_previousSkybox == null)
            {
                _previousSkybox = RenderSettings.skybox;
            }
        }

        static void ApplyRuntimeSkybox(Cubemap cubemap)
        {
            Shader? skyboxShader = Shader.Find("Skybox/Cubemap");
            if (skyboxShader == null)
            {
                Debug.LogWarning("[WSM3D] CubemapLighting: Skybox/Cubemap shader not found; applying reflection cubemap only.");
                return;
            }

            if (_runtimeSkyboxMaterial == null)
            {
                _runtimeSkyboxMaterial = new Material(skyboxShader)
                {
                    name = "WSM3D.RuntimeSkybox",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            else if (_runtimeSkyboxMaterial.shader != skyboxShader)
            {
                _runtimeSkyboxMaterial.shader = skyboxShader;
            }

            _runtimeSkyboxMaterial.SetTexture("_Tex", cubemap);
            RenderSettings.skybox = _runtimeSkyboxMaterial;
        }

        static Cubemap CreateProceduralCubemap()
        {
            Cubemap cubemap = new Cubemap(kProceduralCubemapSize, TextureFormat.RGBAHalf, false)
            {
                name = "WSM3D.ProceduralSkyCubemap",
                hideFlags = HideFlags.HideAndDontSave
            };

            Color bottom = new Color(0.04f, 0.08f, 0.22f, 1f);
            Color horizon = new Color(0.45f, 0.68f, 0.92f, 1f);
            Color top = new Color(0.94f, 0.97f, 1.0f, 1f);

            for (int y = 0; y < kProceduralCubemapSize; y++)
            {
                float t = kProceduralCubemapSize > 1 ? (float)y / (kProceduralCubemapSize - 1) : 0f;
                Color vertical = t < 0.5f
                    ? Color.Lerp(bottom, horizon, t * 2f)
                    : Color.Lerp(horizon, top, (t - 0.5f) * 2f);

                for (int x = 0; x < kProceduralCubemapSize; x++)
                {
                    float u = kProceduralCubemapSize > 1 ? (float)x / (kProceduralCubemapSize - 1) : 0f;
                    float edge = Mathf.Abs(u - 0.5f) * 2f;
                    Color color = Color.Lerp(vertical, top, edge * 0.06f);

                    for (int face = 0; face < 6; face++)
                    {
                        cubemap.SetPixel((CubemapFace)face, x, y, color);
                    }
                }
            }

            cubemap.Apply(false, false);
            return cubemap;
        }
    }
}
