# PlayCUA sample scenarios

Thirteen YAML scenarios drive the agentic live gate against a running WorldBox instance with WorldSphereMod3D installed. Each file is executed by `Tools/wsm3d-playcua/main.py` and asserts bridge health, phase toggles, telemetry, and (where configured) OmniRoute vision on screenshots.

For the release/handoff evidence bundle — live verifier command, report JSON, PlayCUA artifacts, SSIM fixtures, and skip/offline notes — use the canonical checklist in [`docs/live-verification.md`](../../../docs/live-verification.md#canonical-live-proof-bundle).

## Prerequisites (all scenarios)

| Requirement | Details |
|-------------|---------|
| **WorldBox** | Installed, launched, mod enabled |
| **BridgeRPC** | Healthy on `127.0.0.1:8766` |
| **OmniRoute** | Required for vision steps on phase scenarios (and optional on `bridge-health-vision`); set `OMNROUTE_*` per [`docs/live-verification.md`](../../../docs/live-verification.md#omniroute-environment) |

Install PlayCUA deps once: `pip install -r Tools/wsm3d-playcua/requirements.txt`

## Scenarios (13)

| # | File | Name | Phase | Vision |
|---|------|------|-------|--------|
| 1 | [`bridge-health-vision.yaml`](bridge-health-vision.yaml) | `bridge-health-vision` | Bridge | Optional |
| 2 | [`bridge-save-load-smoke.yaml`](bridge-save-load-smoke.yaml) | `bridge-save-load-smoke` | Bridge | — |
| 3 | [`phase-1-voxel-actors.yaml`](phase-1-voxel-actors.yaml) | `phase-1-voxel-actors` | 1 — Voxel actors (`VoxelEntities`) | Required |
| 4 | [`phase-2-procedural-buildings.yaml`](phase-2-procedural-buildings.yaml) | `phase-2-procedural-buildings` | 2 — Procedural buildings | Required |
| 5 | [`phase-3-crossed-quad-foliage.yaml`](phase-3-crossed-quad-foliage.yaml) | `phase-3-crossed-quad-foliage` | 3 — Crossed-quad foliage | Required |
| 6 | [`phase-3b-cloud-crossed-quad.yaml`](phase-3b-cloud-crossed-quad.yaml) | `phase-3b-cloud-crossed-quad` | 3b — Cloud crossed-quad | Required |
| 7 | [`phase-4-mesh-water.yaml`](phase-4-mesh-water.yaml) | `phase-4-mesh-water` | 4 — Mesh water | Required |
| 8 | [`phase-5-high-shadows.yaml`](phase-5-high-shadows.yaml) | `phase-5-high-shadows` | 5 — High shadows + HDR skybox | Required |
| 9 | [`phase-6-skeletal-animation.yaml`](phase-6-skeletal-animation.yaml) | `phase-6-skeletal-animation` | 6 — Skeletal animation | Required |
| 10 | [`phase-7-worldspace-ui.yaml`](phase-7-worldspace-ui.yaml) | `phase-7-worldspace-ui` | 7 — Worldspace UI + 3D labels | Required |
| 11 | [`phase-8-day-night.yaml`](phase-8-day-night.yaml) | `phase-8-day-night` | 8 — Day/night cycle | Required |
| 12 | [`phase-9-postfx-particles.yaml`](phase-9-postfx-particles.yaml) | `phase-9-postfx-particles` | 9 — PostFX + particles | Required |
| 13 | [`phase-10-lod.yaml`](phase-10-lod.yaml) | `phase-10-lod` | 10 — LOD / impostor scale | Required |

(`smoke-test.sh` is a helper script, not counted in the 13.)

## How to run

### Run all scenarios

```powershell
pwsh Tools/wsm3d.ps1 playcua run-all
```

Optional OmniRoute vision backend:

```powershell
pwsh Tools/wsm3d.ps1 playcua run-all -VisionBackend omniroute
```

Artifacts: `Tools/wsm3d-playcua/.reports/run-all-artifacts/`

### List scenarios (live verifier)

```powershell
pwsh Tools/wsm-live-verify.ps1 -ListScenarios
```

Prints each YAML filename and its `name:` field from the scenario manifest.

### Single scenario

```powershell
python Tools/wsm3d-playcua/main.py Tools/wsm3d-playcua/sample-scenarios/phase-1-voxel-actors.yaml
```

### Full live proof bundle

```powershell
pwsh Tools/wsm-live-verify.ps1 -Live -Vision
```

Runs all sample scenarios plus SSIM against phase previews; see [`docs/live-verification.md`](../../../docs/live-verification.md#canonical-live-proof-bundle).

## Related docs

- Agentic gate overview: [`docs/live-verification.md`](../../../docs/live-verification.md)
- Per-phase in-game checklists: [`docs/smoke-test-index.md`](../../../docs/smoke-test-index.md)
- E2E invariants: `tests/WorldSphereMod.Tests.E2E/PlaycuaSampleScenarioInvariantsTests.cs`
