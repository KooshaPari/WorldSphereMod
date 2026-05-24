# Phenotype Journey Schema (WorldSphereMod3D)

This document explains the Phenotype journey manifest system, the assertions DSL, and how to author, record, and verify journeys for WorldSphereMod3D phases.

## Overview

A **journey** is a step-by-step verification of a feature or workflow. Each journey:
- Captures a baseline screenshot
- Performs an action (toggle phase, navigate UI)
- Takes a screenshot after the action
- Asserts expected text is visible (via OCR) or expected text is absent
- Can also carry a short recording asset when motion is the clearest proof
  of the state change

All 10 WorldSphereMod3D phases have journeys (phases 1–10: Voxel Actors, Buildings, Foliage, Water, Shadows, Skeletal, WorldspaceUI, DayNight, PostFX, LOD/Impostor).

Phase 0 hardening uses the same journey system to cover task/journey gates,
API capability discovery, the opt-in profiler overlay, and capture tooling.
The manifests and runbooks are in place; live capture proofs and strict-assets
validation remain the open gaps when a journey still depends on fresh screenshots.

## Record first, verify second

Capture drift is the main failure mode. Keep the workflow explicit:

1. Read the journey doc and the matching manifest together.
2. Capture the frame or recording in the live game window.
3. Check the crop, legibility, and visible state immediately.
4. Run mock verification for schema and OCR preflight.
5. Run live verification only after the capture set is stable.

For a practical checklist, see [recording-runbook.md](./recording-runbook.md).

### Journey Manifest Structure

Each journey is defined in `docs/journeys/manifests/<id>/manifest.json`. The file follows the Phenotype manifest schema:

```json
{
  "id": "us-wsm-phase-1-voxel-actors",
  "intent": "Validate Phase 1 (VoxelEntities) replaces actor billboards with voxel meshes.",
  "keyframe_count": 5,
  "passed": false,
  "steps": [
    {
      "index": 0,
      "slug": "baseline",
      "intent": "Baseline screenshot with phase off — vanilla 2D actor sprites visible.",
      "screenshot_path": "frame-000.png",
      "assertions": {
        "must_contain": ["WorldBox"],
        "must_not_contain": ["Exception", "Error"]
      }
    },
    {
      "index": 1,
      "slug": "open-settings",
      "intent": "Open the WorldSphere tab and locate the 3D Phases window.",
      "screenshot_path": "frame-001.png",
      "assertions": {
        "must_contain": ["3D Phases"]
      }
    },
    {
      "index": 2,
      "slug": "toggle-on",
      "intent": "Toggle Voxel Actors (Phase 1) ON.",
      "screenshot_path": "frame-002.png",
      "assertions": {
        "must_contain": ["Voxel Actors"]
      }
    },
    {
      "index": 3,
      "slug": "reload-world",
      "intent": "Regenerate the world so the new phase takes effect.",
      "screenshot_path": "frame-003.png"
    },
    {
      "index": 4,
      "slug": "verify-voxel",
      "intent": "Actors render as voxel meshes; vanilla 2D sprites are replaced.",
      "screenshot_path": "frame-004.png",
      "assertions": {
        "must_not_contain": ["Material needs to enable instancing", "Failed to compile", "Exception"]
      }
    }
  ]
}
```

## Top-Level Fields

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `id` | string | Yes | — | Kebab-case identifier matching the directory name (e.g., `us-wsm-phase-1-voxel-actors`) |
| `intent` | string | Yes | — | User-facing summary of what the journey validates |
| `steps` | array | Yes | — | Array of step objects (see Step Fields below) |
| `keyframe_count` | int | No | 0 | Number of keyframes (screenshots) in the journey |
| `passed` | boolean | No | false | Set to `true` when the journey has been verified and passes; authors leave as `false` |
| `recording` | string or null | No | null | Path to a video recording of the journey (optional; keep `null` when the journey is screenshot-only) |
| `recording_gif` | string or null | No | null | Path to an animated GIF of the journey (optional; use for short state transitions, not long sessions) |
| `verification` | object or null | No | null | Filled by the verifier after running `phenotype-journey verify`; authors can omit at authoring time |

## Step Fields

