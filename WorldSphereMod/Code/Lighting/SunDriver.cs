using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.Lighting
{
    public static class SunDriver
    {
        public static Light? Sun { get; private set; }
        public static Transform? LightingRoot { get; private set; }

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

        public static void Update()
        {
            if (Sun == null) return;
            if (LightingRoot != null && CameraManager.MainCamera != null)
            {
                LightingRoot.position = CameraManager.MainCamera.transform.position;
            }
            if (LightingRoot != null)
            {
                LightingRoot.rotation = Quaternion.Euler(TimeOfDayToEuler(TimeOfDay), 30f, 0f);
            }
        }

        static float TimeOfDayToEuler(float hours)
        {
            float angle = (hours / 24f) * 360f - 90f;
            return Mathf.Clamp(angle, -90f, 90f);
        }
    }
}
