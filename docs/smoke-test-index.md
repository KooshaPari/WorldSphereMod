# Smoke test index (Phases 1–10)

Single entry point for in-game smoke checklists, PlayCUA scenarios, journey manifest IDs, and screenshot capture commands. Use this page when shipping or re-validating a phase gate.

**Cold start:** [Handoff](./HANDOFF) · **Gate order:** [Live verification](./live-verification) · **CLI reference:** [Tooling](./tooling)

| Gate | Scope | Command |
|------|-------|---------|
| **Offline gate** | **478 tests** (475 passed, 3 skipped) + journey mock (20 manifests) | `pwsh Tools/wsm-live-verify.ps1` |

---

## Quick commands

Install and launch (once per session):

```powershell
./Tools/install.ps1
pwsh Tools/wsm3d.ps1 launch
```

Bridge smoke (no phase toggle):

```powershell
python Tools/wsm3d-playcua/smoke.py
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/bridge-health-vision.yaml
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/bridge-save-load-smoke.yaml
```

Journey mock verify (CI-friendly, no game):

```powershell
pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase<N>
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-<slug>
```

Screenshot helper (game focused, writes `docs/screenshots/phase-<n>-<name>.png`):

```powershell
pwsh Tools/wsm3d.ps1 screenshot phase <n> -Name <slug> -WindowOnly
```

---

## Phase matrix