Each step in the `steps` array requires:

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `index` | int | Yes | — | 0-indexed step number (0, 1, 2, ...) |
| `slug` | string | Yes | — | URL-safe identifier for the step (e.g., `baseline`, `toggle-on`, `verify-voxel`) |
| `intent` | string | Yes | — | Brief description of what the step does or validates |
| `screenshot_path` | string | Yes | — | Relative path to the screenshot file (e.g., `frame-000.png`); lives in the same directory as `manifest.json` |
| `description` | string or null | No | null | Optional longer description of the step |
| `judge_score` | number or null | No | null | Confidence score assigned by the verifier (0.0–1.0); omit at authoring time |
| `assertions` | object or null | No | null | Assertions that must hold for the step (see Assertions DSL below) |
| `annotations` | array or null | No | null | Bounding-box annotations overlaid on the screenshot (optional; use for visual debugging) |

## Assertions DSL

The `assertions` field (optional) contains ground-truth checks performed on the step's screenshot via OCR:

```json
{
  "must_contain": ["WorldBox", "3D Phases"],
  "must_not_contain": ["Exception", "Error", "Failed"],
  "expected_exit": null,
  "ocr_required": false
}
```

### Assertion Fields

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `must_contain` | string array | [] | List of strings that must appear in the OCR text of the screenshot. **Case-sensitive.** If any string is missing, the assertion fails. |
| `must_not_contain` | string array | [] | List of strings that must NOT appear in the OCR text. If any string is found, the assertion fails. |
| `expected_exit` | int or null | null | For journeys that end with an exit code (e.g., CLI commands), the last keyframe must contain `__EXIT_<N>__` where `<N>` matches this field. Omit for UI journeys. |
| `ocr_required` | boolean | false | If `true`, OCR is required to pass the journey; if `false`, OCR is optional (assertions are soft checks). |

### Example Assertions

**Baseline step** (must not crash):
```json
{
  "must_contain": ["WorldBox"],
  "must_not_contain": ["Exception", "Error"]
}
```

**Settings window open** (must show the 3D Phases panel):
```json
{
  "must_contain": ["3D Phases"]
}
```

**Phase toggle active** (must show the phase name):
```json
{
  "must_contain": ["Voxel Actors"]
}
```

**Post-toggle verification** (must not crash, must not have shader errors):
```json
{
  "must_not_contain": ["Material needs to enable instancing", "Failed to compile", "Exception"]
}
```

## Annotations (Optional)

You can annotate screenshots with bounding boxes to highlight important regions. Each annotation is a small labeled box:

```json
{
  "annotations": [
    {
      "bbox": [100, 50, 200, 100],
      "label": "Voxel Actor",
      "color": "#FF0000",
      "style": "solid",
      "kind": "region",
      "note": "Red voxel mesh visible at this location"
    }
  ]
}
```

### Annotation Fields

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `bbox` | [x, y, w, h] | Yes | — | Bounding box as `[x, y, width, height]` in image pixels (top-left origin) |
| `label` | string | Yes | — | Short label for the annotation (e.g., "Voxel Actor", "3D Phases Window") |
| `color` | string or null | No | null | Hex color code (e.g., `#FF0000` for red) or `null` for default |
| `style` | string | No | "solid" | `"solid"` or `"dashed"` for the box outline |
| `kind` | string | No | "region" | `"region"` (bounding box), `"pointer"` (arrow), or `"highlight"` (translucent fill) |
| `note` | string or null | No | null | Optional longer explanation of the annotation |

## Recording and Capturing Screenshots

### Using the WSM3D Screenshot Tool

WorldSphereMod3D provides a PowerShell script for capturing screenshots:

```powershell
pwsh Tools/wsm3d.ps1 screenshot -Path docs/journeys/manifests/<journey-id>/frame-NNN.png
```

**Example**:
```powershell
pwsh Tools/wsm3d.ps1 screenshot -Path docs/journeys/manifests/us-wsm-phase-1-voxel-actors/frame-000.png
pwsh Tools/wsm3d.ps1 screenshot -Path docs/journeys/manifests/us-wsm-phase-1-voxel-actors/frame-001.png
# ... repeat for each step
```

This captures the active game window and saves it to the specified path. The journey runner will then use OCR on these screenshots to verify assertions.

If the moment you care about is a transition rather than a static frame,
record a short GIF or video as well, but still save the still frame the
manifest expects. The runbook in [recording-runbook.md](./recording-runbook.md)
spells out the order.

### Directory Structure

All screenshots for a journey live in the same directory as the manifest:

