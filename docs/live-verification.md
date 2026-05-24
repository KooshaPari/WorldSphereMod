# Live verification

WorldSphereMod3D uses two complementary gates before treating a change as visually and operationally proven: a **programmatic gate** (fast, CI-friendly) and an **agentic gate** (desktop + game required). This page is the single entry point for both.

**Related docs**

- Visual SSIM contract: [`docs/journeys/scratch/visual-regression-harness-design.md`](journeys/scratch/visual-regression-harness-design.md)
- Bridge save/load live checklist: [`docs/journeys/scratch/bridge-scene-transition-known-issue.md`](journeys/scratch/bridge-scene-transition-known-issue.md) (section **Live verification checklist**)
- Journey capture discipline: [`docs/journeys/recording-runbook.md`](journeys/recording-runbook.md)

---

## Canonical live proof bundle

Use this checklist when you need a single release or handoff bundle that proves the mod actually ran in WorldBox. It is the authoritative list for agentic proof, and it ties the runtime checks back to the artifacts reviewers need.

### Prerequisites

- WorldBox is installed and launched.
- The mod is installed and enabled.
- BridgeRPC is healthy on `127.0.0.1:8766`.

### Required proof steps

1. Run the live verifier:
   - `pwsh Tools/wsm-live-verify.ps1 -Live`
   - Add `-Vision` when you want PlayCUA screenshot checks to use the vision backend.
2. Capture PlayCUA scenario output under `Tools/wsm3d-playcua/.reports/live-verify-artifacts/`.
3. Collect `Tools/.reports/live-verify-latest.json`.
4. Keep the phase-preview SSIM fixtures that were used for comparison under `docs/journeys/phase-previews/*/after.png`.
5. Attach the scenario summary, screenshots/captures, and SSIM result to the release or handoff evidence bundle.
6. If `live-playcua-ssim` is skipped or unavailable, say so explicitly in the bundle and note that the run is offline or otherwise blocked.

### Evidence to attach

- Commands run, including the exact `pwsh Tools/wsm-live-verify.ps1` invocation and any `-Live` / `-Vision` flags.
- `Tools/.reports/live-verify-latest.json`.
- PlayCUA screenshots or capture artifacts from `Tools/wsm3d-playcua/.reports/live-verify-artifacts/`.
- Scenario summary from the PlayCUA run.
- SSIM result for the selected phase-preview fixtures.
- An explicit skip/block note when `live-playcua-ssim` did not run.

### Minimum bundle format

Use a short note with these fields in order:

- environment readiness
- live verify command
- PlayCUA artifact path
- report JSON path
- screenshot or capture summary
- SSIM pass/fail or skipped/offline note

This bundle is the release/handoff proof set. If any item is missing, the proof is incomplete even if the offline gates passed.

---

## Programmatic gate

Runs without WorldBox when possible. Use this on every PR and before opening a live session.

