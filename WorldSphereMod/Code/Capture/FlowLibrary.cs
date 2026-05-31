using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WorldSphereMod.Capture
{
    /// <summary>
    /// The accumulating library of recorded flows. A "flow" is just a JSONL event stream — either a
    /// raw <c>session-*.jsonl</c> (auto-captured) or a named <c>flow-&lt;name&gt;.jsonl</c> promoted
    /// from a session via <see cref="SaveAs"/>. Common setups (load+navigate+spawn-for-verify)
    /// accrete here over sessions and become invocable by name through the bridge.
    /// </summary>
    public static class FlowLibrary
    {
        const string FlowPrefix = "flow-";
        const string SessionPrefix = "session-";
        const string Ext = ".jsonl";

        static string Root => CaptureRecorder.CaptureRoot;

        public sealed class FlowInfo
        {
            public string name;
            public string file;
            public string kind;   // "flow" | "session"
            public long events;
            public long sizeBytes;
            public string modifiedUtc;
        }

        /// <summary>List every recorded flow + session, newest first.</summary>
        public static List<FlowInfo> List()
        {
            var infos = new List<FlowInfo>();
            try
            {
                if (!Directory.Exists(Root)) return infos;
                foreach (string path in Directory.GetFiles(Root, "*" + Ext))
                {
                    string fileName = Path.GetFileName(path);
                    bool isFlow = fileName.StartsWith(FlowPrefix, StringComparison.OrdinalIgnoreCase);
                    bool isSession = fileName.StartsWith(SessionPrefix, StringComparison.OrdinalIgnoreCase);
                    if (!isFlow && !isSession) continue;

                    var fi = new FileInfo(path);
                    string name = Path.GetFileNameWithoutExtension(fileName);
                    if (isFlow) name = name.Substring(FlowPrefix.Length);

                    long lineCount = 0;
                    try { lineCount = File.ReadLines(path).Count(l => !string.IsNullOrWhiteSpace(l)); } catch { }

                    infos.Add(new FlowInfo
                    {
                        name = name,
                        file = fileName,
                        kind = isFlow ? "flow" : "session",
                        events = lineCount,
                        sizeBytes = fi.Length,
                        modifiedUtc = fi.LastWriteTimeUtc.ToString("yyyyMMddTHHmmssfff'Z'")
                    });
                }
            }
            catch (Exception ex) { Debug.LogWarning("[WSM3D][Capture] list flows: " + ex.Message); }

            infos.Sort((a, b) => string.CompareOrdinal(b.modifiedUtc, a.modifiedUtc));
            return infos;
        }

        /// <summary>
        /// Promote a source session/flow to a named flow (<c>flow-&lt;name&gt;.jsonl</c>). When
        /// <paramref name="source"/> is null/empty, uses the active recording session.
        /// </summary>
        public static (bool ok, string path, string error) SaveAs(string name, string source)
        {
            try
            {
                string safe = Sanitize(name);
                if (string.IsNullOrEmpty(safe)) return (false, null, "invalid_name");

                string src = ResolveExisting(source) ?? CaptureRecorder.SessionPath;
                if (string.IsNullOrEmpty(src) || !File.Exists(src))
                    return (false, null, "no_source_session");

                Directory.CreateDirectory(Root);
                string dest = Path.Combine(Root, FlowPrefix + safe + Ext);
                // Flush the live session first so a save of the current session is complete.
                if (string.Equals(src, CaptureRecorder.SessionPath, StringComparison.OrdinalIgnoreCase))
                    File.Copy(src, dest, overwrite: true);
                else
                    File.Copy(src, dest, overwrite: true);
                return (true, dest, null);
            }
            catch (Exception ex) { return (false, null, ex.Message); }
        }

        /// <summary>Resolve a flow/session reference (name, "flow-x", "flow-x.jsonl", or absolute path) to a real file.</summary>
        public static string ResolveExisting(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            try
            {
                if (File.Exists(reference)) return Path.GetFullPath(reference);

                string r = reference.Trim();
                string[] candidates =
                {
                    r,
                    r.EndsWith(Ext, StringComparison.OrdinalIgnoreCase) ? r : r + Ext,
                    FlowPrefix + r + Ext,
                    SessionPrefix + r + Ext,
                };
                foreach (string c in candidates)
                {
                    string p = Path.Combine(Root, c);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var chars = name.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            return new string(chars);
        }
    }
}
