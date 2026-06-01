using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.Lighting
{
    [Phase(nameof(SavedSettings.HighShadows))]
    public static class SunDriver
    {
        public static Light? Sun { get; private set; }
        public static Transform? LightingRoot { get; private set; }
        static Camera? _trackingCamera;
        static float _nextCameraRefresh;
        const float kCameraRefreshInterval = 0.05f;

        public static float TimeOfDay = 11.0f;

        public static bool Active => Sun != null && Sun.shadows != LightShadows.None;

        public static void ApplyShadowSettings()
        {
            if (Sun == null) return;

            bool highShadows = Core.savedSettings.HighShadows;
            // Keep the sun on soft shadows in both modes; HighShadows only expands the
            // cascade budget and tightens the light bias for voxel silhouettes.
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = highShadows ? 0.92f : 0.78f;
            Sun.shadowBias = highShadows ? 0.035f : 0.05f;
            Sun.shadowNormalBias = highShadows ? 0.30f : 0.45f;

            ShadowCascadeConfig.Apply(highShadows);
        }

        public static void Init()
        {
            if (!Core.IsWorld3D) return;
            if (Sun != null) return;

            GameObject rootGo = new GameObject("WSM3D.LightingRoot");
            LightingRoot = rootGo.transform;
            LightingRoot.position = Vector3.zero;

            GameObject sunGo = new GameObject("WSM3D.Sun");
            sunGo.transform.SetParent(LightingRoot, worldPositionStays: false);

            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.intensity = 1.0f;
            Sun.color = Color.white;
            Sun.enabled = true;

            LightingRoot.rotation = Quaternion.Euler(TimeOfDayToEuler(TimeOfDay), 30f, 0f);

            // SUN=NULL ROOT-CAUSE FIX: nothing else assigns RenderSettings.sun, so
            // the scene had no key light (RenderSettings.sun == null at runtime) and
            // the lit CompoundSphere/OpaqueVertexColor terrain rendered near-black.
            // Register our directional light as the scene's sun so ambient/skybox
            // and lit shaders pick it up as the primary directional source.
            RenderSettings.sun = Sun;

            // MOONLIGHT / AMBIENT FLOOR so night (and the day-night-cycle-off
            // default, where TimeOfDay never pumps SunRig.Drive) is never pure black.
            // SunRig.Drive overrides these every frame once day/night is active; this
            // is the static baseline when it isn't.
            if (RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Trilight)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            }
            RenderSettings.ambientSkyColor = SunRig.ZenithColor(TimeOfDay / 24f);
            RenderSettings.ambientEquatorColor = SunRig.HorizonColor(TimeOfDay / 24f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.13f, 0.16f, 1f);
            RenderSettings.ambientIntensity = Mathf.Max(RenderSettings.ambientIntensity, 0.35f);

            ApplyShadowSettings();
            SunRig.Bind(Sun);
            BindMainCamera(CameraManager.MainCamera);

            // Apply the day/night colour curve once immediately so the very first
            // frame is lit even before any Update pump (day/night cycle may be off).
            SunRig.Drive(TimeOfDay / 24f);
        }

        public static void Teardown()
        {
            ShadowCascadeConfig.Reset();
            if (RenderSettings.sun == Sun)
            {
                RenderSettings.sun = null;
            }
            if (LightingRoot != null)
            {
                Object.Destroy(LightingRoot.gameObject);
            }
            LightingRoot = null;
            Sun = null;
        }

        public static void BindMainCamera(Camera? camera)
        {
            _trackingCamera = camera;
            _nextCameraRefresh = 0f;
        }

        public static void Update()
        {
            if (Sun == null) return;
            if (Time.time >= _nextCameraRefresh)
            {
                _trackingCamera = CameraManager.MainCamera;
                _nextCameraRefresh = Time.time + kCameraRefreshInterval;
            }

            if (LightingRoot != null && _trackingCamera != null)
            {
                LightingRoot.position = _trackingCamera.transform.position;
            }
            if (LightingRoot != null)
            {
                LightingRoot.rotation = Quaternion.Euler(TimeOfDayToEuler(TimeOfDay), 30f, 0f);
            }

            SunRig.Drive(TimeOfDay / 24f);
        }

        static float TimeOfDayToEuler(float hours)
        {
            return (hours / 24f) * 360f - 90f;
        }
    }
}