```
docs/journeys/manifests/
  us-wsm-phase-1-voxel-actors/
    manifest.json        ← Journey definition
    frame-000.png        ← Baseline screenshot (index 0)
    frame-001.png        ← Settings window open (index 1)
    frame-002.png        ← Phase toggled ON (index 2)
    frame-003.png        ← World reloaded (index 3)
    frame-004.png        ← Voxel actors visible (index 4)
  us-wsm-phase-2-mesh-buildings/
    manifest.json
    frame-000.png
    ... (5 more frames)
```

## How to Author a New Journey

1. **Create the manifest directory**:
   ```powershell
   mkdir docs/journeys/manifests/us-wsm-phase-<N>-<slug>
   ```

2. **Create `manifest.json`** with the journey structure (see template below).

3. **Capture screenshots**:
   - Start with the game in the baseline state (phase off, world loaded).
   - Use `pwsh Tools/wsm3d.ps1 screenshot -Path ...` to capture each step.
   - Label frames as `frame-000.png`, `frame-001.png`, etc.
   - Keep the same crop and window size for the entire journey.

4. **Define assertions** for each step:
   - `must_contain`: OCR text that should appear (e.g., phase name, UI element).
   - `must_not_contain`: Error messages or unexpected state.

5. **Validate the manifest** (see Validation section below).

6. **If needed, add a motion asset**:
   - Use a short GIF or recording only when the step is easier to understand
     in motion than in a still frame.
   - Keep the screenshot set canonical even if you add a recording.
   - Add the asset path to `recording_gif` or `recording` in the manifest,
     and leave the field `null` until the capture exists if the journey is
     still waiting on live media.

### Journey Template (5-Step Pattern)

All WSM3D phase journeys follow this 5-step shape:

```json
{
  "id": "us-wsm-phase-N-<slug>",
  "intent": "Validate Phase N (<feature-name>) <description>.",
  "keyframe_count": 5,
  "passed": false,
  "steps": [
    {
      "index": 0,
      "slug": "baseline",
      "intent": "Baseline screenshot with phase off — vanilla UI visible.",
      "screenshot_path": "frame-000.png",
      "assertions": {
        "must_contain": ["WorldBox"],
        "must_not_contain": ["Exception", "Error"]
      }
    },
    {
      "index": 1,
      "slug": "open-settings",
      "intent": "Open the WorldSphere tab and locate the 3D Phases window.",
      "screenshot_path": "frame-001.png",
      "assertions": {
        "must_contain": ["3D Phases"]
      }
    },
    {
      "index": 2,
      "slug": "toggle-on",
      "intent": "Toggle Phase N ON.",
      "screenshot_path": "frame-002.png",
      "assertions": {
        "must_contain": ["<Phase Name>"]
      }
    },
    {
      "index": 3,
      "slug": "reload-world",
      "intent": "Regenerate the world so the new phase takes effect.",
      "screenshot_path": "frame-003.png"
    },
    {
      "index": 4,
      "slug": "verify-<feature>",
      "intent": "Verify the feature is active and working; vanilla behavior is replaced.",
      "screenshot_path": "frame-004.png",
      "assertions": {
        "must_not_contain": ["Material needs to enable instancing", "Failed to compile", "Exception"]
      }
    }
  ]
}
```

## Validation & Verification

### JSON Schema Validation

Every manifest must parse as valid JSON and conform to the Phenotype manifest schema at `C:/Users/koosh/Dino/tools/phenotype-journeys/schema/manifest.schema.json`.

**Quick validation** (PowerShell):
```powershell
Get-ChildItem docs/journeys/manifests -Filter manifest.json -Recurse | ForEach-Object {
  try {
    Get-Content $_.FullName -Raw | ConvertFrom-Json | Out-Null
    Write-Host "OK $($_.FullName)"
  } catch {
    Write-Host "BAD $($_.FullName): $_"
  }
}
```

### Phenotype Journey CLI

The `phenotype-journey` CLI can verify journeys against actual screenshots (with OCR). The tool is located at:

```
C:/Users/koosh/Dino/tools/phenotype-journeys/bin/phenotype-journey
```

If not yet built, build from source:
```bash
cd C:/Users/koosh/Dino/tools/phenotype-journeys
cargo build --release
# Binary: target/release/phenotype-journey(.exe)
```

**Usage**:
```bash
phenotype-journey verify docs/journeys/manifests/us-wsm-phase-1-voxel-actors/manifest.json --mock
phenotype-journey verify docs/journeys/manifests/us-wsm-phase-1-voxel-actors/manifest.json --live
```

