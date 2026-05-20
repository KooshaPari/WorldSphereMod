using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.Lighting
{
    public static class SunDriver
    {
        public static Light? Sun { get; private set; }
        public static Transform? LightingRoot { get; private set; }
        static Camera? _trackingCamera;
        static float _nextCameraRefresh;
        const float kCameraRefreshInterval = 0.05f;

        public static float TimeOfDay = 11.0f;

        public static bool Active => Sun != null && Sun.shadows != LightShadows.None;

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
            Sun.shadows = LightShadows.Soft;

            LightingRoot.rotation = Quaternion.Euler(TimeOfDayToEuler(TimeOfDay), 30f, 0f);

            SunRig.Bind(Sun);
            BindMainCamera(CameraManager.MainCamera);

            ShadowCascadeConfig.Apply(Core.savedSettings.HighShadows);
        }

        public static void Teardown()
        {
            ShadowCascadeConfig.Reset();
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
