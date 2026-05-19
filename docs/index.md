---
layout: home

hero:
  name: WorldSphereMod3D
  text: Finishing the 3D conversion of WorldBox
  tagline: A hard fork of MelvinShwuaner/WorldSphereMod. Upstream made the terrain 3D. This fork makes everything else 3D — actors, buildings, foliage, water, lighting, animation, UI.
  actions:
    - theme: brand
      text: Get started
      link: /journeys/install-and-play
    - theme: alt
      text: 10-phase plan
      link: /PLAN
    - theme: alt
      text: View on GitHub
      link: https://github.com/KooshaPari/WorldSphereMod

features:
  - title: Voxelized actors
    details: Every sprite-billboard actor, item, drop, and projectile is replaced with a per-sprite voxel mesh, batched via DrawMeshInstanced. (Phase 1)
  - title: Procedural buildings
    details: Building meshes generated from sprite footprints + heuristic roof inference. Override per-asset via JSON rules. (Phase 2)
  - title: Crossed-quad foliage
    details: Trees, bushes, rocks ship as crossed quads with wind-sway vertex displacement. Cheap, looks right at any angle. (Phase 3)
  - title: Mesh water
    details: Gerstner-wave surface mesh overlaid on the terrain, depth-tinted, with shoreline foam from depth gradient. (Phase 4)
  - title: Real sun + cascaded shadows
    details: Directional light parented to the camera rig, 4 URP shadow cascades, SSAO. Per-vertex terrain normals. (Phase 5)
  - title: Skeletal animation
    details: Auto-rigged voxel actors driven by WorldBox's existing AnimationFrameData. Custom rigs for Crabzilla, dragons. (Phase 6)
  - title: Worldspace UI
    details: Nameplates, HP bars, damage numbers, selection rings — all in 3D space with distance-fade. (Phase 7)
  - title: Day / night / sky
    details: Procedural Hosek-Wilkie sky, autonomous time-of-day driver, height fog, color-temperature shift. (Phase 8)
  - title: Particles / decals / PostFX
    details: Voxel-mesh particle bursts, pooled DecalProjectors for scorch/blood/footprints, URP PostFX volume. (Phase 9)
  - title: LOD + impostor fallback
    details: Voxel → low-poly proxy → impostor billboard. Same path serves the compatibility fallback for older GPUs. (Phase 10)
---

## What this site is

The full documentation for **WorldSphereMod3D**, a hard fork of
[MelvinShwuaner/WorldSphereMod](https://github.com/MelvinShwuaner/WorldSphereMod).
Start with one of these:

- **Just want to play?** → [Install & play journey](/journeys/install-and-play)
- **Contributing code?** → [Contribute a phase](/journeys/contribute-a-phase) and the [PR checklist](/PR_CHECKLIST)
- **Writing a downstream mod?** → [Extend via the API](/journeys/extend-via-api)
- **Power user?** → [Tooling reference](/tooling) — CLI, MCP, slash commands for automation
- **Cold-starting a new agent session?** → [`HANDOFF`](/HANDOFF) and [`CLAUDE.md`](https://github.com/KooshaPari/WorldSphereMod/blob/main/CLAUDE.md) at the repo root
- **Need the full plan?** → [`PLAN.md`](/PLAN) — every phase, every file, every verification step

## Phase status

| Phase | Status | Summary |
|---|---|---|
| 0 — Fork plumbing | landed | Build portability (`WORLDBOX_PATH`), CI, settings + API v2 |
| 1 — Voxel actors | ready-to-test | Greedy mesher + DrawMeshInstanced; awaits in-game smoke |
| 2 — Procedural buildings | code-complete | Heuristic + roof inference; awaits smoke |
| 3 — Foliage / walls / overlays | landed | Crossed quads ON by default |
| 4 — Mesh water | landed (lite) | Gerstner surface ON; AssetBundle bake deferred |
| 5 — Lighting + shadows | research | `SunDriver`/`ShadowCascadeConfig` landed; lit shader bake pending Unity 2022.3 |
| 6 — Skeletal animation | landed | Humanoid + quadruped rigs + compute skin; flag default OFF |
| 7 — Worldspace UI | landed | Nameplate / HP / damage popups ON |
| 8 — Day/night + sky | landed | Autonomous TOD; flag default OFF |
| 9 — Particles / decals / PostFX | landed | Particles ON, PostFX OFF |
| 10 — LOD + impostor | landed | Soft hardware gate; Proxy tier falls through to Voxel |

See [`HANDOFF`](/HANDOFF) for the live state and any blocking items.

## Live deploys

This site builds in two places simultaneously — same VitePress source, two
hosts:

| Target | URL | Driver |
|---|---|---|
| GitHub Pages | <https://kooshapari.github.io/WorldSphereMod/> | `.github/workflows/docs-deploy.yml` (uses `actions/deploy-pages`, builds with `DOCS_BASE=/WorldSphereMod/`) |
| Vercel Production | the project's Vercel domain | `.github/workflows/vercel-production.yml` (uses Vercel CLI, builds with `DOCS_BASE=/`) |

The split exists because GH Pages serves under a repo-path prefix while
Vercel serves from the project root; the `DOCS_BASE` env var in
`docs/.vitepress/config.mts` lets one VitePress config target both.

The setup is fully CLI-automated — no GitHub UI clicks required:

- Pages source toggle: `gh api -X POST repos/<owner>/<repo>/pages -F build_type=workflow`
- Vercel link: `vercel link --yes --project <name>` (writes `.vercel/project.json`)
- Repo secrets: `gh secret set VERCEL_TOKEN/VERCEL_ORG_ID/VERCEL_PROJECT_ID`
