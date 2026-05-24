# WSM3D holistic quality gaps, ranked by adoption impact

Ranking is from highest to lowest impact on adoption, not engineering effort.

1. **Onboarding** - Highest leverage. A first-run tutorial popup, bundled sample save, and a sane default phase preset remove the “what do I do now?” problem immediately. This is the shortest path from install to visible value.
2. **Documentation site** - The current VitePress site is strong on architecture and phase history, but it is still missing the content that new users actually need: getting-started, cookbook recipes, troubleshooting, FAQ, and video walkthroughs. Better docs reduce support load and make the fork feel finishable.
3. **Marketing** - Showcase reel, screenshot gallery, and an explicit comparison vs upstream are the main discovery layer. If users cannot quickly see why this fork exists and what looks different, they will not click through to install.
4. **Community** - Discord, forum, or GitHub Discussions gives users a place to ask questions before they abandon the install. This matters once people are evaluating the project, especially for bugs, mod compatibility, and feature requests.
5. **Accessibility** - Color-blind-safe voxel palettes, key rebinding, font scaling, and screen-reader hints widen the reachable audience and reduce exclusion. This is not just compliance work; it makes the mod easier to use for everyone.
6. **i18n** - English, Russian, Czech, and Chinese coverage is a good base, and the locale-key coverage test is the right guardrail. Adding Spanish, French, German, Portuguese, Japanese, and simplified Chinese would improve reach, but translation is usually a second-order adoption driver after first-run usability.
7. **Showroom** - Phase-by-phase gameplay demo videos are useful proof, but they are mostly a presentation layer on top of marketing and docs. They help conversion, though less than a clear install/tutorial path.
8. **User analytics** - Opt-in telemetry and crash reporting help retention and support, especially after users have already installed. They build confidence in maintenance quality, but they do not create adoption on their own.
9. **Performance dashboard public-facing** - Uptime, frame budget, and regression tracking build trust with power users and contributors. Useful for credibility, but most prospective players will not discover the project through a metrics page.
10. **Plugin ecosystem** - Letting users add phases without editing core code is strategically important, but it is a platform bet, not an adoption driver. It pays off after the project already has momentum.

Bottom line: if the goal is more installs, start with onboarding plus docs, then pair that with marketing and a community surface. Accessibility and i18n broaden who can stay, while telemetry, dashboards, and plugin extensibility matter more after the base experience is already usable.
