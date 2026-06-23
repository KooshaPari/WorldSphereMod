using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using WorldSphereMod.Bridge;

namespace WorldSphereMod.Capture
{
    /// <summary>
    /// Replays a recorded flow headlessly by dispatching each <see cref="CaptureEvent"/> through the
    /// SAME <see cref="BridgeActions"/> entrypoints the bridge POST /actions/* routes use. Because
    /// the recorder hooks the backing methods those actions drive, capture→replay is a closed loop:
    /// load+navigate+spawn-for-verify captured from a real session re-runs without the user.
    ///
    /// MUST be invoked on the Unity main thread (BridgeActions touches live engine state). The
    /// bridge route queues <see cref="ReplayFile"/> onto the main-thread queue. Recording is
    /// suppressed for the duration so replayed actions don't pollute the live session.
    ///
    /// load_save is intentionally async in the engine (it queues a world load), so we replay it via
    /// the same deferred path the bridge uses and stop applying further events in this pass — the
    /// caller can chain a second replay (or the post-load events) once the world is ready. For the
    /// common flow shape this keeps replay deterministic and crash-free.
    /// </summary>
    public static class CaptureReplayer
    {
        public sealed class StepResult
        {
            public int index;
            public string type;
            public bool ok;
            public object result;
            public string error;
        }

        public sealed class ReplayReport
        {
            public bool ok;
            public string file;
            public int total;
            public int applied;
            public int skipped;
            public int failed;
            public string note;
            public List<StepResult> steps = new List<StepResult>();
        }

        /// <summary>Replay every event in <paramref name="flowFile"/>. Main thread only.</summary>
        public static ReplayReport ReplayFile(string flowFile)
        {
            var report = new ReplayReport { file = flowFile };
            string path = FlowLibrary.ResolveExisting(flowFile);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                report.ok = false;
                report.note = "flow_not_found";
                return report;
            }

            report.file = path;
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception ex) { report.ok = false; report.note = "read_failed:" + ex.Message; return report; }

            bool prevSuppress = CaptureRecorder.SuppressForReplay;
            CaptureRecorder.SuppressForReplay = true;
            try
            {
                int i = 0;
                foreach (string raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    report.total++;
                    CaptureEvent evt = null;
                    try { evt = JsonConvert.DeserializeObject<CaptureEvent>(raw); }
                    catch { }
                    if (evt == null || string.IsNullOrEmpty(evt.type))
                    {
                        report.skipped++;
                        report.steps.Add(new StepResult { index = i++, type = "?", ok = false, error = "unparseable" });
                        continue;
                    }

                    var step = new StepResult { index = i++, type = evt.type };
                    try
                    {
                        object result = Dispatch(evt);
                        step.result = result;
                        step.ok = ResultOk(result);
                        if (step.ok) report.applied++; else report.failed++;
                    }
                    catch (Exception ex)
                    {
                        step.ok = false;
                        step.error = ex.Message;
                        report.failed++;
                    }
                    report.steps.Add(step);

                    // A world load is async; stop applying further (stale) events this pass.
                    if (evt.type == CaptureEventTypes.LoadSave)
                    {
                        report.note = "stopped_after_load_save(async world load); chain a follow-up replay for post-load events";
                        break;
                    }
                }
            }
            finally { CaptureRecorder.SuppressForReplay = prevSuppress; }

            report.ok = report.failed == 0;
            return report;
        }

        /// <summary>Route one normalized event to the matching headless action.</summary>
        static object Dispatch(CaptureEvent e)
        {
            switch (e.type)
            {
                case CaptureEventTypes.NewWorld:   return BridgeActions.NewWorld();
                case CaptureEventTypes.Regenerate: return BridgeActions.Regenerate();
                case CaptureEventTypes.Save:       return BridgeActions.Save();
                case CaptureEventTypes.Pause:      return BridgeActions.Pause();
                case CaptureEventTypes.Play:       return BridgeActions.Play();
                case CaptureEventTypes.SetSpeed:   return BridgeActions.SetSpeed(Str(e, "speed"));
                case CaptureEventTypes.SelectTool: return BridgeActions.SelectTool(Str(e, "id"));
                case CaptureEventTypes.UseTool:    return BridgeActions.UseTool(Str(e, "id"), Str(e, "x"), Str(e, "y"));
                case CaptureEventTypes.Camera:     return BridgeActions.Camera(Str(e, "x"), Str(e, "y"), Str(e, "zoom"));
                case CaptureEventTypes.LoadSave:   return ReplayLoadSave(e);
                default: return new { ok = false, error = "unknown_event_type:" + e.type };
            }
        }

        // load_save is not a BridgeActions method (the bridge queues it directly). Re-drive the same
        // deferred engine path so replay matches the operator's POST /actions/load_save semantics.
        static object ReplayLoadSave(CaptureEvent e)
        {
            try
            {
                if (World.world == null || World.world.save_manager == null)
                    return new { ok = false, error = "world_not_ready" };

                int slot = 0;
                int.TryParse(Str(e, "slot"), NumberStyles.Integer, CultureInfo.InvariantCulture, out slot);
                string path = Str(e, "path");
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    path = ResolveSavePath(slot);
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    return new { ok = false, error = "missing_save", slot };

                SaveManager.setCurrentPathAndId(path, slot);
                World.world.save_manager.prepareLoading();
                World.world.save_manager.loadWorld(path, false);
                return new { ok = true, slot, path };
            }
            catch (Exception ex) { return new { ok = false, error = ex.Message }; }
        }

        static string ResolveSavePath(int slot)
        {
            string root = SaveManager.persistentDataPath;
            if (string.IsNullOrEmpty(root)) root = Application.persistentDataPath;
            return Path.Combine(root, "saves", "save" + slot.ToString(CultureInfo.InvariantCulture));
        }

        static string Str(CaptureEvent e, string key)
        {
            if (e.args == null || !e.args.TryGetValue(key, out object v) || v == null) return string.Empty;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        // BridgeActions return anonymous { ok = ... }; read it reflectively.
        static bool ResultOk(object result)
        {
            if (result == null) return false;
            try
            {
                var p = result.GetType().GetProperty("ok");
                if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(result, null);
            }
            catch { }
            return true; // non-standard shape: treat as applied
        }
    }
}
