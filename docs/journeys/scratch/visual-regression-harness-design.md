# Visual Regression Harness Design

Goal: turn the existing phase preview screenshots into a repeatable regression gate for WSM3D.

## Inputs And Contract

- Canonical fixtures already exist at `docs/journeys/phase-previews/<phase>/before.png` and `after.png`.
- `Tools/wsm3d-capture` is the preferred capture primitive because it targets the running WorldBox window directly.
- `Tools/wsm3d.ps1 screenshot` stays available as the manual fallback and debug path.
- `phenotype-journey verify <manifest> --mock` remains a manifest/schema preflight, not the visual diff gate.

## Workflow

1. Boot WorldBox in a deterministic state.
   - Launch with the usual mod path plus `WSM3D_AUTOTEST=1`.
   - Load a canned save or seed for the phase journey.
   - Wait for the Harmony self-test hook to report that the scene is ready before capturing.

2. Capture the phase journey state.
   - For each phase, take a `before` frame and an `after` frame.
   - `before` is the baseline state from the canonical fixture.
   - `after` is the post-toggle state that should match the visual intent of the phase.
   - Save captures under a per-run temp directory, keyed by phase slug.

3. Compare against canonical fixtures.
   - Compare `before` to `before.png` and `after` to `after.png`.
   - Use SSIM as the primary gate with a pass threshold of `>= 0.95`.
   - Add a pixel-tolerance mask for triage, so tiny UI shimmer or compression noise does not hide real regressions.
   - If SSIM fails, emit a diff image, a compact stats JSON, and the captured PNG for inspection.

4. Report the result.
   - In PR runs, upload the diff artifacts and post a short comment with the phase, SSIM score, and artifact links.
   - If PR commenting is unavailable, fail the check and leave the artifacts in the workflow run.
   - Keep the job deterministic: same save, same capture path, same threshold.

## CI Shape

- Use a matrix over `docs/journeys/phase-previews/<phase>/`.
- Each matrix entry resolves its own canonical `before.png` and `after.png`.
- The job should fail fast on launch or capture errors, and only evaluate the diff when both screenshots exist.
- The existing Vercel screenshot workflow is a useful pattern for artifact publication and URL/target resolution, but this harness is local-process based rather than web based.

## Tooling Notes

- `Tools/wsm3d-capture` should own window capture and file output.
- `Tools/wsm3d.ps1 screenshot` should remain the human-friendly wrapper for ad hoc captures.
- `phenotype-journey verify <manifest> --mock` can still run as a preflight step to catch manifest drift, but it should not be treated as proof that the visuals are correct.

## Failure Modes

- Missing or stale canned save: stop before capture and report the phase slug.
- Wrong process or wrong window: capture fails before diffing.
- Stable but wrong rendering: SSIM drops below threshold and the diff artifact becomes the reviewer input.
- Small noise only: keep a narrow pixel tolerance so the gate does not churn on harmless frame variance.

## Outcome

This gives each phase a concrete visual contract: boot the same scene, capture the same before/after frames, compare against canonical PNGs, and make regressions visible in the PR rather than after merge.
