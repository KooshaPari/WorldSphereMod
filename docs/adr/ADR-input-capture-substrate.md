# ADR: Shared `phenotype-inputcapture` substrate (capture → learn → replay)

- Status: Proposed (WSM3D realization implemented; reusable package = design-only)
- Date: 2026-05-30
- Owners: WSM3D chat (this repo). Civis integration is **another chat's call** (multi-chat repo ownership) — this ADR only describes the shared shape so that chat can adopt it.
- Traceability: closes the autonomy loop alongside `/diag/errors` (failure observability) and `/actions/*` (headless control). New capability = capturing the user's **navigation/setup** so the agent can learn and replay flows.

## Context

The WSM3D bridge already lets an agent *drive* the game headlessly (`POST /actions/{new_world,camera,select_tool,use_tool,set_speed,load_save,...}`) and *observe* it (`/health`, `/telemetry`, `/diag/*`, `/world/state`). What was missing: a way to **learn what the user actually does** — which tool on which tile, how they frame the camera, how they create/load worlds, what speed they run. Without that, the agent must be told every setup step.

This substrate passively records the user's input stream into a normalized, replay-stable, diffable event log, accreting a **flow library** over sessions, and replays any flow headlessly through the exact same action path the agent already drives. Capture and replay are therefore **symmetric by construction**: the recorder hooks the same backing engine methods that `/actions/*` invoke.

This is the WSM3D instance of a substrate that WSM3D, Civis, and future game-mods can share. Abstraction-at-2-uses (org convention) says: design the reusable shape now, extract the package when the second consumer (Civis) lands.

## Decision

### 1. Event schema (replay-stable + diffable)

One JSON object per user action, one per JSONL line:

```jsonc
{
  "v": 1,                 // schema version
  "type": "use_tool",     // canonical action type (mirrors /actions/* surface)
  "t": 1717000000123,     // unix epoch ms (UTC)
  "frame": 9001,          // Unity Time.frameCount at capture (inter-event pacing)
  "args": { "id": "fire", "x": 128, "y": 64 }  // string-keyed primitives only
}
```

Canonical `type` values (1:1 with bridge actions): `new_world`, `regenerate`, `load_save`,
`save`, `select_tool`, `use_tool`, `camera`, `set_speed`, `pause`, `play`.

Per-type `args`:

| type | args |
|------|------|
| `use_tool` / `select_tool` | `id`, (`x`,`y` for use) |
| `camera` | `x`, `y`, `zoom` |
| `set_speed` | `speed` (named time-scale id) |
| `load_save` | `slot`, `path` |
| `new_world`/`save`/`pause`/`play`/`regenerate` | — |

Why this shape:
- **Replay-stable**: each event maps directly onto one action dispatch with no interpretation.
- **Diffable**: stable field order, canonical primitives (no engine structs), rounded floats →
  two sessions diff cleanly; a flow library can dedupe/merge across sessions.
- **Engine-agnostic core**: `v`/`type`/`t`/`frame`/`args` are the shared contract; only the set of
  `type` values and `args` keys are game-specific.

### 2. Ports (hexagonal)

The substrate is two ports + a transport, all game-agnostic:

- **`IInputRecorder`** — `Record(CaptureEvent)`, `Emit(type)`, session lifecycle
  (open/append/close). Append-only line-delimited transport (crash-safe; tail/diff-friendly).
- **`IFlowLibrary`** — `List()`, `SaveAs(name, source)`, `Resolve(reference)`. Distinguishes
  raw auto-captured `session-*` streams from promoted, named `flow-*` flows.
- **`IFlowReplayer`** — `ReplayFile(flow)` → per-step report. Dispatches each event to the host's
  **action adapter** (the same one a remote control surface drives).

Adapters (game-specific, supplied by each consumer):
- **Capture adapter**: engine hooks that translate native input → `CaptureEvent` (in WSM3D:
  Harmony Postfixes; in Bevy/Civis: an input-system observer).
- **Action adapter**: maps `CaptureEvent` → a concrete engine call (in WSM3D: `BridgeActions`).

### 3. WSM3D realization (this repo)

