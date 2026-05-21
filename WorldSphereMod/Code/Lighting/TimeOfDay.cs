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
        bool _seededFromWorldTime;
        float _lastWorldTime;
        float _lastWorldTimeSampleAt;
        float _worldTimeRate = 0.001f;

        public static void EnsureCreated()
        {
            if (Instance != null) return;
            if (!Core.IsWorld3D) return;
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
            if (!Core.IsWorld3D)
            {
                return;
            }

            if (_useWbTime && _wbTimeField != null)
            {
                object? boxed = _wbTimeField.IsStatic ? _wbTimeField.GetValue(null) : _wbTimeField.GetValue(MapBox.instance);
                if (boxed is float wt)
                {
                    float worldTime = Mathf.Repeat(wt, 1f);
                    if (!_seededFromWorldTime)
                    {
                        Current = worldTime;
                        _lastWorldTime = worldTime;
                        _lastWorldTimeSampleAt = Time.unscaledTime;
                        _worldTimeRate = DaySpeed;
                        _seededFromWorldTime = true;
                    }
                    else
                    {
                        float sampleAge = Time.unscaledTime - _lastWorldTimeSampleAt;
                        float delta = Mathf.DeltaAngle(_lastWorldTime * 360f, worldTime * 360f) / 360f;
                        if (sampleAge > 0f && Mathf.Abs(delta) > Mathf.Epsilon)
                        {
                            _worldTimeRate = delta / sampleAge;
                            _lastWorldTime = worldTime;
                            _lastWorldTimeSampleAt = Time.unscaledTime;
                        }
                    }
                    Current = Mathf.Repeat(Current + Time.deltaTime * _worldTimeRate, 1f);
                }
                else
                {
                    Current = Mathf.Repeat(Current + Time.deltaTime * DaySpeed, 1f);
                }
            }
            else
            {
                Current = Mathf.Repeat(Current + Time.deltaTime * DaySpeed, 1f);
            }
            SunDriver.TimeOfDay = Current * 24f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = SunRig.AmbientColor(Current) * 0.8f;
            RenderSettings.fogDensity = Core.savedSettings.FogDensity > 0f ? Core.savedSettings.FogDensity : 0.0125f;

            WorldSphereMod.API.WorldSphereModAPI.RaiseTimeOfDay(Current);
        }
    }
}
