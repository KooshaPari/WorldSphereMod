# Bridge dies on save-load scene transition (known issue)

After 4 layered fixes (`56a2507`, `88a9ab9`, `de06d03`, `c18acca`)
the BridgeServer still goes unresponsive after a save load.

## Tried + still failing

- DontDestroyOnLoad on the GameObject (silently no-ops on non-root)
- Own root GameObject for the BridgeServer
- Static `_mainThreadQueue` (any instance drains)
- Drain on Update + LateUpdate + FixedUpdate
- Mod.PostInit re-invokes EnsureCreated (with destroyed-host detection)
- 5s `done.Wait(5000)` timeout (returns default(T) — at least no infinite hangs)

## Symptoms

- Bridge alive + responding cleanly at fresh game start (pre-save-load)
- Cache + telemetry endpoints return valid JSON
- After save load completes, all endpoints return `null` (5s timeout)
- Log shows `[WSM3D][Bridge] main-thread dispatch timed out` repeating
- Port stays bound (`netstat` shows LISTENING on 8766)
- HTTP requests accepted (connection ESTABLISHED) but never get processed

## Hypotheses not yet eliminated

1. WorldBox scene-load uses LoadSceneMode.Single → destroys even DontDestroyOnLoad-marked roots
2. Mod.PostInit only fires once at first world load, not on subsequent
3. Some other MonoBehaviour with Update isn't firing either
4. Game's main thread is in a tight loop that doesn't process Update callbacks for non-vanilla scripts

## Workarounds

- Restart game (kill+launch) → bridge healthy again until next save load
- Use `Tools/wsm3d.ps1 settings set` for disk-state changes
- Inspect Player.log directly for runtime telemetry

## Impact

- All 15 FRs LANDED PRE-SAVE-LOAD via bridge
- Most acceptance gates verified at fresh-start time
- Observability degrades after save load but mod core remains functional

## Update: VoxelFrameDriver dies same way (commit 16ba1b4)

Telemetry log entries plateau at 1 per session — VoxelFrameDriver.LateUpdate fires once at first IsWorld3D=true, then stops. Three escalating fixes attempted:

1. `93661b2` DontDestroyOnLoad on existing parent — no-op (not root)
2. `f20c63f` if(parent==null) DontDestroyOnLoad guard — no-op (parent always non-null)
3. `16ba1b4` SetParent(null) + DDoL — still 1 entry per session

This isn't a simple "make it root" problem. WorldBox's scene transition appears to destroy ALL non-engine GameObjects regardless of DDoL marker, OR re-creates the mod root and the new driver doesn't re-attach OnEnable doesn't fire on the surviving instance.

## Path forward (deferred)

- Hook telemetry into a vanilla Harmony Postfix on something WB calls every frame (MapBox.Update / ActorManager.update_actors postfix). Bypasses our MonoBehaviour entirely.
- That's also the fix for the bridge queue drain — Postfix a method WB definitely runs.

For now: bridge + log telemetry both unreliable post-save-load. Functional features (voxel rendering, phase toggles) still work — observability is the failing layer.


## Final attempt: Harmony Postfix on MapBox.renderStuff (commit 4d6a7de)

Even with explicit Patcher.PatchAll(typeof(BridgePerFrameTick)) registered alongside the ~20 other patches in Core.cs, the Postfix isn't logging. Either:
- MapBox.renderStuff isn't called when WorldBox is in 3D mode
- Patch silently fails to apply
- Something else

After 6 layered fixes (MonoBehaviour DDoL on existing parent → new root → SetParent(null) → triple-callback drain → static queue → Harmony Postfix on vanilla), still 1 telemetry entry per session post-world-load.

## Status: PARKED

Mod features work (PostFxController + voxel render proven via prior screenshots).
Observability degraded post-save-load.
Bridge + log telemetry both fail after world transitions to game scene.

Next session candidates:
- Try MapBox.Update instead of renderStuff
- Try Postfix on ActorManager.update_actors (definitely per-frame)
- Move observability into the existing Postfix chain that we KNOW fires (BuildingProcRender.EmitMeshes, etc — these increment FrameDrawCalls per the telemetry that DID work before save load)


## Status update 0d74a41: Telemetry log PARTIAL recovery

Switched Postfix target ActorManager.precalculateRenderDataParallel — entries are NOW GROWING:

```
[WSM3D][Telemetry] frameMs=16.67 ... gcMB=405.8
[WSM3D][Telemetry] frameMs=16.66 ... gcMB=415.3
```

2 entries after world load + 45s wait. The Postfix fires when actor processing runs (not strictly per-frame; depends on game state). Acceptable cadence for steady-state observability.

Bridge endpoints still time out (queue not draining via this hook either — DrainStaticQueue called but actions queued by listener thread maybe not flushing if main-thread context differs). But log telemetry is the resilient channel.

NFR-WSM-006 partial recovery: bridge pre-save-load, log post-save-load.

