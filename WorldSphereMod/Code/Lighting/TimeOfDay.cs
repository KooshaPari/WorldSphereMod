using System.Reflection;
using UnityEngine;

namespace WorldSphereMod.Lighting
{
    [Phase(nameof(SavedSettings.DayNightCycle))]
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
        float _lastMovingWorldTimeAt;
        const float _worldTimeLerpSpeed = 14f;
        const float _worldTimeStaticFallbackSeconds = 0.75f;

        static readonly int _wsmFogDensity = Shader.PropertyToID("_WSM_FogDensity");
        static readonly int _wsmFogColor = Shader.PropertyToID("_WSM_FogColor");

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
            if (!Core.IsWorld3D || Core.savedSettings == null || !Core.savedSettings.DayNightCycle)
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
                        _lastMovingWorldTimeAt = Time.unscaledTime;
                        _worldTimeRate = DaySpeed;
                        _seededFromWorldTime = true;
                    }
                    else
                    {
                        float sampleAge = Time.unscaledTime - _lastWorldTimeSampleAt;
                        float delta = Mathf.DeltaAngle(_lastWorldTime * 360f, worldTime * 360f) / 360f;
                        bool worldTimeMoving = sampleAge > 0f && Mathf.Abs(delta) > 0.00001f;
                        if (worldTimeMoving)
                        {
                            float targetRate = delta / sampleAge;
                            float catchup = 1f - Mathf.Exp(-_worldTimeLerpSpeed * sampleAge);
                            _worldTimeRate = Mathf.Lerp(_worldTimeRate, targetRate, catchup);
                            _lastWorldTime = worldTime;
                            _lastWorldTimeSampleAt = Time.unscaledTime;
                            _lastMovingWorldTimeAt = Time.unscaledTime;
                        }
                        else if (Time.unscaledTime - _lastMovingWorldTimeAt >= _worldTimeStaticFallbackSeconds)
                        {
                            _worldTimeRate = DaySpeed;
                        }
                    }
                    if (Time.unscaledTime - _lastMovingWorldTimeAt < _worldTimeStaticFallbackSeconds)
                    {
                        float worldDriven = Mathf.Repeat(Current + Time.deltaTime * _worldTimeRate, 1f);
                        float t = 1f - Mathf.Exp(-_worldTimeLerpSpeed * Time.deltaTime);
                        Current = Mathf.Repeat(Mathf.LerpAngle(worldDriven * 360f, worldTime * 360f, t) / 360f, 1f);
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
            }
            else
            {
                Current = Mathf.Repeat(Current + Time.deltaTime * DaySpeed, 1f);
            }
            SunDriver.TimeOfDay = Current * 24f;
            // Pump the sun driver so the directional light rotation + colour and the
            // ambient SH bands track the day/night curve. Previously SunDriver.Update
            // was never called by anyone, so the sun was frozen at its Init angle.
            SunDriver.Update();
            ApplyFog(Current);

            WorldSphereMod.API.WorldSphereModAPI.RaiseTimeOfDay(Current);
        }

        static void ApplyFog(float t)
        {
            float density = Mathf.Max(0f, Core.savedSettings.FogDensity);
            Color fogColor = SunRig.FogColor(t);

            bool fogOn = density > 0f;
            RenderSettings.fog = fogOn;
            if (fogOn)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = density;
            }

            Shader.SetGlobalFloat(_wsmFogDensity, density);
            Shader.SetGlobalColor(_wsmFogColor, fogColor);
        }
    }
}
