import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'WorldSphereMod3D',
  description: 'Hard fork of WorldSphereMod that finishes the 3D conversion of WorldBox.',
  lang: 'en-US',
  // base is repo-subpath on GitHub Pages (kooshapari.github.io/WorldSphereMod/),
  // but root on Vercel / dev. DOCS_BASE overrides for non-Pages targets.
  base: process.env.DOCS_BASE ?? '/WorldSphereMod/',
  lastUpdated: true,
  cleanUrls: true,
  appearance: 'dark',
  ignoreDeadLinks: true,
  srcExclude: ['**/screenshots/**', '**/journeys/assets/**/README.md'],

  themeConfig: {
    siteTitle: 'WorldSphereMod3D',

    nav: [
      { text: 'Guide', link: '/CONTRIBUTING' },
      { text: 'Architecture', link: '/phase2-architecture' },
      { text: 'Journeys', link: '/journeys/install-and-play' },
      { text: 'ADRs', link: '/adr/' },
      { text: 'Reference', link: '/render-data-fields' },
      { text: 'GitHub', link: 'https://github.com/KooshaPari/WorldSphereMod' },
    ],

    sidebar: [
      {
        text: 'Guide',
        items: [
          { text: 'Overview', link: '/' },
          { text: 'Plan (10-phase)', link: '/PLAN' },
          { text: 'Handoff', link: '/HANDOFF' },
          { text: 'Contributing', link: '/CONTRIBUTING' },
          { text: 'PR Checklist', link: '/PR_CHECKLIST' },
          { text: 'Phase 1 review', link: '/phase1-review' },
          { text: 'Phase 1 smoke test', link: '/smoke-test-phase1' },
          { text: 'Performance', link: '/performance' },
        ],
      },
      {
        text: 'Architecture (per phase)',
        collapsed: false,
        items: [
          { text: 'Phase 2 — Procedural buildings', link: '/phase2-architecture' },
          { text: 'Phase 3 — Foliage / walls / overlays', link: '/phase3-architecture' },
          { text: 'Phase 3 decompile findings', link: '/phase3-decompile-findings' },
          { text: 'Phase 4 — Mesh water', link: '/phase4-architecture' },
          { text: 'Phase 5 — Lighting / shadows', link: '/phase5-architecture' },
          { text: 'Phase 5 prep', link: '/phase5-prep' },
          { text: 'Phase 6 — Skeletal animation', link: '/phase6-architecture' },
          { text: 'Phase 7 — Worldspace UI', link: '/phase7-architecture' },
          { text: 'Phase 8 — Sky / TOD', link: '/phase8-architecture' },
          { text: 'Phase 9 — Particles / decals / PostFX', link: '/phase9-architecture' },
          { text: 'Phase 10 — LOD / impostor', link: '/phase10-architecture' },
        ],
      },
      {
        text: 'User Journeys',
        collapsed: false,
        items: [
          { text: 'Install & play', link: '/journeys/install-and-play' },
          { text: 'Contribute a phase', link: '/journeys/contribute-a-phase' },
          { text: 'Extend via the API', link: '/journeys/extend-via-api' },
          { text: 'Diagnose performance', link: '/journeys/diagnose-perf' },
          { text: 'Upgrade from upstream', link: '/journeys/upgrade-from-upstream' },
        ],
      },
      {
        text: 'Architecture Decisions',
        collapsed: false,
        items: [
          { text: 'ADR index', link: '/adr/' },
          { text: 'Template', link: '/adr/template' },
          { text: '0001 — Hybrid sprite→3D strategy', link: '/adr/0001-hybrid-sprite-to-3d-strategy' },
          { text: '0002 — Defer shader bake to Unity 2022.3', link: '/adr/0002-defer-shader-bake-to-unity-2022-3' },
          { text: '0003 — Reflective URP bindings', link: '/adr/0003-reflective-urp-bindings' },
          { text: '0004 — Rigid skinning over blended', link: '/adr/0004-rigid-skinning-over-blended' },
          { text: '0005 — Default-on flags per phase ship gate', link: '/adr/0005-default-on-flags-per-phase-ship-gate' },
        ],
      },
      {
        text: 'Reference',
        items: [
          { text: 'render_data field map', link: '/render-data-fields' },
          { text: 'Performance budgets', link: '/performance' },
        ],
      },
    ],

    editLink: {
      pattern: 'https://github.com/KooshaPari/WorldSphereMod/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },

    search: {
      provider: 'local',
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/KooshaPari/WorldSphereMod' },
    ],

    footer: {
      message: 'Hard fork of MelvinShwuaner/WorldSphereMod. MIT-licensed where not otherwise marked.',
      copyright: '© 2026 WorldSphereMod3D contributors',
    },
  },
})