- **Recorder** (`Code/Capture/CaptureRecorder.cs`): append-only JSONL at
  `mods_config/wsm3d-input-capture/session-<utcTs>.jsonl`. Lazy-opens on first event, flushes per
  line. Gated by `SavedSettings.InputCaptureEnabled` (**default on for now**) AND
  `!SuppressForReplay` so replayed actions never self-record. All writes lock-guarded; events are
  emitted from main-thread hooks.
- **Capture hooks** (`Code/Capture/CaptureHooks.cs`, all Harmony **Postfix**, exception-swallowing):
  | event | hooked backing method (same one `/actions/*` drives) |
  |-------|------|
  | `use_tool` | `PlayerControl.clickedFinal(Vector2Int, GodPower, bool)` — shared finger/brush click sink; resolves selected power when null |
  | `select_tool` | `PowerButtonSelector.setSelectedPower(PowerButton, GodPower, bool)` |
  | `new_world` | `MapBox.clickGenerateNewMap()` |
  | `load_save` | `SaveManager.loadWorld(string, bool)` (parses `saveN` → slot) |
  | `set_speed` | `Config.setWorldSpeed(string,bool)` + `(WorldTimeScaleAsset,bool)` (reads asset id) |
  | `camera` (zoom) | `MapBox.setZoomOrthographic(float)` → forces a camera-frame sample |
  - **camera (pan)**: `CaptureCameraSampler.Tick()` runs each frame from the existing
    `BridgeSurvival.Run` per-frame tick; debounced by distance + min interval so drag-panning emits
    settle points, not thousands of micro-events.
  - All WorldBox members reached via publicizer-safe access (build links against the publicized
    assembly; reflection for the asset `id`). **NML publicizer trap** respected — no assumed names.
- **Replayer** (`Code/Capture/CaptureReplayer.cs`): reads the JSONL and dispatches each event
  through `BridgeActions` (the same main-thread path `/actions/*` use). `load_save` is async (queues
  a world load) so replay stops after it and reports a chain-a-follow-up note. Sets
  `SuppressForReplay` for the pass. Returns a per-step `{index,type,ok,result,error}` report.
- **Flow library** (`Code/Capture/FlowLibrary.cs`): lists `session-*`/`flow-*`, promotes a session
  to `flow-<name>.jsonl`, resolves a flow by name/file/path.
- **Bridge routes** (added to `BridgeServer.cs` POST routing block + GET block, coordinating with
  existing routes — not rewriting them):
  - `GET  /capture/list` — flow library (sessions + named flows, newest first).
  - `GET  /capture/status` — enabled?, active session path, event count.
  - `POST /capture/replay?file=<flow>` — headless replay via `BridgeActions` (queued main-thread).
  - `POST /capture/save?name=<flow>[&source=<session|flow|path>]` — promote a session to a named flow.

### 4. Learning / flow library

The append-only normalized stream is the substrate for learning: because events are canonical and
diffable, repeated setups (e.g. *load slot 1 → frame the capital → spawn 20 humans for verify*)
recur as near-identical subsequences across sessions and can be clustered/named into reusable flows.
v1 ships the storage + manual promotion (`/capture/save`); automated flow mining (subsequence
clustering, naming) is a later layer that consumes the same JSONL with no schema change.

## Consumption by other engines (design-only)

- **WSM3D**: implemented here (Harmony + `BridgeActions` adapters).
- **Civis (Bevy)**: would implement `IInputRecorder`/`IFlowReplayer` over a Bevy input observer +
  command adapter; event schema unchanged. **Owned by another chat — not created here.**
- **Future game-mods**: implement the two adapters; reuse schema, recorder, library, replayer.

Extraction trigger (org convention, abstraction-at-2-uses): when Civis becomes the second consumer,
lift `CaptureEvent` + the three port interfaces + JSONL recorder + library + replayer skeleton into a
shared `phenotype-inputcapture` package; WSM3D keeps only its Harmony/`BridgeActions` adapters.

## Consequences

- Closes the autonomy loop: agent can now capture the user's setup and replay it headlessly.
- Passive + default-on: zero user effort to build a flow corpus; one settings flag to disable.
- Symmetric capture/replay (hooks target the action backing methods) → low replay drift.
- Async `load_save` means a load-bearing flow replays in two passes (load, then post-load); acceptable
  for v1 and explicitly reported.
- Recording is best-effort and never throws into gameplay; a torn final JSONL line is the only loss.