| Layer | What it proves | Command / workflow |
|-------|----------------|-------------------|
| **dotnet test** | API surface, install/manifest contracts, bridge source invariants, harness preflight | `task test-all` or `dotnet test tests/WorldSphereMod.Tests.{Unit,Integration,E2E}/` |
| **Journey mock** | Manifest JSON, step slugs, canonical preview PNGs exist; `phenotype-journey` schema/OCR preflight (not pixel truth) | `pwsh Tools/wsm3d.ps1 journey verify -Id <phase-id>` (default mock) or `phenotype-journey verify <manifest> --mock` |
| **SSIM (optional)** | Captured frames match canonical `docs/journeys/phase-previews/<phase>/before.png` and `after.png` | Local only today — see [SSIM optional gate](#ssim-optional-gate). In `Tools/wsm-live-verify.ps1 -Live`, any selected phase preview directory missing `after.png` fails the live SSIM stage. |

Mock journey verification is **not** a substitute for screenshots of the running game; it catches manifest drift and missing assets early.

### SSIM optional gate

`Tools/wsm-live-verify.ps1` orchestrates the programmatic + optional live pipeline (dotnet test → journey mock → bridge/playcua/SSIM) and writes `Tools/.reports/live-verify-latest.json`. For pairwise PNG checks it shells out to `Tools/wsm-ssim-compare.py`, which prints JSON with `"ok"`, `"ssim"`, and `"threshold"` (default **0.95**).

When `Tools/wsm3d-capture` (or `wsm3d screenshot`) has fresh captures, compare them to the canonical phase previews using the harness in [`visual-regression-harness-design.md`](journeys/scratch/visual-regression-harness-design.md):

- **Metric:** SSIM (structural similarity)
- **Pass threshold:** `>= 0.95` per compared pair (`before`→`before.png`, `after`→`after.png`)
- **On failure:** emit diff PNG, stats JSON, and the captured frame for triage
- **Live mode requirement:** if a phase preview directory is selected for SSIM, `after.png` must exist; missing required fixtures fail the live stage instead of being reported as skipped.

The pixel-diff backend is not wired in CI yet (integration tests only assert path/checklist contracts). Treat SSIM as an optional local hardening step until the harness lands in a workflow.

---

## Agentic gate

Requires a Windows desktop with WorldBox, the mod installed, and the BridgeRPC listener up (`127.0.0.1:8766` by default).

| Layer | What it proves | Command / workflow |
|-------|----------------|-------------------|
| **wsm3d-playcua** | YAML scenarios drive bridge actions (`health`, `load_save`, `toggle_flag`, `assert_telemetry`, `screenshot`) | [`Tools/wsm3d-playcua/sample-scenarios/README.md`](../Tools/wsm3d-playcua/sample-scenarios/README.md) — all 13 scenarios; `python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/bridge-health-vision.yaml` |
| **OmniRoute vision combo** | Screenshot steps with `vision:` criteria get multi-VLM judgment via OpenAI-compatible `/v1/chat/completions` | Set `OMNROUTE_*` env (below); backend: `Tools/wsm3d-playcua/vision.py` → `OmniRouteVisionValidator` |
| **Bridge smoke** | JSON invariants on `/health`, `/telemetry`, `/voxel/sprite`, `/phase/*` | `python Tools/wsm3d-playcua/smoke.py` |
| **Journey live** | Manifest frames + OCR against real captures | `pwsh Tools/wsm3d.ps1 journey verify -Id <id> -Live` |

Install PlayCUA deps once:

```powershell
pip install -r Tools/wsm3d-playcua/requirements.txt
```

Typical agentic session:

```powershell
$env:WORLDBOX_PATH = "C:/Program Files (x86)/Steam/steamapps/common/worldbox"
pwsh Tools/wsm3d.ps1 build
pwsh Tools/wsm3d.ps1 install
pwsh Tools/wsm3d.ps1 launch
Start-Sleep -Seconds 20
python Tools/wsm3d-playcua/smoke.py
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/bridge-health-vision.yaml
```

For phase toggles and vision assertions after the world is in 3D, use:

- Phase 1 voxel actors: `Tools/wsm3d-playcua/sample-scenarios/phase-1-voxel-actors.yaml`
- Phase 2 procedural buildings: `Tools/wsm3d-playcua/sample-scenarios/phase-2-procedural-buildings.yaml` — in-game checklist: [`docs/smoke-test-phase2.md`](smoke-test-phase2.md)
- Phase 3 crossed-quad foliage: `Tools/wsm3d-playcua/sample-scenarios/phase-3-crossed-quad-foliage.yaml` — in-game checklist: [`docs/smoke-test-phase3.md`](smoke-test-phase3.md)
- Phase 3b cloud crossed-quad (`CrossedQuadFoliage` + `fx_cloud`): `Tools/wsm3d-playcua/sample-scenarios/phase-3b-cloud-crossed-quad.yaml`
- Phase 4 mesh water: `Tools/wsm3d-playcua/sample-scenarios/phase-4-mesh-water.yaml` — in-game checklist: [`docs/smoke-test-phase4.md`](smoke-test-phase4.md)
- Phase 5 high shadows + HDR skybox: `Tools/wsm3d-playcua/sample-scenarios/phase-5-high-shadows.yaml` — in-game checklist: [`docs/smoke-test-phase5.md`](smoke-test-phase5.md)
- Phase 6 skeletal animation: `Tools/wsm3d-playcua/sample-scenarios/phase-6-skeletal-animation.yaml` — in-game checklist: [`docs/smoke-test-phase6.md`](smoke-test-phase6.md)
- Phase 7 worldspace UI + 3D labels: `Tools/wsm3d-playcua/sample-scenarios/phase-7-worldspace-ui.yaml` — in-game checklist: [`docs/smoke-test-phase7.md`](smoke-test-phase7.md)
- Phase 8 day/night cycle: `Tools/wsm3d-playcua/sample-scenarios/phase-8-day-night.yaml` — in-game checklist: [`docs/smoke-test-phase8.md`](smoke-test-phase8.md)
- Phase 9 PostFX + particles: `Tools/wsm3d-playcua/sample-scenarios/phase-9-postfx-particles.yaml` — in-game checklist: [`docs/smoke-test-phase9.md`](smoke-test-phase9.md)
- Phase 10 LOD scale: `Tools/wsm3d-playcua/sample-scenarios/phase-10-lod.yaml` — in-game checklist: [`docs/smoke-test-phase10.md`](smoke-test-phase10.md)

Bridge save/load smoke (pre/post `health` + telemetry; optional `load_save`; manual UI save/load notes):

```powershell
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/bridge-save-load-smoke.yaml
```

Maps to the live checklist in [`bridge-scene-transition-known-issue.md`](journeys/scratch/bridge-scene-transition-known-issue.md#live-verification-checklist-required-to-clear-partial). E2E guardrails: `tests/WorldSphereMod.Tests.E2E/PlaycuaSampleScenarioInvariantsTests.cs`.

### OmniRoute environment

Copy [`Tools/omniroute-vision.env.example`](../Tools/omniroute-vision.env.example) to a local env file (for example `Tools/omniroute-vision.env`), set `OMNROUTE_API_KEY` from **Dashboard → Endpoints**, then load it before PlayCUA:

```powershell
Get-Content Tools/omniroute-vision.env | ForEach-Object {
  if ($_ -match '^\s*([^#=]+)=(.*)$') { Set-Item -Path "env:$($matches[1].Trim())" -Value $matches[2].Trim() }
}
```

| Variable | Required | Default | Purpose |
|----------|----------|---------|---------|
| `OMNROUTE_API_KEY` | Yes (for OmniRoute vision) | — | API key from **Dashboard → Endpoints** in the OmniRoute UI |
| `OMNROUTE_BASE_URL` | No | `http://127.0.0.1:20128/v1` | OpenAI-compatible base URL for chat completions |
| `OMNROUTE_VISION_COMBO` | One of combo/model | — | Combo **name** created in OmniRoute (preferred for multi-VLM) |
| `OMNROUTE_VISION_MODEL` | One of combo/model | — | Single model id if not using a combo |

`OmniRouteVisionValidator` sends the screenshot as a base64 `image_url` in the user message and expects JSON: `{"passes": bool, "reason": "...", "confidence": 0.0-1.0}`.

**Anthropic fallback:** `main.py` can use `ANTHROPIC_API_KEY` / `--anthropic-key` with `VisionValidator` when OmniRoute is not configured. Prefer OmniRoute for frontier multi-model routing and quota sharing across providers.

Example (PowerShell) — same values as `Tools/omniroute-vision.env.example`:

```powershell
$env:OMNROUTE_API_KEY = "<from OmniRoute dashboard → Endpoints>"
$env:OMNROUTE_BASE_URL = "http://127.0.0.1:20128/v1"
$env:OMNROUTE_VISION_COMBO = "wsm3d-vision-frontier"
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-1-voxel-actors.yaml
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-2-procedural-buildings.yaml
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-3-crossed-quad-foliage.yaml
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-3b-cloud-crossed-quad.yaml
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-4-mesh-water.yaml
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-5-high-shadows.yaml
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-6-skeletal-animation.yaml
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-7-worldspace-ui.yaml
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-8-day-night.yaml
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-9-postfx-particles.yaml
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-10-lod.yaml
```

---

## Configure an OmniRoute “model combo” for multi-VLM frontier vision

OmniRoute combos chain multiple vision-capable models behind one **model id** you pass to clients. PlayCUA uses that id as the `model` field on `/v1/chat/completions`.

1. **Run OmniRoute** locally (default gateway `http://127.0.0.1:20128`).
2. **Connect providers** — **Dashboard → Providers** — add at least two frontier multimodal providers (e.g. Anthropic Claude 3+, OpenAI GPT-4o, Google Gemini). Ensure each account can accept image input.
3. **Create a combo** — **Dashboard → Combos → Create** (use multiple vision-capable models so OmniRoute can consensus-check or fall back when a provider rate-limits):
   - **Name:** `wsm3d-vision-frontier` (must match `OMNROUTE_VISION_COMBO` in `Tools/omniroute-vision.env.example`).
   - **Strategy:** `priority` for deterministic primary→fallback, or `round-robin` / `weighted` if you want load spread across VLMs.
   - **Steps:** Add only models that advertise vision in OmniRoute’s catalog (`capabilities.vision` / image modalities). Example chain:
     1. `cc/claude-sonnet-4-*` (or your best Claude vision model)
     2. `openai/gpt-4o` (or `gpt-4o-mini` for cost)
     3. `gc/gemini-2.5-flash` (or current Gemini vision preview)
   - Run the combo **readiness check** in the dashboard before relying on it in CI/agents.
4. **Create an API key** — **Dashboard → Endpoints** — copy the key into `OMNROUTE_API_KEY`.
5. **Smoke the combo** outside the game:

   ```powershell
   curl http://127.0.0.1:20128/v1/models -H "Authorization: Bearer $env:OMNROUTE_API_KEY"
   ```

   Confirm your combo name appears and that member models list image input modalities.

6. **Point PlayCUA at the combo** — set `OMNROUTE_VISION_COMBO=wsm3d-vision-frontier` (or copy from `Tools/omniroute-vision.env.example`; do not pass underlying provider model ids).

For “frontier” vision, favor **priority** with the strongest VLM first and a cheaper multimodal model as fallback so screenshot steps stay reliable when the primary provider rate-limits.

---

## CI vs local — what runs where

| Check | CI (`ubuntu-latest`) | Local (Windows + game) |
|-------|----------------------|-------------------------|
| `WorldSphereMod.Tests.Unit` | Yes — `test-gate.yml` | Yes — `task test` / `dotnet test` |
| `WorldSphereMod.Tests.Integration` | Yes — `test-gate.yml` | Yes — `task test-integration` |
| `WorldSphereMod.Tests.E2E` | No in `test-gate.yml` (yes in `nightly.yml`, integration allowed to fail) | Yes — `task test-e2e` |
| `journeys-gate.yml` — JSON + fixture PNGs + `phenotype-journey verify` (mock) | Yes on `docs/journeys/**` changes | Yes — `pwsh Tools/verify-journeys.ps1` or `wsm3d journey verify -Id …` |
| SSIM vs `phase-previews/` | Not yet | Optional — design in `visual-regression-harness-design.md` |
| `wsm3d-playcua` scenarios + OmniRoute vision | No (no game display) | Yes — agentic gate |
| `phenotype-journey verify --live` | No | Yes — after captures exist |
| Bridge save/load checklist | No | Yes — [`bridge-scene-transition-known-issue.md`](journeys/scratch/bridge-scene-transition-known-issue.md) |

**PR merge bar today:** green `build`, `test-gate`, `lint-gate`, `journeys-gate` (when journeys change), and `docs-build-gate` (when docs change). Live/agentic proof remains manual until a Windows runner or self-hosted game host exists.

---

## Bridge scene transition (live checklist)

Bridge hardening after save/load is **PARTIAL** until the live checklist is cleared. Do not skip it when touching `BridgeServer`, `BridgePerFrameTick`, or voxel flush timing.

Full checklist (pre-save-load, save/load transition, listener race, voxel path):

**[Live verification checklist — bridge-scene-transition-known-issue.md](journeys/scratch/bridge-scene-transition-known-issue.md#live-verification-checklist-required-to-clear-partial)**

Quick smoke before the checklist:

```powershell
python Tools/wsm3d-playcua/smoke.py
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/bridge-save-load-smoke.yaml
```

The YAML scenario asserts pre-save-load `health` + telemetry, documents an optional bridge `load_save` or manual in-game save/load, then post-reload `health` + telemetry. Skip automated `load_save` when exercising only the WorldBox UI path (`optional: true` on that step).

---

## Suggested order of operations

1. **Programmatic:** `task test-all` → `pwsh Tools/wsm3d.ps1 journey verify -Id <phase-id>` (mock).
2. **Local SSIM (optional):** capture with `wsm3d-capture` / `wsm3d screenshot`, compare to `docs/journeys/phase-previews/` at SSIM ≥ 0.95.
3. **Agentic:** install/launch → `smoke.py` → playcua scenario with `OMNROUTE_VISION_COMBO` → journey `-Live` if manifests have frames.
4. **Bridge:** complete save/load checklist when bridge or scene-transition code changes.
