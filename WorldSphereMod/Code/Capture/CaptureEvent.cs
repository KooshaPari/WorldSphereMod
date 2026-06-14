using System.Collections.Generic;

namespace WorldSphereMod.Capture
{
    /// <summary>
    /// Normalized, replay-stable, diffable input event. One per user action.
    ///
    /// Design goals (see docs/adr/ADR-input-capture-substrate.md):
    ///  - <b>Replay-stable</b>: every field maps 1:1 onto a bridge /actions/* call so a recorded
    ///    event can be re-driven through the SAME main-thread action path with no interpretation.
    ///  - <b>Diffable</b>: stable field order + canonical types (no Unity structs) so two sessions'
    ///    JSONL streams diff cleanly and a flow library can dedupe/merge over time.
    ///  - <b>Engine-agnostic shape</b>: the schema is the WSM3D realization of the shared
    ///    phenotype-inputcapture event contract; only <see cref="Type"/> values and the
    ///    <see cref="Args"/> keys are game-specific.
    ///
    /// JSONL line example:
    /// {"type":"use_tool","t":1717000000123,"frame":9001,"args":{"id":"fire","x":128,"y":64}}
    /// </summary>
    public sealed class CaptureEvent
    {
        /// <summary>Schema version; bump on any breaking change to <see cref="Args"/> keys.</summary>
        public int v = 1;

        /// <summary>
        /// Canonical action type. Mirrors the bridge POST /actions/* surface so replay is a direct
        /// dispatch: new_world | regenerate | load_save | save | select_tool | use_tool |
        /// camera | set_speed | pause | play.
        /// </summary>
        public string type;

        /// <summary>Unix epoch milliseconds (UTC) when the action was captured.</summary>
        public long t;

        /// <summary>Unity Time.frameCount at capture; used for inter-event pacing on replay.</summary>
        public int frame;

        /// <summary>
        /// Action arguments as string-keyed primitives (string/number/bool) so the stream is
        /// JSON-canonical and diffable. Keys per type:
        ///  use_tool/select_tool : id, x, y
        ///  camera               : x, y, zoom
        ///  set_speed            : speed
        ///  load_save            : slot
        ///  new_world/save/pause/play/regenerate : (none)
        /// </summary>
        public Dictionary<string, object> args = new Dictionary<string, object>();

        public CaptureEvent() { }

        public CaptureEvent(string type, long t, int frame)
        {
            this.type = type;
            this.t = t;
            this.frame = frame;
        }

        public CaptureEvent Arg(string key, object value)
        {
            args[key] = value;
            return this;
        }
    }

    public static class CaptureEventTypes
    {
        public const string NewWorld = "new_world";
        public const string Regenerate = "regenerate";
        public const string LoadSave = "load_save";
        public const string Save = "save";
        public const string SelectTool = "select_tool";
        public const string UseTool = "use_tool";
        public const string Camera = "camera";
        public const string SetSpeed = "set_speed";
        public const string Pause = "pause";
        public const string Play = "play";
    }
}
