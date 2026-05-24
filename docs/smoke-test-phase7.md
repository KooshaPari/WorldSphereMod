# Phase 7 in-game smoke test — checklist

What to verify when you toggle `WorldspaceUI = true` and `WorldspaceLabel3D = true` for the first time.

**Agentic automation:** [`Tools/wsm3d-playcua/sample-scenarios/phase-7-worldspace-ui.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-7-worldspace-ui.yaml) drives bridge `toggle_flag`, telemetry, and vision screenshot steps. Full programmatic + agentic gate order: [`docs/live-verification.md`](live-verification.md).

## Setup

```powershell
./Tools/install.ps1
```

That builds the mod (sanity check) and copies sources + assets to
`<WorldBox>/Mods/WorldSphereMod3D/`. Default WorldBox path is the standard Steam install.

Launch WorldBox. NeoModLoader compiles `Code/*.cs` at startup. If you see a red mod icon, the compute-shader/instancing hardware gate failed — Phase 10 will add a billboard fallback for that case; nothing to do here.

Load or generate a world with **units on screen** (village, army, selected leader). Save slot 2 is the baseline used by the PlayCUA scenario.

## Regression checks (with `WorldspaceUI = false` and `WorldspaceLabel3D = false`)

Keep `VoxelEntities` **ON** and prior phases as needed so 3D actors exist to label. Only Phase 7 worldspace UI is under test here.

Open the map. Confirm:

- Actors render without **floating nameplates, HP bars, or selection rings** in world space (vanilla screen UI may still appear).
- Settings tab → WorldSphere → **Worldspace UI** and **Worldspace Label 3D** toggles are present and OFF (or flip OFF if your save inherited default-on).
- Selecting a unit does not show a 3D torus ring at its feet.

If any of those fail, Phase 0–6 plumbing has regressed. Don't proceed.

## Enable worldspace UI + 3D labels

1. Settings → WorldSphere → toggle **Worldspace UI** ON.
2. Toggle **Worldspace Label 3D** ON (Phase 7b; bundled in the same smoke pass).
3. Select a unit and pan the camera: top-down, low-angle, zoom in/out past fade distances.
4. Optional bridge run (game + listener on `127.0.0.1:8766`):

   ```powershell
   python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-7-worldspace-ui.yaml
   ```

   See [`docs/live-verification.md`](live-verification.md) for OmniRoute vision env and install deps.

### Verify

| Check | Expected | Failure mode if broken |
|---|---|---|
| Nameplates / HP bars anchor in 3D | Widgets track actor position as camera moves | Screen-locked overlays only → `WorldspaceUI` didn't apply, or `WorldUIRenderer` not created |
| 3D text labels when enabled | `WorldspaceLabel3D` shows TMP world text | Flat sprite text only → label path not wired |
| Selection ring on selected unit | Torus ring at actor feet, scrolls when animated | No ring → `SelectionRing` not hooked to `SelectedUnit` |
| Distance fade | Plates thin out beyond ~30 world units | Pop-in at all distances → fade constants wrong |
| Damage popups on hit (optional) | Rise-and-fade numbers when combat occurs | Missing is OK for gate if nameplates work |
| Telemetry shows render work | Bridge `drawCalls > 0`, `frameMs` stable after settle | UI-only hitch → TMP/font atlas first use |
| No shader error banner | Clean UI, no red compile overlay | Console: `[WorldSphereMod3D]` + `NameplateSoft` / `SelectionRing` shader errors |

## Multi-world session check (optional)

`WorldUIRenderer.OnWorldUnload` should detach plates, but a second world without restart may leave orphaned canvases. Workaround: restart WorldBox between worlds during testing.

## Capture screenshots

Drop comparison shots into `docs/screenshots/`:

- `phase-7-before.png` — `WorldspaceUI = false`, `WorldspaceLabel3D = false`, same scene.
- `phase-7-after.png` — both ON, same scene + camera angle.
- `phase-7-nameplates.png` — nameplate / HP bar / selection ring closeup (matches PlayCUA artifact `phase-7-worldspace-ui/nameplates.png`).

Link them in the Phase 7 PR body when marking ready for review. Diff optional against `docs/journeys/phase-previews/` per [`docs/live-verification.md`](live-verification.md#ssim-optional-gate).

## What's expected to look bad

- **TMP dependency.** Missing TextMesh Pro in mod context may noop labels — see `docs/phase7-architecture.md`.
- **Crowded battles.** Many simultaneous plates overlap; readability is a follow-up, not a gate blocker.
- **Selection without combat.** HP bar may read full until damage events fire.

## If something is broken

- Check the WorldBox console (default toggle: backtick) for `[WorldSphereMod3D]` / `[Worldspace]` errors.
- Check `<WorldBox>/output_log.txt` or NeoModLoader's compile-error log.
- Toggle **Worldspace UI** and **Worldspace Label 3D** OFF — vanilla UI returns without restart.
- Re-run the PlayCUA scenario or `pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-7-worldspace-ui` (mock) for manifest drift.
- Open an issue on the PR with console excerpt + which check from this list failed.
