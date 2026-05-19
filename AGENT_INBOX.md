TL;DR
- Repo A has 10 journey manifests and no embedded capture/gallery/overlay fields yet.
- Repo B has an incomplete Vue3 `@phenotype/journey-viewer` package already present at `tools/phenotype-journeys/npm/journey-viewer/`.
- Recommended convergence is to finish and harden that shared viewer in Repo B, then consume it from Repo A.

Shared need
- One consistent journey-records documentation UX for both repos:
  - playback-ready video records
  - keyframe PNG browsing
  - per-step screenshots
  - OCR overlays
  - optional SVG annotations
- Shared output contract should remain deterministic for docs ingestion and review.

Prior art summary
- Dino (`C:\Users\koosh\Dino`) journey viewer state:
  - Vue3 package already exists under `tools/phenotype-journeys/npm/journey-viewer/`.
  - Source files present: `src/Index.ts`, `JourneyViewer.vue`, `JourneyStep.vue`, `KeyframeGallery.vue`, `StructuralPane.vue`, etc., plus npm artifact and lockfile.
  - Repository-level intent indicates this path is the right convergence anchor, but implementation is incomplete for the richer feature set.
- HWLedger-like scan result:
  - No confirmed repository location found in this environment pass; treat as `HWLedger location TBD`.

Proposed convergence plan
- Source the canonical UI package as `C:\Users\koosh\Dino\tools/phenotype-journeys/npm/journey-viewer/` and publish/consume as `@phenotype/journey-viewer`.
- Keep viewer package pure frontend concerns (Vue3 + TanStack UI primitives where useful).
- Add a shared JSON manifest/data schema extension for:
  - timeline video URI
  - step screenshot list
  - keyframe asset references
  - OCR text + bounding boxes
  - optional SVG overlay assets
- In this repo (WorldSphereMod), add only thin references into docs pages that consume the shared viewer contract (no UI implementation drift here).
- Cross-link with sibling inbox for execution coordination:
  - `../Dino/AGENT_INBOX.md`

Next actions for the Dino-side session (3-5 concrete steps)
1. Finalize the `journey-viewer` data model in `src/types.ts` (explicit optional/required fields for video, keyframes, per-step shots, OCR, SVG overlays).
2. Implement/complete the viewer components to render all required layers from manifest data with predictable fallbacks.
3. Add manifest parsing + validation (if missing fields, degrade gracefully with disabled layers).
4. Publish a `@phenotype/journey-viewer` release/tarball update and document usage.
5. Return back a minimal integration example for WorldSphereMod manifest conversion/rendering.

Tech-stack hints
- Tooling preference: Rust or Go for future batch processors/transformers that generate manifest artifacts.
- UI preference: TanStack + Vue3 (existing alignment with this repo’s package context), with a single shared component library in Dino and consumer-only integration in Repo A.

Cross-repo coordination
- Repo B execution will own the shared package completion and release quality.
- Repo A execution should only adapt manifests + embedding points to consume that package once the shared contract is stable.
