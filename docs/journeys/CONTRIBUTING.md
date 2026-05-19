# Contributing Journey Tests

A **journey** is a scripted end-to-end test that captures screenshots of the game, extracts text via OCR, and asserts that specific strings appear or don't appear. Each journey is a JSON manifest with a sequence of steps, where each step has a screenshot and an assertion DSL.

Journeys validate entire features: a phase ships (Phase 1 voxel actors), a UI flow works (open settings, toggle a flag, reload), or a regression is fixed. They run in CI and locally, with screenshots cropped tight to reduce OCR noise and cost.

## Authoring workflow

### 1. Pick your feature
What do you want to validate? A phase completion? A UI interaction? A bug fix? Choose one scope per journey.

### 2. Choose an ID
Use the pattern `us-wsm-<feature-or-phase>-<slug>` in kebab-case. Examples:
- `us-wsm-phase-1-voxel-actors` — Phase 1 lands, actors render as meshes
- `us-wsm-phase-2-mesh-buildings` — Phase 2 lands, procedural buildings work
- `us-wsm-regression-shader-crash` — Specific bug is fixed

### 3. Create the manifest
Create a directory `docs/journeys/manifests/<id>/` and add `manifest.json`:

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
      "slug": "phase-toggle",
      "intent": "Open settings and enable the phase.",
      "screenshot_path": "frame-001.png",
      "assertions": {
        "must_contain": ["Voxel Actors"]
      }
    }
  ]
}
```

Each step has:
- **index** — 0-based frame number
- **slug** — human name (for logs)
- **intent** — what this frame tests (comments for reviewers)
- **screenshot_path** — relative to the manifest directory (e.g., `frame-000.png`)
- **assertions** — optional OCR checks (see table below)

### 4. Update the manifest index
Add your journey to `docs/journeys/manifests/index.json`:
```json
{
  "journeys": [
    { "id": "us-wsm-phase-1-voxel-actors", "path": "us-wsm-phase-1-voxel-actors" },
    { "id": "us-wsm-phase-2-mesh-buildings", "path": "us-wsm-phase-2-mesh-buildings" }
  ]
}
```

### 5. Capture screenshots
Run the capture tool interactively:
```powershell
pwsh Tools/wsm3d.ps1 journey capture -Id us-wsm-phase-1-voxel-actors
```

The tool will:
1. Launch the game with the manifest path
2. Prompt you to take 5 screenshots (one per step)
3. Save them to `frame-000.png`, `frame-001.png`, etc.

### 6. Verify locally
Test your assertions against the captured screenshots. Mock mode uses Tesseract OCR locally (no API key needed):
```powershell
phenotype-journey verify docs/journeys/manifests/us-wsm-phase-1-voxel-actors/manifest.json --mode mock
```

Live mode (with Anthropic's Vision API) requires `ANTHROPIC_API_KEY`:
```powershell
$env:ANTHROPIC_API_KEY = "sk-..."
phenotype-journey verify docs/journeys/manifests/us-wsm-phase-1-voxel-actors/manifest.json --mode live
```

Mock mode is free + deterministic; use it for local iteration. Live mode is more accurate on OCR-mangled text.

### 7. Push and let CI verify
Commit your manifest + screenshots:
```powershell
git add docs/journeys/manifests/us-wsm-phase-1-voxel-actors/
git commit -m "feat: journey for Phase 1 voxel actors"
git push origin feat/phase-1-voxel-actors
```

The CI gate `.github/workflows/journeys-gate.yml` will re-run `phenotype-journey verify` in live mode. If it passes in CI, your journey is solid.

## Assertion DSL

| Field | Type | Description |
|-------|------|-------------|
| `must_contain` | `string[]` | The OCR output must include ALL of these substrings (case-sensitive). Fails if any is missing. |
| `must_not_contain` | `string[]` | The OCR output must NOT include any of these substrings. Fails if any appears. Use for "Exception", "Error", "Failed". |
| `must_contain_regex` | `string[]` | The OCR output must match at least one of these regexes. Use when exact substrings are unreliable due to OCR mangling. |
| `expected_exit` | `number` | The last frame's OCR must contain `__EXIT_<N>__` marker. Used for CLI-driven journeys. |
| `ocr_required` | `boolean` | If `true`, fail if the OCR backend itself crashes. Default `false` (tolerate OCR errors gracefully). |

## Tips

### Install OCR locally
Mock mode uses Tesseract. Install it:
```powershell
choco install tesseract
# or macOS:
brew install tesseract
```

### Crop screenshots tight
Full-screen captures waste OCR tokens and produce noisier matches. Crop to the region you care about. Example: if testing the settings tab, crop to just that window.

### Use annotations for clarity
You can add bounding boxes to flag regions:
```json
"annotations": [
  { "x": 100, "y": 200, "w": 300, "h": 150, "label": "settings_window" }
]
```
Not required, but useful for reviewers to see what the journey is checking.

### Keep steps focused
Each step should test one thing. Bad: "Open settings, toggle three flags, reload world, verify actors are voxels" (5 things). Good: "Baseline with all phases off" → "Toggle Phase 1" → "Reload world" → "Verify voxels" (4 focused steps).

### Reference the schema
The Phenotype journeys package defines the full manifest schema. See the `phenotype-journeys` docs for the exact structure.

---

**Questions?** Check `docs/journeys/README.md` or `CLAUDE.md` "Dev tooling" section for the full journey CLI.