Output: Updated `manifest.json` with:
- `"passed": true/false` (based on assertion results)
- `"verification"` object with scores and violation details

### Mock/Offline Verification

If screenshots are not yet captured, the manifest still parses as valid JSON. The journey-records validator can run in **offline mode** to validate the manifest schema and step ordering without checking screenshot files:

```bash
cargo run --manifest-path Tools/journey-records/Cargo.toml -- validate docs/journeys/manifests/us-wsm-phase-1-voxel-actors/manifest.json
```

This checks schema correctness and contiguity without requiring screenshot files to exist yet. Use strict asset validation when the capture set is present:

```bash
cargo run --manifest-path Tools/journey-records/Cargo.toml -- validate --strict-assets docs/journeys/manifests/us-wsm-phase-1-voxel-actors/manifest.json
```

Strict asset validation requires each step screenshot to exist and resolves optional recording assets when present. If the capture set is still missing, keep the manifest honest and leave the live-verification fields unset instead of implying proof that has not been recorded yet.

## Integration with phenotype-journey CLI

The `index.json` at `docs/journeys/manifests/index.json` lists all journeys:

```json
[
  {
    "id": "us-wsm-phase-1-voxel-actors",
    "intent": "Validate Phase 1 ...",
    "file": "us-wsm-phase-1-voxel-actors/manifest.json"
  },
  ...
]
```

Journey verification is driven per manifest. For batch runs, use `task journeys`,
`just journeys`, or a loop over manifest paths.

```bash
# Example:
for manifest in docs/journeys/manifests/*/manifest.json; do
  phenotype-journey verify "$manifest" --mock
done
```

Mock mode is the default contract and maps to `phenotype-journey verify <manifest> --mock`. Use `--live` only when validating against live captures.

## WSM3D-Specific Notes

### Screenshot Capture Command

All WorldSphereMod3D journey screenshots are captured via:

```powershell
pwsh Tools/wsm3d.ps1 screenshot -Path docs/journeys/manifests/<journey-id>/frame-NNN.png
```

This is a thin wrapper around the game's MCP server (DINOForge MCP) which uses Win32/playCUA to capture the active game window. The tool handles:
- Waiting for the game window to be ready
- Capturing the game viewport (excluding UI overlays if needed)
- Saving as PNG to the specified path

### playCUA Backend

If playCUA is configured, the screenshot tool routes through:
- **playCUA**: Cross-platform screenshot/input injection via JSON-RPC
- **Fallback (Windows)**: Hidden desktop automation (Win32 CreateDesktopW)

See `docs/playcua_phase3_5_spec.md` for backend details.

### Scratch Directory

Ad-hoc test screenshots (not part of a formal journey) can be saved to:

```
docs/journeys/scratch/
```

This directory is git-ignored and serves as a staging area for exploratory screenshots.

## Existing Journeys (Manifest Index)

All 10 WorldSphereMod3D phases have journeys in `docs/journeys/manifests/`:

| Phase | ID | Intent |
|-------|----|----|
| 1 | `us-wsm-phase-1-voxel-actors` | Validate voxel actor rendering |
| 2 | `us-wsm-phase-2-mesh-buildings` | Validate procedural building meshes |
| 3 | `us-wsm-phase-3-crossed-foliage` | Validate crossed-quad foliage with wind |
| 4 | `us-wsm-phase-4-mesh-water` | Validate Gerstner waves + foam |
| 5 | `us-wsm-phase-5-shadows` | Validate cascaded shadow maps |
| 6 | `us-wsm-phase-6-skeletal` | Validate skeletal animation |
| 7 | `us-wsm-phase-7-worldspace-ui` | Validate world-space nameplates/HP bars |
| 8 | `us-wsm-phase-8-day-night` | Validate day-night sky cycle |
| 9 | `us-wsm-phase-9-postfx` | Validate bloom + color grading |
| 10 | `us-wsm-phase-10-lod-impostor` | Validate LOD fallback to impostors |

## Further Reading

- **Phenotype Journeys**: https://github.com/kooshapari/phenotype-journeys
- **Manifest Schema**: `C:/Users/koosh/Dino/tools/phenotype-journeys/schema/manifest.schema.json`
- **WorldSphereMod3D Docs**: `C:/Users/koosh/Dev/WorldSphereMod/docs/`
