using System.Reflection;
using UnityEngine;

namespace WorldSphereMod.Lighting
{
    public sealed class TimeOfDay : MonoBehaviour
    {
        public static TimeOfDay? Instance;
        public static float Current = 11.0f / 24f;
        public float DaySpeed = 0.001f;

        FieldInfo? _wbTimeField;
        bool _useWbTime;

        public static void EnsureCreated()
        {
            if (Instance != null) return;
            if (!Core.IsWorld3D) return;
            if (!Core.savedSettings.DayNightCycle && Core.savedSettings.FogDensity == 0f) return;
            if (Mod.Object == null) return;
            Mod.Object.AddComponent<TimeOfDay>();
        }

        void Awake()
        {
            Instance = this;
            _wbTimeField = typeof(MapBox).GetField("world_time",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static);
            _useWbTime = _wbTimeField != null && _wbTimeField.FieldType == typeof(float);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (_useWbTime && _wbTimeField != null)
            {
                object? boxed = _wbTimeField.IsStatic ? _wbTimeField.GetValue(null) : _wbTimeField.GetValue(MapBox.instance);
                if (boxed is float wt) Current = Mathf.Repeat(wt, 1f);
            }
            else
            {
                Current = Mathf.Repeat(Current + Time.deltaTime * DaySpeed, 1f);
            }
            SunDriver.TimeOfDay = Current * 24f;

            if (Core.savedSettings.DayNightCycle || Core.savedSettings.FogDensity > 0f)
            {
                RenderSettings.fog = Core.savedSettings.FogDensity > 0f || Core.savedSettings.DayNightCycle;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = Core.savedSettings.FogDensity;
            }

            WorldSphereMod.API.WorldSphereModAPI.RaiseTimeOfDay(Current);
        }
    }
}
