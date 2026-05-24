# Replace Journeys Research

**Recommendation: keep building `phenotype-journeys`, not replace it.**

The current harness already owns the pieces the alternatives do not combine well: WorldBox capture, OCR assertions, manifest-driven scenarios, and phase-specific docs. The gaps are in diffing, capture automation, and broader test layers.

## Survey

| Option | Fit |
|---|---|
| [Playwright](https://playwright.dev/docs/test-snapshots) | Strong for web-only screenshot baselines and traceable browser tests. Good for our Vercel/docs preview flows, but it does not exercise the Unity game window. |
| [Unity Test Framework](https://docs.unity3d.com/Manual/com.unity.test-framework.html) + [Recorder](https://docs.unity3d.com/Manual/com.unity.recorder.html) | Good for Edit Mode / Play Mode tests inside Unity, and Recorder can capture images/video from the Unity Editor. The Recorder docs explicitly scope it to the Editor, so it is not a stand-alone WorldBox mod harness. |
| [pixelmatch](https://github.com/mapbox/pixelmatch), [Resemble.js](https://github.com/Huddle/Resemble.js), [ImageMagick compare](https://legacy.imagemagick.org/script/compare.php/) | Best match for visual regression. `pixelmatch` is lightweight and CI-friendly; Resemble.js adds richer comparison options; ImageMagick is a reliable CLI fallback for ad hoc triage. |
| Steam Game Recording / [OBS automation](https://obsproject.com/kb/remote-control-guide) | Useful as capture plumbing. Steam recording is background video capture, and OBS can be remote-controlled via WebSocket, but neither gives us assertions or pixel diffs. |
| [AltTester Unity SDK](https://altom.com/testing-tools/) | Useful when we need object-level automation inside Unity, especially for inspecting and controlling game/app state. It is stronger on interaction than on visual or OCR verification. |
| [Apptim](https://docs.apptim.com/) | Good for performance collection. The docs emphasize on-prem/container deployment, parallel sessions, and REST APIs, but it is mobile/perf oriented rather than a desktop mod visual harness. |

## What To Keep In `phenotype-journeys`

Keep `phenotype-journeys` as the source of truth for:

- journey manifests and step order
- screenshot capture of the actual WorldBox window
- OCR assertions and “must not contain” checks
- phase preview fixtures under `docs/journeys/phase-previews/`
- CI reporting and artifact publishing

## What To Add

1. **Pluggable image diff backend.** Add `pixelmatch` first, with ImageMagick as a fallback/manual tool. This closes the biggest gap in the current harness: we have screenshots, but not a robust pixel comparison gate.
2. **Capture abstraction.** Keep the current capture path, but make the transport swappable so OBS or other recorders can be used for artifact capture without changing the journey contract.
3. **Optional Unity-layer tests.** Use Unity Test Framework for fast editor/playmode checks on pure game logic, not as a replacement for end-to-end journeys.
4. **Optional object automation bridge.** Add AltTester only if we need deeper in-game inspection/actions than the current bridge can provide.
5. **Performance sidecar.** Use Apptim-style collection only for perf runs, not for the primary visual regression gate.

## Bottom Line

No surveyed tool replaces `phenotype-journeys` end to end. The best path is to keep the manifest/OCR model and add pixel diffing plus a cleaner capture layer. That gives us a game-native visual regression harness without losing the current docs and CI workflow.
