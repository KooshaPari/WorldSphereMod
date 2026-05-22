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

