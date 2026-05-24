using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WorldSphereMod.Perf
{
    /// <summary>
    /// Per-system stopwatch accumulator. <see cref="Begin"/> / <see cref="End"/>
    /// brackets feed a 1 s rolling window; <see cref="Tick"/> (driven by
    /// <see cref="ProfilerFrameDriver"/>) flushes a single log line per window.
    ///
    /// Gated end-to-end by <c>Core.savedSettings.ProfilerDump</c>: when off, every
    /// public entry collapses to a single branch — no dictionary lookups, no
    /// stopwatch calls. Step 7 sprinkles the actual <c>Begin</c>/<c>End</c>
    /// brackets through the hot paths; this file is just the framework.
    /// </summary>
    public static class FrameProfiler
    {
        const float kWindowSize = 1.0f;

        static readonly Dictionary<string, double> _totalMs = new Dictionary<string, double>();
        static readonly Dictionary<string, Stopwatch> _running = new Dictionary<string, Stopwatch>();
        static readonly Dictionary<string, long> _begin = new Dictionary<string, long>();
        static float _windowElapsed;

        public static void Register(string key)
        {
            if (!_totalMs.ContainsKey(key)) _totalMs[key] = 0.0;
            if (!_running.ContainsKey(key)) _running[key] = new Stopwatch();
        }

        public static void Begin(string key)
        {
            if (!Core.savedSettings.ProfilerDump) return;
            _begin[key] = Stopwatch.GetTimestamp();
        }

        public static void End(string key)
        {
            if (!Core.savedSettings.ProfilerDump) return;
            if (!_begin.TryGetValue(key, out long start)) return;
            double elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            if (_totalMs.ContainsKey(key)) _totalMs[key] += elapsedMs;
            else _totalMs[key] = elapsedMs;
        }

        public static void Tick(float dt)
        {
            if (!Core.savedSettings.ProfilerDump) return;
            _windowElapsed += dt;
            if (_windowElapsed < kWindowSize) return;

            string line = "[WSM-PROF] " + string.Join(" | ",
                _totalMs.Select(kv => $"{kv.Key}={kv.Value:F2}ms"));
            Debug.Log(line);

            var keys = _totalMs.Keys.ToList();
            for (int i = 0; i < keys.Count; i++) _totalMs[keys[i]] = 0.0;
            _windowElapsed = 0f;
        }
    }
}
