# Game Bridge RPC Design

## Decision

Use the existing `Tools/wsm3d-mcp/` FastMCP server as the localhost RPC
front-end instead of adding a second in-process `HttpListener` inside the mod.
This keeps one transport, one health check surface, and one place for test
automation to connect. The Unity mod still owns the game actions; FastMCP is
only the localhost RPC envelope.

## Protocol

- Transport: HTTP over `127.0.0.1` only.
- Payload format: JSON request/response bodies, one method per call.
- RPC style: tool-oriented methods exposed through FastMCP, not protobuf.
- Reason: the repo already ships a FastMCP server on `8765`, already has a
  `/health` endpoint, and already uses JSON for the rest of the dev loop.

The bridge methods are:

- `load_save(path)`
- `toggle_phase(name, value)`
- `wait_until_world_loaded(timeout_ms)`
- `advance_frames(n)`
- `capture_screenshot(path)`
- `query_telemetry()`
- `shutdown()`

## API Shape

All methods return a JSON object with at least `ok: bool`. On failure, return
`ok: false` plus `error: string`. Successful methods return method-specific
fields:

- `load_save(path)` -> `save_path`, `loaded`, `world_name?`, `elapsed_ms?`
- `toggle_phase(name, value)` -> `name`, `old_value`, `new_value`
- `wait_until_world_loaded(timeout_ms)` -> `ready`, `elapsed_ms`, `reason?`
- `advance_frames(n)` -> `requested_frames`, `advanced_frames`, `frame_index`
- `capture_screenshot(path)` -> `path`, `width`, `height`, `frame_index`
- `query_telemetry()` -> telemetry JSON object
- `shutdown()` -> `drained`, `listener_closed`

`query_telemetry()` must return a stable JSON object, not a string blob. The
minimum fields are:

- `FrameMs`
- `CacheHitRates` with named cache ratios
- `DrawCalls`

## Threading Model

Unity API calls must never execute directly on the HTTP request thread. Each
bridge method enqueues a command into a main-thread queue owned by a Harmony-
injected `MonoBehaviour`. The queue is drained from `Update()`.

Rules:

- Pure bookkeeping may happen off-thread.
- Anything that touches save loading, scene objects, camera state, screenshots,
  or frame progression must run on the main thread.
- `wait_until_world_loaded()` polls bridge state and may block the caller, but
  the actual readiness transition is still observed on the main thread.
- `advance_frames(n)` should enqueue `n` frame ticks or a deterministic frame
  pump, then wait until the target frame count is reached.

This keeps Unity-safe work serialized while still allowing the FastMCP server to
serve requests concurrently.

## Auth

No auth for the initial design. Bind only to loopback and treat the endpoint as
developer-local automation. That matches the current FastMCP setup and avoids
credential management for a tool that is not meant to be exposed beyond the
machine running WorldBox.

If this ever leaves localhost, add a bearer token or nonce handshake later.

## Shutdown

`shutdown()` is graceful, not destructive:

- stop accepting new bridge requests
- drain or cancel pending queued work
- flush any last telemetry snapshot
- close the bridge listener/session

It does not kill WorldBox. Game process lifecycle remains in the existing
`game_kill` / `game_launch` surface.

## Suggested Placement

- Python FastMCP: add bridge tools under `Tools/wsm3d-mcp/wsm3d_mcp/tools/`
  and register them in `server.py`.
- Unity side: add one bridge component plus a main-thread command queue, using
  the same pattern as the mod’s other per-frame drivers.

## Why This Route

The in-process `HttpListener` option would duplicate transport, auth, parsing,
and shutdown code inside Unity. FastMCP already provides the localhost RPC
surface, existing health checks, and the right dev ergonomics for test
automation. The only Unity-specific work left is command execution and
telemetry gathering.
