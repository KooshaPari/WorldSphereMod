using System;
using System.Globalization;
using System.IO;
using System.Text;
using NeoModLoader.constants;
using Newtonsoft.Json;
using UnityEngine;

namespace WorldSphereMod.Capture
{
    /// <summary>
    /// Passive, append-only recorder for the user's in-game input stream.
    ///
    /// One session = one JSONL file under
    /// <c>mods_config/wsm3d-input-capture/session-&lt;utcTs&gt;.jsonl</c>; each line is one
    /// <see cref="CaptureEvent"/> serialized compact. Append-only + line-delimited so the file is
    /// crash-safe (a torn last line is the only loss) and cheaply tailable/diffable while a flow
    /// library accretes over sessions.
    ///
    /// Threading: <see cref="Record"/> is only ever called from Unity-main-thread Harmony Postfix
    /// hooks (see <see cref="CaptureHooks"/>), so no lock is needed for ordering; a lock guards the
    /// writer against the (rare) replay-driven re-entrancy and session rotation.
    ///
    /// Gating: recording is on only when <see cref="Enabled"/> (backed by
    /// SavedSettings.InputCaptureEnabled, default true for now) AND not currently replaying — we
    /// never record our own replayed actions back into the live session.
    /// </summary>
    public static class CaptureRecorder
    {
        const string FolderName = "wsm3d-input-capture";

        static readonly object _gate = new object();
        static StreamWriter _writer;
        static string _sessionPath;
        static long _eventCount;

        /// <summary>Set true while <see cref="CaptureReplayer"/> drives actions so replay never self-records.</summary>
        public static volatile bool SuppressForReplay;

        /// <summary>Effective on/off: settings flag AND not replaying.</summary>
        public static bool Enabled
        {
            get
            {
                if (SuppressForReplay) return false;
                try { return Core.savedSettings == null || Core.savedSettings.InputCaptureEnabled; }
                catch { return true; }
            }
        }

        public static string SessionPath { get { lock (_gate) { return _sessionPath; } } }
        public static long EventCount { get { lock (_gate) { return System.Threading.Interlocked.Read(ref _eventCount); } } }

        /// <summary>Root dir for all capture artifacts (sessions + saved named flows).</summary>
        public static string CaptureRoot => Path.Combine(ModConfigRoot, FolderName);

        // Resolve the mods_config dir the same way the rest of WSM3D does (Core.SaveSettings,
        // VoxelDiskCache): Paths.ModsConfigPath is WorldBox/NML's own cross-platform resolver
        // (LocalLow on Windows, ~/Library/.. on macOS, ~/.config/.. on Linux). Falling back to a
        // hardcoded Windows AppData/LocalLow path broke macOS/Linux.
        static string ModConfigRoot
        {
            get
            {
                try { return Paths.ModsConfigPath; }
                catch { return Application.persistentDataPath; }
            }
        }

        /// <summary>
        /// Record one normalized event. No-op when disabled. Lazily opens the session file on the
        /// first event so empty sessions never create files. Never throws into a game hook.
        /// </summary>
        public static void Record(CaptureEvent evt)
        {
            if (evt == null || !Enabled) return;
            try
            {
                lock (_gate)
                {
                    EnsureWriter();
                    if (_writer == null) return;
                    _writer.WriteLine(JsonConvert.SerializeObject(evt, Formatting.None));
                    _writer.Flush();
                    System.Threading.Interlocked.Increment(ref _eventCount);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WSM3D][Capture] record failed (" + (evt.type ?? "?") + "): " + ex.Message);
            }
        }

        /// <summary>Convenience: build + record an event at the current wall-clock + frame.</summary>
        public static CaptureEvent Emit(string type)
        {
            long t = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            int frame = 0;
            try { frame = Time.frameCount; } catch { }
            return new CaptureEvent(type, t, frame);
        }

        static void EnsureWriter()
        {
            if (_writer != null) return;
            Directory.CreateDirectory(CaptureRoot);
            string ts = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff'Z'", CultureInfo.InvariantCulture);
            _sessionPath = Path.Combine(CaptureRoot, "session-" + ts + ".jsonl");
            _writer = new StreamWriter(new FileStream(_sessionPath, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            _eventCount = 0;
            Debug.Log("[WSM3D][Capture] recording session -> " + _sessionPath);
        }

        /// <summary>Close the active session writer (e.g. on world unload). Safe to call repeatedly.</summary>
        public static void CloseSession()
        {
            lock (_gate)
            {
                try { _writer?.Flush(); _writer?.Dispose(); } catch { }
                _writer = null;
            }
        }
    }
}
