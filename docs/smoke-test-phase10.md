# Phase 10 in-game smoke test — checklist

What to verify when you apply `LODScale = 0.5` (factory default) and confirm the LOD / impostor ladder.

**Agentic automation:** [`Tools/wsm3d-playcua/sample-scenarios/phase-10-lod.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-10-lod.yaml) drives bridge `set_setting` for `LODScale`, telemetry, and vision screenshot steps. Full programmatic + agentic gate order: [`docs/live-verification.md`](live-verification.md).

## Setup

```powershell
./Tools/install.ps1
```

That builds the mod (sanity check) and copies sources + assets to
`<WorldBox>/Mods/WorldSphereMod3D/`. Default WorldBox path is the standard Steam install.

Launch WorldBox. NeoModLoader compiles `Code/*.cs` at startup. If you see a red mod icon, the compute-shader/instancing hardware gate failed — this phase adds the **billboard / impostor fallback** for that gate; if you hit the red icon, document hardware tier and skip in-world LOD ladder.

Load or generate a world with **many distant actors** (large village, army on plains). Save slot 2 is the baseline used by the PlayCUA scenario.

## Regression checks (with `LODScale = 1.0` or higher)

Keep Phases 1–9 as needed so voxel actors populate the map. Only Phase 10 LOD tuning is under test here.

Open the map. Confirm:

- Near and far actors both use **full voxel meshes** (no simplified proxy cards at distance).
- Settings tab → WorldSphere → **LOD Scale** slider is present (default `0.5` in fresh saves).
- Zooming out does not swap distant units to impostor billboards aggressively.

If any of those fail, Phase 0–9 plumbing has regressed. Don't proceed.

## Apply default LOD scale (0.5)

1. Settings → WorldSphere → set **LOD Scale** to **0.5** (factory default in `SavedSettings.cs`).
2. Pan camera: close on a unit, then pull back until armies fill the horizon — watch tier transitions.
3. Optional bridge run (game + listener on `127.0.0.1:8766`):

   ```powershell
   python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-10-lod.yaml
   ```

   See [`docs/live-verification.md`](live-verification.md) for OmniRoute vision env and install deps.

   Or via CLI (game running):

   ```powershell
   pwsh Tools/wsm3d.ps1 settings set -Key LODScale -Value 0.5
   ```

### Verify

| Check | Expected | Failure mode if broken |
|---|---|---|
| Near actors stay full voxel | Close units retain limb detail | All proxies → thresholds inverted or `LODScale` misread |
| Distant actors simplify | Proxy cards or impostor billboards at far bands | All full voxel at horizon → `LodSelector` not running |
| Hysteresis stable | Tier doesn't flicker when panning slowly | Pop every frame → hysteresis dict not applied |
| `LODScale` live update | Slider/bridge change recomputes cutoffs immediately | Stale tiers until reload → `LodSelector` cache |
| `frameMs` improves vs `LODScale=1` on large maps | Fewer distant voxel draws | No win → impostor path disabled; check `ImpostorOnlyMode` |
| Telemetry shows render work | Bridge `drawCalls > 0`, `instances > 10` | `instances` collapse → LOD culled everything |
| No shader error banner | Clean UI, no red compile overlay | Console: `[WorldSphereMod3D]` + impostor shader errors |

## Impostor-only hardware fallback (optional)

On GPUs that fail the compute/instancing gate, Phase 10 may force `ImpostorOnlyMode`. Confirm the world still renders billboards instead of a black screen — see `docs/phase10-architecture.md`.

## Multi-world session check (optional)

`LodSelector` per-entity hysteresis resets on world unload; second world without restart may show wrong tiers until camera moves. Workaround: restart WorldBox between worlds during testing.

## Capture screenshots

Drop comparison shots into `docs/screenshots/`:

- `phase-10-before.png` — `LODScale = 1.0` (or max), same scene.
- `phase-10-after.png` — `LODScale = 0.5`, same scene + camera angle.
- `phase-10-lod-ladder.png` — near full voxel + distant proxies in one frame (matches PlayCUA artifact `phase-10-lod/lod-ladder.png`).

Link them in the Phase 10 PR body when marking ready for review. Diff optional against `docs/journeys/phase-previews/` per [`docs/live-verification.md`](live-verification.md#ssim-optional-gate).

## What's expected to look bad

- **Pop-in at tier boundaries.** Screen-fraction thresholds (0.08 / 0.025) trade quality for stability — see `docs/phase10-architecture.md`.
- **Impostor color drift.** Billboards may not match per-actor tint until atlas refresh lands.
- **LODScale ≠ entity scale.** `_entityHeight` is pre-multiplied for `VoxelScaleMultiplier=16`; extreme slider values need judgment.

## If something is broken

- Check the WorldBox console (default toggle: backtick) for `[WorldSphereMod3D]` / `[LodSelector]` errors.
- Check `<WorldBox>/output_log.txt` or NeoModLoader's compile-error log.
- Set **LOD Scale** back to `1.0` — full voxel at all distances returns without restart.
- Re-run the PlayCUA scenario or `pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-10-lod-impostor` (mock) for manifest drift.
- Open an issue on the PR with console excerpt + which check from this list failed.
