using System.Diagnostics;
using UnityEngine;

namespace WorldSphereMod
{
    /// <summary>
    /// One-shot Init profiler. Wraps a named action with a Stopwatch + Debug.Log.
    /// Use sparingly — only around suspected hotspots, not every method call.
    /// This diagnostic tool measures Init-time performance of expensive operations
    /// before they're optimized away by phase-gating or batching.
    /// </summary>
    public static class InitProfiler
    {
        /// <summary>
        /// Measures elapsed time of an action and logs it to the console with [WSM3D] prefix.
        /// </summary>
        /// <param name="name">Short name of the operation being measured</param>
        /// <param name="action">The operation to measure</param>
        public static void Measure(string name, System.Action action)
        {
            var sw = Stopwatch.StartNew();
            try { action(); }
            finally
            {
                sw.Stop();
                float seconds = (float)sw.Elapsed.TotalSeconds;
                float milliseconds = (float)sw.ElapsedMilliseconds;
                UnityEngine.Debug.Log($"[WSM3D] InitProfiler {name} = {seconds:F4}s ({milliseconds}ms)");
            }
        }
    }
}
