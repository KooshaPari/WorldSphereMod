# Journey integration trace

Scope: Phase 1 edit -> journey manifest/preview -> PR gate -> report.

## Trace

1. A developer edits Phase 1 code in the mod source tree, but the gate only watches `.github/workflows/journeys-gate.yml` and `docs/journeys/**`, not `WorldSphereMod/Code/**`. A direct Phase 1 code change therefore does not trigger the journey gate by itself. `.github/workflows/journeys-gate.yml:7-16`
2. The journey manifest for Phase 1 lives under `docs/journeys/manifests/us-wsm-phase-1-voxel-actors/manifest.json` and defines the expected 5-step flow. `docs/journeys/manifests/us-wsm-phase-1-voxel-actors/manifest.json:1-46`
3. The canonical preview PNGs are stored separately under `docs/journeys/phase-previews/phase-1-voxel-actors/`, and the gallery page exposes them for docs only. `docs/journeys/phase-previews/index.md:1-27`
4. The gate workflow clones and builds `phenotype-journey`, validates JSON, checks preview PNG existence, and then iterates manifests. `.github/workflows/journeys-gate.yml:49-150`

## Findings

- High: the PR gate is not actually blocking on journey verification. The `verify` step pipes output through `tail -5 || true`, so any CLI failure is swallowed, and the job still prints `✓ All manifests verified`. That means the workflow can report success even if the manifest verifier fails. `.github/workflows/journeys-gate.yml:137-150`
- High: the workflow does not cover the actual code-edit trigger path. It is path-filtered to docs and workflow files only, so a Phase 1 source edit can land without the journey gate running at all unless the author also touches `docs/journeys/**`. `.github/workflows/journeys-gate.yml:7-16`
- Medium: historical doc drift around verification CLI. Earlier notes mixed the `wsm3d journey verify -Id <id>` wrapper with direct `phenotype-journey` calls. The direct `phenotype-journey` contract uses manifest paths plus `--mock` / `--live`; `-Id` is for the `wsm3d` wrapper, not the underlying CLI. This is now a superseded note, but it is worth keeping the distinction explicit in any future doc edits. `.github/workflows/journeys-gate.yml:144-146` `docs/journeys/CONTRIBUTING.md:80-102` `docs/journeys/README.md:344-353` `docs/HANDOFF.md:125-130`
- Medium: the authoring docs describe the manifest index with a different shape than the file actually uses. CONTRIBUTING says `{"journeys":[{"id","path"}]}`, but the real index is a top-level array with `id`, `intent`, and `file`. That breaks the documented handoff for new journey authors. `docs/journeys/CONTRIBUTING.md:58-67` `docs/journeys/manifests/index.json:1-52`
- Medium: the canonical preview PNGs are only checked for existence and hash diversity; nothing ties them back to the Phase 1 manifest or to the edited Phase 1 code. They are a docs asset, not part of the verifier's acceptance criteria, so they do not enforce freshness when Phase 1 behavior changes. `.github/workflows/journeys-gate.yml:70-128` `docs/journeys/phase-previews/index.md:1-27`
- Low: the workflow's only persistent output is an artifact upload of `docs/journeys/manifests/`; there is no summarized verification report, PR comment, or status file for reviewers to inspect. `.github/workflows/journeys-gate.yml:152-158`

## Bottom line

The chain is only partially wired. The manifests and preview assets exist, but the PR gate does not reliably fire on Phase 1 code edits, does not reliably fail on verifier errors, and does not publish a real verification report. The contributor docs also drift from the actual index/CLI shape, which makes the intended cycle harder to follow and easier to break. `docs/journeys/manifests/us-wsm-phase-1-voxel-actors/manifest.json:1-46` `.github/workflows/journeys-gate.yml:7-16` `.github/workflows/journeys-gate.yml:137-150`