| Phase | Checklist | PlayCUA scenario | Smoke manifest | User-journey manifest | Screenshot slugs → `docs/screenshots/` |
|------:|-----------|------------------|----------------|----------------------|----------------------------------------|
| 1 | [smoke-test-phase1](./smoke-test-phase1) | [`phase-1-voxel-actors.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-1-voxel-actors.yaml) | [`smoke-test-phase1`](./journeys/manifests/smoke-test-phase1/manifest.json) | [`us-wsm-phase-1-voxel-actors`](./journeys/manifests/us-wsm-phase-1-voxel-actors/manifest.json) | `before`, `after`, `buildings` |
| 2 | [smoke-test-phase2](./smoke-test-phase2) | [`phase-2-procedural-buildings.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-2-procedural-buildings.yaml) | [`smoke-test-phase2`](./journeys/manifests/smoke-test-phase2/manifest.json) | [`us-wsm-phase-2-mesh-buildings`](./journeys/manifests/us-wsm-phase-2-mesh-buildings/manifest.json) | `before`, `after`, `buildings` |
| 3 | [smoke-test-phase3](./smoke-test-phase3) | [`phase-3-crossed-quad-foliage.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-3-crossed-quad-foliage.yaml) · [3b cloud](../Tools/wsm3d-playcua/sample-scenarios/phase-3b-cloud-crossed-quad.yaml) | [`smoke-test-phase3`](./journeys/manifests/smoke-test-phase3/manifest.json) | [`us-wsm-phase-3-crossed-foliage`](./journeys/manifests/us-wsm-phase-3-crossed-foliage/manifest.json) | `before`, `after`, `foliage` |
| 4 | [smoke-test-phase4](./smoke-test-phase4) | [`phase-4-mesh-water.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-4-mesh-water.yaml) | [`smoke-test-phase4`](./journeys/manifests/smoke-test-phase4/manifest.json) | [`us-wsm-phase-4-mesh-water`](./journeys/manifests/us-wsm-phase-4-mesh-water/manifest.json) | `before`, `after`, `water` |
| 5 | [smoke-test-phase5](./smoke-test-phase5) | [`phase-5-high-shadows.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-5-high-shadows.yaml) | [`smoke-test-phase5`](./journeys/manifests/smoke-test-phase5/manifest.json) | [`us-wsm-phase-5-shadows`](./journeys/manifests/us-wsm-phase-5-shadows/manifest.json) | `before`, `after`, `shadows-sky` |
| 6 | [smoke-test-phase6](./smoke-test-phase6) | [`phase-6-skeletal-animation.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-6-skeletal-animation.yaml) | [`smoke-test-phase6`](./journeys/manifests/smoke-test-phase6/manifest.json) | [`us-wsm-phase-6-skeletal`](./journeys/manifests/us-wsm-phase-6-skeletal/manifest.json) | `before`, `after`, `actors-rig` (manifest slug `skeletal`) |
| 7 | [smoke-test-phase7](./smoke-test-phase7) | [`phase-7-worldspace-ui.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-7-worldspace-ui.yaml) | [`smoke-test-phase7`](./journeys/manifests/smoke-test-phase7/manifest.json) | [`us-wsm-phase-7-worldspace-ui`](./journeys/manifests/us-wsm-phase-7-worldspace-ui/manifest.json) | `before`, `after`, `nameplates` (manifest slug `ui`) |
| 8 | [smoke-test-phase8](./smoke-test-phase8) | [`phase-8-day-night.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-8-day-night.yaml) | [`smoke-test-phase8`](./journeys/manifests/smoke-test-phase8/manifest.json) | [`us-wsm-phase-8-day-night`](./journeys/manifests/us-wsm-phase-8-day-night/manifest.json) | `before`, `after`, `sky-cycle` (manifest slug `day-night`) |
| 9 | [smoke-test-phase9](./smoke-test-phase9) | [`phase-9-postfx-particles.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-9-postfx-particles.yaml) | [`smoke-test-phase9`](./journeys/manifests/smoke-test-phase9/manifest.json) | [`us-wsm-phase-9-postfx`](./journeys/manifests/us-wsm-phase-9-postfx/manifest.json) | `before`, `after`, `effects` (manifest slug `postfx`) |
| 10 | [smoke-test-phase10](./smoke-test-phase10) | [`phase-10-lod.yaml`](../Tools/wsm3d-playcua/sample-scenarios/phase-10-lod.yaml) | [`smoke-test-phase10`](./journeys/manifests/smoke-test-phase10/manifest.json) | [`us-wsm-phase-10-lod-impostor`](./journeys/manifests/us-wsm-phase-10-lod-impostor/manifest.json) | `before`, `after`, `lod-ladder` (manifest slug `lod`) |

Manifest JSON lives under [`docs/journeys/manifests/`](./journeys/manifests/) (catalog: [`index.json`](./journeys/manifests/index.json)).

---

## Per-phase commands

Replace `<n>` and paths as needed. Run PlayCUA with WorldBox + bridge listener on `127.0.0.1:8766`.

### Phase 1 — VoxelEntities

```powershell
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-1-voxel-actors.yaml
pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase1
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-1-voxel-actors
pwsh Tools/wsm3d.ps1 screenshot phase 1 -Name before -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 1 -Name after -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 1 -Name buildings -WindowOnly
```

Outputs: `phase-1-before.png`, `phase-1-after.png`, `phase-1-buildings.png`

### Phase 2 — ProceduralBuildings

```powershell
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-2-procedural-buildings.yaml
pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase2
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-2-mesh-buildings
pwsh Tools/wsm3d.ps1 screenshot phase 2 -Name before -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 2 -Name after -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 2 -Name buildings -WindowOnly
```

Outputs: `phase-2-before.png`, `phase-2-after.png`, `phase-2-buildings.png`

### Phase 3 — CrossedQuadFoliage

```powershell
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-3-crossed-quad-foliage.yaml
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-3b-cloud-crossed-quad.yaml
pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase3
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-3-crossed-foliage
pwsh Tools/wsm3d.ps1 screenshot phase 3 -Name before -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 3 -Name after -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 3 -Name foliage -WindowOnly
```

Outputs: `phase-3-before.png`, `phase-3-after.png`, `phase-3-foliage.png`

### Phase 4 — MeshWater

```powershell
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-4-mesh-water.yaml
pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase4
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-4-mesh-water
pwsh Tools/wsm3d.ps1 screenshot phase 4 -Name before -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 4 -Name after -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 4 -Name water -WindowOnly
```

Outputs: `phase-4-before.png`, `phase-4-after.png`, `phase-4-water.png`

### Phase 5 — HighShadows + HdrSkybox

```powershell
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-5-high-shadows.yaml
pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase5
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-5-shadows
pwsh Tools/wsm3d.ps1 screenshot phase 5 -Name before -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 5 -Name after -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 5 -Name shadows-sky -WindowOnly
```

Outputs: `phase-5-before.png`, `phase-5-after.png`, `phase-5-shadows-sky.png`

### Phase 6 — SkeletalAnimation

```powershell
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-6-skeletal-animation.yaml
pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase6
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-6-skeletal
pwsh Tools/wsm3d.ps1 screenshot phase 6 -Name before -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 6 -Name after -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 6 -Name actors-rig -WindowOnly
```

Outputs: `phase-6-before.png`, `phase-6-after.png`, `phase-6-actors-rig.png` (journey manifest slug `skeletal`)

### Phase 7 — WorldspaceUI

```powershell
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-7-worldspace-ui.yaml
pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase7
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-7-worldspace-ui
pwsh Tools/wsm3d.ps1 screenshot phase 7 -Name before -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 7 -Name after -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 7 -Name nameplates -WindowOnly
```

Outputs: `phase-7-before.png`, `phase-7-after.png`, `phase-7-nameplates.png` (journey manifest slug `ui`)

### Phase 8 — DayNightCycle

```powershell
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-8-day-night.yaml
pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase8
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-8-day-night
pwsh Tools/wsm3d.ps1 screenshot phase 8 -Name before -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 8 -Name after -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 8 -Name sky-cycle -WindowOnly
```

Outputs: `phase-8-before.png`, `phase-8-after.png`, `phase-8-sky-cycle.png` (journey manifest slug `day-night`)

### Phase 9 — PostFX + particles

```powershell
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-9-postfx-particles.yaml
pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase9
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-9-postfx
pwsh Tools/wsm3d.ps1 screenshot phase 9 -Name before -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 9 -Name after -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 9 -Name effects -WindowOnly
```

Outputs: `phase-9-before.png`, `phase-9-after.png`, `phase-9-effects.png` (journey manifest slug `postfx`)

### Phase 10 — LOD / impostor

```powershell
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-10-lod.yaml
pwsh Tools/wsm3d.ps1 journey verify -Id smoke-test-phase10
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-10-lod-impostor
pwsh Tools/wsm3d.ps1 screenshot phase 10 -Name before -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 10 -Name after -WindowOnly
pwsh Tools/wsm3d.ps1 screenshot phase 10 -Name lod-ladder -WindowOnly
```

Outputs: `phase-10-before.png`, `phase-10-after.png`, `phase-10-lod-ladder.png` (journey manifest slug `lod`)

---

## Related docs

| Doc | Purpose |
|-----|---------|
| [HANDOFF](./HANDOFF) | Cold-start orientation and branch/PR pointers |
| [live-verification](./live-verification) | Programmatic vs agentic gate order, OmniRoute vision env |
| [phase1-review](./phase1-review) | Phase 1 fixes already applied |
| [journeys/recording-runbook](./journeys/recording-runbook) | Capture discipline for journey frames |
| [tooling](./tooling) | Full `wsm3d` CLI surface |
