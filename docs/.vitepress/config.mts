import { defineConfig } from "vitepress";

export default defineConfig({
    title: "WorldSphereMod3D",
    description:
        "Hard fork of WorldSphereMod that finishes the 3D conversion of WorldBox.",
    lang: "en-US",
    // base is repo-subpath on GitHub Pages (kooshapari.github.io/WorldSphereMod/),
    // but root on Vercel / dev. DOCS_BASE overrides for non-Pages targets.
    base: process.env.DOCS_BASE ?? "/WorldSphereMod/",
    lastUpdated: true,
    cleanUrls: true,
    appearance: "dark",
    ignoreDeadLinks: true,
    srcExclude: [
        "**/screenshots/**",
        "**/journeys/assets/**/README.md",
        "journeys/scratch/**",
        "PRD.md",
    ],

    themeConfig: {
        siteTitle: "WorldSphereMod3D",

        nav: [
            { text: "Guide", link: "/CONTRIBUTING" },
            { text: "Architecture", link: "/phase2-architecture" },
            { text: "Journeys", link: "/journeys/install-and-play" },
            { text: "Tooling", link: "/tooling" },
            { text: "ADRs", link: "/adr/" },
            { text: "Reference", link: "/render-data-fields" },
            {
                text: "GitHub",
                link: "https://github.com/KooshaPari/WorldSphereMod",
            },
        ],

        sidebar: [
            {
                text: "Guide",
                items: [
                    { text: "Overview", link: "/" },
                    { text: "Plan (10-phase)", link: "/PLAN" },
                    { text: "Handoff", link: "/HANDOFF" },
                    { text: "Contributing", link: "/CONTRIBUTING" },
                    { text: "PR Checklist", link: "/PR_CHECKLIST" },
                    { text: "Phase 1 review", link: "/phase1-review" },
                    {
                        text: "Smoke test index",
                        link: "/smoke-test-index",
                    },
                    { text: "Phase 1 smoke test", link: "/smoke-test-phase1" },
                    { text: "Phase 2 smoke test", link: "/smoke-test-phase2" },
                    { text: "Phase 3 smoke test", link: "/smoke-test-phase3" },
                    { text: "Phase 4 smoke test", link: "/smoke-test-phase4" },
                    { text: "Phase 5 smoke test", link: "/smoke-test-phase5" },
                    { text: "Phase 6 smoke test", link: "/smoke-test-phase6" },
                    { text: "Phase 7 smoke test", link: "/smoke-test-phase7" },
                    { text: "Phase 8 smoke test", link: "/smoke-test-phase8" },
                    { text: "Phase 9 smoke test", link: "/smoke-test-phase9" },
                    { text: "Phase 10 smoke test", link: "/smoke-test-phase10" },
                    { text: "Performance", link: "/performance" },
                ],
            },
            {
                text: "Architecture (per phase)",
                collapsed: false,
                items: [
                    {
                        text: "Phase 2 — Procedural buildings",
                        link: "/phase2-architecture",
                    },
                    {
                        text: "Phase 3 — Foliage / walls / overlays",
                        link: "/phase3-architecture",
                    },
                    {
                        text: "Phase 3 decompile findings",
                        link: "/phase3-decompile-findings",
                    },
                    {
                        text: "Phase 4 — Mesh water",
                        link: "/phase4-architecture",
                    },
                    {
                        text: "Phase 5 — Lighting / shadows",
                        link: "/phase5-architecture",
                    },
                    { text: "Phase 5 prep", link: "/phase5-prep" },
                    {
                        text: "Phase 6 — Skeletal animation",
                        link: "/phase6-architecture",
                    },
                    {
                        text: "Phase 7 — Worldspace UI",
                        link: "/phase7-architecture",
                    },
                    {
                        text: "Phase 8 — Sky / TOD",
                        link: "/phase8-architecture",
                    },
                    {
                        text: "Phase 9 — Particles / decals / PostFX",
                        link: "/phase9-architecture",
                    },
                    {
                        text: "Phase 10 — LOD / impostor",
                        link: "/phase10-architecture",
                    },
                ],
            },
            {
                text: "User Journeys",
                collapsed: false,
                items: [
                    {
                        text: "Install & play",
                        link: "/journeys/install-and-play",
                    },
                    {
                        text: "Phase previews",
                        link: "/journeys/phase-previews/",
                    },
                    {
                        text: "Contribute a phase",
                        link: "/journeys/contribute-a-phase",
                    },
                    {
                        text: "Extend via the API",
                        link: "/journeys/extend-via-api",
                    },
                    {
                        text: "Diagnose performance",
                        link: "/journeys/diagnose-perf",
                    },
                    {
                        text: "Upgrade from upstream",
                        link: "/journeys/upgrade-from-upstream",
                    },
                ],
            },
            {
                text: "Tooling",
                collapsed: false,
                items: [{ text: "Tooling reference", link: "/tooling" }],
            },
            {
                text: "Architecture Decisions",
                collapsed: false,
                items: [
                    { text: "ADR index", link: "/adr/" },
                    { text: "Template", link: "/adr/template" },
                    {
                        text: "0001 — Hybrid sprite→3D strategy",
                        link: "/adr/0001-hybrid-sprite-to-3d-strategy",
                    },
                    {
                        text: "0002 — Defer shader bake to Unity 2022.3",
                        link: "/adr/0002-defer-shader-bake-to-unity-2022-3",
                    },
                    {
                        text: "0003 — Reflective URP bindings",
                        link: "/adr/0003-reflective-urp-bindings",
                    },
                    {
                        text: "0004 — Rigid skinning over blended",
                        link: "/adr/0004-rigid-skinning-over-blended",
                    },
                    {
                        text: "0005 — Default-on flags per phase ship gate",
                        link: "/adr/0005-default-on-flags-per-phase-ship-gate",
                    },
                    {
                        text: "0006 — Phase 6 step 9 drawprocedural skinning",
                        link: "/adr/ADR-0006-phase-6-step-9-drawprocedural-skinning",
                    },
                    {
                        text: "0007 — Conditional Harmony patch dispatch",
                        link: "/adr/ADR-0007-conditional-patch-dispatch",
                    },
                    {
                        text: "0007 — NML precompiled detection followup",
                        link: "/adr/ADR-0007-nml-precompiled-detection-followup",
                    },
                    {
                        text: "0007 — NML precompiled detection",
                        link: "/adr/ADR-0007-nml-precompiled-detection",
                    },
                    {
                        text: "0008 — Voxel mesh smoothing",
                        link: "/adr/ADR-0008-voxel-mesh-smoothing",
                    },
                    {
                        text: "0009 — Voxel lit material",
                        link: "/adr/ADR-0009-voxel-lit-material",
                    },
                    {
                        text: "0010 — 3d clouds",
                        link: "/adr/ADR-0010-3d-clouds",
                    },
                    {
                        text: "0011 — Phase 1 visibility postmortem",
                        link: "/adr/0011-phase-1-visibility-postmortem",
                    },
                    {
                        text: "0012 — Phase 2 procedural not rendering",
                        link: "/adr/0012-phase-2-procedural-not-rendering",
                    },
                    {
                        text: "0013 — Flush gate silently drops foliage",
                        link: "/adr/0013-flush-gate-silently-drops-foliage",
                    },
                    {
                        text: "0014 — Autotest persist and tile dirty",
                        link: "/adr/0014-autotest-persist-and-tile-dirty",
                    },
                    {
                        text: "0015 — Actor invisibility final root causes",
                        link: "/adr/0015-actor-invisibility-final-root-causes",
                    },
                    {
                        text: "0016 — Phase 1 victory chain",
                        link: "/adr/0016-phase-1-victory-chain",
                    },
                    {
                        text: "0016 — Thread-safe MeshInstanceBatcher.Submit via deferred queue",
                        link: "/adr/0016-thread-safe-meshinstancebatcher-submit-deferred-queue",
                    },
                    {
                        text: "0020 — Wave-26 follow-up: docs posture + rig inventory",
                        link: "/adr/ADR-0020-wave-26-follow-up",
                    },
                    {
                        text: "0012 — AssetBundle shader bake plan",
                        link: "/adr/ADR-0012-assetbundle-shader-bake-plan",
                    },
                ],
            },
            {
                text: "Reference",
                items: [
                    { text: "Releases", link: "/RELEASES" },
                    {
                        text: "render_data field map",
                        link: "/render-data-fields",
                    },
                    { text: "Performance budgets", link: "/performance" },
                    { text: "Stats dashboard", link: "/dashboard" },
                ],
            },
        ],

        editLink: {
            pattern:
                "https://github.com/KooshaPari/WorldSphereMod/edit/main/docs/:path",
            text: "Edit this page on GitHub",
        },

        search: {
            provider: "local",
        },

        socialLinks: [
            {
                icon: "github",
                link: "https://github.com/KooshaPari/WorldSphereMod",
            },
        ],

        footer: {
            message:
                "Hard fork of MelvinShwuaner/WorldSphereMod. MIT-licensed where not otherwise marked.",
            copyright: "© 2026 WorldSphereMod3D contributors",
        },
    },
});
