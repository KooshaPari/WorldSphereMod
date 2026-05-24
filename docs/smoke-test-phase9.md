# Phase 9 in-game smoke test — checklist

What to verify when you toggle `PostFX = true` and `ParticleEffects = true` for the first time. Phase 9 now uses the unified `WSM3DPostStack` BRP chain, so the smoke pass should show a single deterministic SSAO -> SSGI -> Bloom -> ACES -> LUT path instead of separate post-effect MonoBehaviours.

**Agentic automation:** [`Tools/wsm3d-playcua/sample-scenarios/phase-9-postfx-particles.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-9-postfx-particles.yaml) drives bridge `toggle_flag`, telemetry, and vision screenshot steps. Full programmatic + agentic gate order: [`docs/live-verification.md`](live-verification.md).

## Setup

```powershell
./Tools/install.ps1
```

That builds the mod (sanity check) and copies sources + assets to
`<WorldBox>/Mods/WorldSphereMod3D/`. Default WorldBox path is the standard Steam install.

Launch WorldBox. NeoModLoader compiles `Code/*.cs` at startup. If you see a red mod icon, the compute-shader/instancing hardware gate failed — Phase 10 will add a billboard fallback for that case; nothing to do here.

Load or generate a world with **combat or spell effects** (battle, magic units). Save slot 2 is the baseline used by the PlayCUA scenario.

## Regression checks (with `PostFX = false` and `ParticleEffects = false`)

Keep Phases 1–8 as needed so the scene is fully lit. Only Phase 9 FX stack is under test here.

Open the map. Confirm:

- Scene lacks **bloom halos** and heavy color grading (rawer contrast).
- Combat hits do not spawn **particle bursts** from the WSM3D pool (vanilla FX may still appear).
- Settings tab → WorldSphere → **Post FX** and **Particle Effects** toggles are present and OFF (or flip OFF if your save inherited default-on).

If any of those fail, Phase 0–8 plumbing has regressed. Don't proceed.

## Enable PostFX + particle effects

1. Settings → WorldSphere → toggle **Post FX** ON.
2. Toggle **Particle Effects** ON (Phase 9b; bundled in the same smoke pass).
3. Trigger combat or spells; pan camera across bright sky and effect-heavy areas.
4. Optional bridge run (game + listener on `127.0.0.1:8766`):

   ```powershell
   python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-9-postfx-particles.yaml
   ```

   See [`docs/live-verification.md`](live-verification.md) for OmniRoute vision env and install deps.

### Verify

| Check | Expected | Failure mode if broken |
|---|---|---|
| Post-processing visible | Deterministic SSAO -> SSGI -> Bloom -> ACES -> LUT chain; bright areas bloom, highlights roll off filimically, and LUT grading still applies | Identical to OFF -> `PostFX` did not attach `WSM3DPostStack` or `BrpBloom.shader` / `BrpACES.shader` failed to load |
| SSAO when `SSAOEnabled` on | Contact shading in crevices (subtle) | No AO → `ScreenSpaceAO` gated off or pipeline missing |
| Bloom when `BloomEnabled` on | Soft halos and composite around bright emissive areas; default OFF for Phase 9 | No bloom → `BloomEnabled` false or `BrpBloom.shader` missing |
| ACES when `ACESTonemapping` on | Filmic shoulder on bright sky / lights; default ON for Phase 9 | Harsh clipping or linear-looking highlights → `ACESTonemapping` false or `BrpACES.shader` missing |
| Particle bursts on supported IDs | Pooled bursts on combat/effect hooks (5 IDs) | No particles → `ParticleEffects` false or effect ID unmapped |
| `frameMs` acceptable on iGPU | Compare ON vs OFF if hitch | >50 ms sustained → disable `PostFX` for A/B |
| Telemetry shows render work | Bridge `drawCalls > 0` after effects | `drawCalls=0` with scene visible → unrelated to FX gate |
| No shader error banner | Clean UI, no red compile overlay | Console: `[WorldSphereMod3D]` + `WSM3DPostStack` errors |

## SSAO / SSGI knobs (optional)

`SSAOEnabled` defaults **ON** with PostFX; `SSGIEnabled` defaults **OFF**; `BloomEnabled` defaults **OFF**; `ACESTonemapping` defaults **ON**. Toggle for A/B on integrated GPUs — not required to clear Phase 9.

## Multi-world session check (optional)

`WSM3DPostStack` materials/temp render textures and particle pools must release across reload. Workaround for investigation only: restart WorldBox between worlds during testing.

## Capture screenshots

Drop comparison shots into `docs/screenshots/`:

- `phase-9-before.png` — `PostFX = false`, `ParticleEffects = false`, same scene.
- `phase-9-after.png` — both ON, same scene + camera angle.
- `phase-9-effects.png` — bloom / particles closeup (matches PlayCUA artifact `phase-9-postfx-particles/effects.png`).

Link them in the Phase 9 PR body when marking ready for review. Diff optional against `docs/journeys/phase-previews/` per [`docs/live-verification.md`](live-verification.md#ssim-optional-gate).

## What's expected to look bad

- **Built-in post stack.** `WSM3DPostStack` in `WorldSphereMod/Code/PostFx/WSM3DPostStack.cs` is the primary runtime path. The reflective `PostFxController` URP volume path is retained for URP-capable builds, but is not required for current WorldBox smoke passes.
- **Limited effect IDs.** Only five burst IDs wired; exotic spells may show vanilla FX only.
- **iGPU cost.** Bloom pyramid ~2–3 ms at 1080p; acceptable to leave `PostFX` off on weak hardware.

## If something is broken

- Check the WorldBox console (default toggle: backtick) for `[WorldSphereMod3D]` / `[WSM3DPostStack]` errors.
- Check `<WorldBox>/output_log.txt` or NeoModLoader's compile-error log.
- Toggle **Post FX** and **Particle Effects** OFF — unprocessed scene returns without restart.
- Re-run the PlayCUA scenario or `pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-9-postfx` (mock) for manifest drift.
- Open an issue on the PR with console excerpt + which check from this list failed.
