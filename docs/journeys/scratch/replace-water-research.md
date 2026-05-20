# Water Replacement Research

Current baseline: WSM3D already has Phase 4 mesh water with Gerstner displacement, shoreline foam, and code-driven lifecycle hooks. Any replacement has to fit WorldBox’s built-in render pipeline and preserve that runtime control.

## Comparison

- **Crest Ocean**  
Best overall fit among the candidates. It is open-source, mature, and closer to a code-first water stack that can be adapted than a black-box commercial asset. The downside is that it is still an ocean system, so inland/lake-style WorldBox water and custom tile masking would need integration work.

- **KWS Water System**  
Strong visual quality and feature depth, but it is a heavier commercial dependency. Source access and mod-level patchability are weaker than Crest, so it is a worse fit when the goal is to keep the water system maintainable inside a mod.

- **Unity HDRP Water**  
Not a fit. It is HDRP-only, while WorldBox is on the built-in render pipeline. Using it would mean changing rendering assumptions the game does not have.

- **Calm Water**  
Lightweight and probably the easiest to drop in, but also the least capable. It is fine for a simple surface, not for WSM3D’s current target of displaced water with foam, depth tint, and tighter terrain integration.

## Recommendation

Do **not** replace `WaterRender` unless the project explicitly wants to trade custom control for a third-party dependency. The current Phase 4 implementation is the best fit for WorldBox’s built-in pipeline and for the mod’s tile-aware water behavior.

If a package replacement is mandatory, **Crest Ocean** is the best option: open-source, adaptable, and the least mismatched to a mod that needs source-level control. KWS is the runner-up for visuals, while HDRP Water is ruled out and Calm Water is too limited.
