using System;
using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Render-failure taxonomy. Each value maps to a distinct in-world marker color
    /// (see <see cref="RenderErrorMarkers"/>) so a single screenshot shows WHAT failed
    /// WHERE, GMod-ERROR-prop style. Add a value here + a color in RenderErrorMarkers
    /// when a new failure point is wired.
    /// </summary>
    public enum RenderErrorType
    {
        ShaderFailed = 0,    // instanced variant missing / InternalError → magenta
        MeshBuildFailed = 1, // voxelization produced empty/invalid mesh → red
        VoxelNotReady = 2,   // async mesh build still pending → yellow
        MaterialNull = 3,    // EnsureMaterial() returned no usable material → orange
        Unsupported = 4,     // GPU/feature unsupported (no instancing, etc.) → cyan
        SpriteNull = 5,      // render_data had no source sprite to voxelize → grey
    }

    /// <summary>
    /// HEXAGONAL CORE of the diagnostics system. Render paths report failures here via
    /// <see cref="Record"/>; sinks (visual ERROR props, /diag/errors JSON, the [ERRORS]
    /// summary log, the per-object overlay) read from it. Telemetry is ALWAYS recorded —
    /// only the visual prop is gated behind SavedSettings.RenderErrorProps.
    ///
    /// Thread-safety: render emit postfixes run on Unity's main thread; the bridge reads
    /// via InvokeOnMainThread. All access is therefore main-thread; a lock guards the
    /// rare cross-thread read so a listener-thread call can't tear the dictionaries.
    /// </summary>
    public static class RenderErrorRegistry
    {
        public sealed class Example
        {
            public string name;
            public string reason;
            public float x, y, z;
        }

        sealed class Entry
        {
            public long count;
            public readonly List<Example> examples = new List<Example>(MaxExamplesPerType);
        }

        const int MaxExamplesPerType = 5;
        // Summary log throttle: only emit when counts change OR this many seconds elapse.
        const float SummaryMinInterval = 5f;

        static readonly object _lock = new object();
        static readonly Dictionary<RenderErrorType, Entry> _entries = new Dictionary<RenderErrorType, Entry>();
        // Snapshot of per-type counts at last summary emit — used for on-change detection.
        static readonly Dictionary<RenderErrorType, long> _lastSummaryCounts = new Dictionary<RenderErrorType, long>();
        static float _lastSummaryTime = -999f;

        // Markers collected THIS frame for the visual sink. Emit postfixes append; the
        // frame driver drains them after Flush. Frame-scoped so stale positions don't
        // linger after an object moves or recovers.
        public struct Marker
        {
            public RenderErrorType Type;
            public Vector3 Pos;
        }
        static readonly List<Marker> _frameMarkers = new List<Marker>(256);

        /// <summary>
        /// Report a render failure. ALWAYS records telemetry. When RenderErrorProps is on,
        /// also queues a typed in-world marker at <paramref name="worldPos"/> for this frame.
        /// </summary>
        public static void Record(RenderErrorType type, string objectName, string reason, Vector3 worldPos)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(type, out Entry e))
                {
                    e = new Entry();
                    _entries[type] = e;
                }
                e.count++;
                if (e.examples.Count < MaxExamplesPerType)
                {
                    e.examples.Add(new Example
                    {
                        name = string.IsNullOrEmpty(objectName) ? "<unnamed>" : objectName,
                        reason = reason ?? string.Empty,
                        x = worldPos.x,
                        y = worldPos.y,
                        z = worldPos.z,
                    });
                }

                // VISUAL SINK GATE: only the prop is gated; the telemetry above is unconditional.
                if (Core.savedSettings != null && Core.savedSettings.RenderErrorProps)
                {
                    _frameMarkers.Add(new Marker { Type = type, Pos = worldPos });
                }
            }
        }

        /// <summary>Drain this frame's queued markers for the visual sink. Clears the buffer.</summary>
        public static void DrainFrameMarkers(List<Marker> into)
        {
            lock (_lock)
            {
                into.AddRange(_frameMarkers);
                _frameMarkers.Clear();
            }
        }

        /// <summary>Drop frame markers without rendering (called when the visual sink is off).</summary>
        public static void ClearFrameMarkers()
        {
            lock (_lock) { _frameMarkers.Clear(); }
        }

        /// <summary>Per-type count for the current process. 0 if never recorded.</summary>
        public static long CountOf(RenderErrorType type)
        {
            lock (_lock) { return _entries.TryGetValue(type, out Entry e) ? e.count : 0L; }
        }

        /// <summary>Total failures recorded across all types.</summary>
        public static long TotalCount()
        {
            lock (_lock)
            {
                long total = 0;
                foreach (var kv in _entries) total += kv.Value.count;
                return total;
            }
        }

        /// <summary>
        /// Snapshot for the /diag/errors bridge sink: per-type count + sample examples.
        /// Returns POD lists safe to JSON-serialize off the registry's lock.
        /// </summary>
        public static List<TypeReport> Snapshot()
        {
            var result = new List<TypeReport>();
            lock (_lock)
            {
                foreach (RenderErrorType t in (RenderErrorType[])Enum.GetValues(typeof(RenderErrorType)))
                {
                    if (!_entries.TryGetValue(t, out Entry e) || e.count == 0) continue;
                    var report = new TypeReport
                    {
                        type = t.ToString(),
                        count = e.count,
                        examples = new List<Example>(e.examples.Count),
                    };
                    // Copy examples so callers can't mutate registry state.
                    for (int i = 0; i < e.examples.Count; i++)
                    {
                        Example src = e.examples[i];
                        report.examples.Add(new Example { name = src.name, reason = src.reason, x = src.x, y = src.y, z = src.z });
                    }
                    result.Add(report);
                }
            }
            return result;
        }

        public sealed class TypeReport
        {
            public string type;
            public long count;
            public List<Example> examples;
        }

        /// <summary>
        /// LOW-FREQUENCY structured summary log sink. Emits "[WSM3D][ERRORS] ..." only when
        /// counts changed OR <see cref="SummaryMinInterval"/> elapsed — NOT per-frame, so it
        /// stays grep-able after a run without flooding the console. Call once per frame from
        /// the frame driver; the throttle is internal.
        /// </summary>
        public static void MaybeEmitSummary()
        {
            lock (_lock)
            {
                bool changed = false;
                foreach (var kv in _entries)
                {
                    _lastSummaryCounts.TryGetValue(kv.Key, out long prev);
                    if (prev != kv.Value.count) { changed = true; break; }
                }
                if (_entries.Count == 0) return;

                float now = Time.realtimeSinceStartup;
                if (!changed && (now - _lastSummaryTime) < SummaryMinInterval) return;

                _lastSummaryTime = now;
                var sb = new System.Text.StringBuilder("[WSM3D][ERRORS]");
                foreach (RenderErrorType t in (RenderErrorType[])Enum.GetValues(typeof(RenderErrorType)))
                {
                    long c = _entries.TryGetValue(t, out Entry e) ? e.count : 0L;
                    _lastSummaryCounts[t] = c;
                    sb.Append(' ').Append(ToCamel(t)).Append('=').Append(c);
                }
                Debug.Log(sb.ToString());
            }
        }

        static string ToCamel(RenderErrorType t)
        {
            string s = t.ToString();
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>Clear all counters + markers. Call from VoxelRender.Reset on world reload.</summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _entries.Clear();
                _lastSummaryCounts.Clear();
                _frameMarkers.Clear();
                _lastSummaryTime = -999f;
            }
        }
    }
}
