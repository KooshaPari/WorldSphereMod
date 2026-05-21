# WSM3D scratch documentation index

This index groups all `docs/journeys/scratch/*` files landed this session by topic, with one-line summaries.

## Buy-vs-build & roadmap
- [`actor-vs-building-render-diff.md`](scratch/actor-vs-building-render-diff.md): Identifies a likely actor visibility regression root cause in `VoxelRender` and the exact early-return branch to remove.
- [`actors-fixed-190054.png`](scratch/actors-fixed-190054.png): Binary proof image for actor visibility validation after fixing the branch behavior.
- [`buy-vs-build-roadmap.md`](scratch/buy-vs-build-roadmap.md): Prioritizes top replacement decisions across replace-* areas and keeps explicit build-vs-buy tradeoffs.
- [`replace-frustum-lod-research.md`](scratch/replace-frustum-lod-research.md): Compares current FRUSTUM/LOD render path against alternatives for the actor/building pipeline.
- [`replace-journeys-research.md`](scratch/replace-journeys-research.md): Recommends keeping phenotype-journeys and extending it instead of replacing it.
- [`replace-decal-particle-research.md`](scratch/replace-decal-particle-research.md): Evaluates decal and particle replacement choices for Phase 9 effects.
- [`replace-mesh-instance-batcher-research.md`](scratch/replace-mesh-instance-batcher-research.md): Reviews `MeshInstanceBatcher` alternatives and upgrade complexity.
- [`replace-mesh-smoother-research.md`](scratch/replace-mesh-smoother-research.md): Assesses MeshSmoother replacement and costs at cache-build time.
- [`replace-rig-driver-research.md`](scratch/replace-rig-driver-research.md): Concludes keeping custom `RigDriver` is currently the right direction.
- [`replace-sky-research.md`](scratch/replace-sky-research.md): Tests feasibility of sky replacement versus the current ProceduralSky-based model.
- [`replace-sprite-voxelizer-research.md`](scratch/replace-sprite-voxelizer-research.md): Assesses alternatives to the in-repo sprite voxelizer implementation.
- [`replace-water-research.md`](scratch/replace-water-research.md): Evaluates water replacement feasibility against existing Phase 4 Gerstner surface lifecycle.
- [`phase1b-drops-projectiles-spec.md`](scratch/phase1b-drops-projectiles-spec.md): Specifies the work needed to implement drops and projectiles in the V1 render path.

## Architecture, platform and phase governance
- [`anatomical-template-spec.md`](scratch/anatomical-template-spec.md): Defines rigged template anatomy as a fallback-safe route for skeletal voxel bodies.
- [`hexagonal-architecture-proposal.md`](scratch/hexagonal-architecture-proposal.md): Proposes clean layering around core, ports, and adapters.
- [`INDEX-reading-order.md`](scratch/INDEX-reading-order.md): Suggests a practical reading order for this scratch corpus.
- [`all-phases-on-audit.md`](scratch/all-phases-on-audit.md): Audits cross-phase ordering and failure coupling when all phase hooks run together.
- [`consolidated-audit-summary.md`](scratch/consolidated-audit-summary.md): Merges findings across many audits and highlights overlap/contradiction.
- [`cross-cutting-concerns.md`](scratch/cross-cutting-concerns.md): Cross-module concerns across bridge, UI, locales, and loading.
- [`phase-patch-manager-deepdive.md`](scratch/phase-patch-manager-deepdive.md): Audits patch-manager coverage and lifecycle expectations.
- [`phase-previews-coverage-audit.md`](scratch/phase-previews-coverage-audit.md): Checks which features have before/after phase preview coverage.
- [`contributor-onboarding-gaps.md`](scratch/contributor-onboarding-gaps.md): Finds onboarding docs gaps for new phase proposals beyond Phase 10.
- [`plan-vs-actual-gap-audit.md`](scratch/plan-vs-actual-gap-audit.md): Compares `PLAN.md` targets with runtime and documentation status.
- [`holistic-project-quality.md`](scratch/holistic-project-quality.md): Ranks overall project-quality gaps by adoption impact.
- [`governance-gaps.md`](scratch/governance-gaps.md): Lists governance-level process gaps and recommended improvements.
- [`docs-vs-code-drift.md`](scratch/docs-vs-code-drift.md): Compares docs against implementation state for inconsistency risk.
- [`agent-team-management-policy.md`](scratch/agent-team-management-policy.md): Defines collaboration branching, review, and operational workflow for multi-agent work.

## Core startup, settings, and compatibility
- [`core-startup-audit.md`](scratch/core-startup-audit.md): Documents startup sequencing and startup-time assumptions across `Core.Init` and `Patch`.
- [`auto-verify-phase1-20260519-154057.png`](scratch/auto-verify-phase1-20260519-154057.png): Artifact showing phase-1 behavior after baseline verification run.
- [`auto-verify-phase1-20260519-153046.png`](scratch/auto-verify-phase1-20260519-153046.png): Artifact showing phase-1 verification input frame.
- [`auto-verify-phase1-postcullfix-155212.png`](scratch/auto-verify-phase1-postcullfix-155212.png): Artifact showing post cull-lift fix output for phase-1.
- [`autotest-cycle-170613.png`](scratch/autotest-cycle-170613.png): Visual capture artifact from auto-test cycle.
- [`autotest-cycle2-171214.png`](scratch/autotest-cycle2-171214.png): Visual capture artifact from a second auto-test cycle.
- [`baseline-voxel-off-160915.png`](scratch/baseline-voxel-off-160915.png): Baseline comparison screenshot before voxel-on behavior.
- [`canonical-phase1-on-160312-0.png`](scratch/canonical-phase1-on-160312-0.png): Canonical phase-1 ON visual snapshot.
- [`canonical-phase1-on-160315-1.png`](scratch/canonical-phase1-on-160315-1.png): Canonical phase-1 ON visual snapshot.
- [`canonical-phase1-on-160319-2.png`](scratch/canonical-phase1-on-160319-2.png): Canonical phase-1 ON visual snapshot.
- [`phase2-debug-172313.png`](scratch/phase2-debug-172313.png): Artifact from phase-2 debug pass.
- [`phase2-on-164210.png`](scratch/phase2-on-164210.png): Visual artifact from phase-2 ON state.
- [`phase2-with-buildings-165416.png`](scratch/phase2-with-buildings-165416.png): Phase-2 visual evidence including buildings.
- [`sanity-cube-163650.png`](scratch/sanity-cube-163650.png): Visual sanity artifact for rendering checks.
- [`user-actors-AFTER-fix-190314.png`](scratch/user-actors-AFTER-fix-190314.png): Visual proof image after actor fix.
- [`user-invisible-actors-183835.png`](scratch/user-invisible-actors-183835.png): Visual artifact for invisible actor investigation.
- [`validate-actors-190921.png`](scratch/validate-actors-190921.png): Visual check image for actor render validation.
- [`validate-actors-after-assembly-fix-192214.png`](scratch/validate-actors-after-assembly-fix-192214.png): Artifact confirming actors after assembly fix.
- [`validate-foreground-192330.png`](scratch/validate-foreground-192330.png): Artifact validating foreground rendering state.
- [`auto-diff-pre-vs-post-cullfix.txt`](scratch/auto-diff-pre-vs-post-cullfix.txt): Notes image comparison files for cull-fix regression evidence.
- [`auto-verify-histogram.txt`](scratch/auto-verify-histogram.txt): Captures image histogram metadata for an automated comparison pass.

## Rendering, voxel, and graphics correctness
- [`actor-vs-building-render-diff.md`](scratch/actor-vs-building-render-diff.md): Traces actor rendering branch divergence from building rendering in voxel submission.
- [`batcher-overflow-audit.md`](scratch/batcher-overflow-audit.md): Validates `MeshInstanceBatcher` chunking behavior for large buckets.
- [`black-voxel-mesh-diagnosis.md`](scratch/black-voxel-mesh-diagnosis.md): Explains missing texture usage causing black voxel output and points to material hookup gaps.
- [`cull-lift-final-verification.md`](scratch/cull-lift-final-verification.md): Final verification status for the cull-lift bug class.
- [`decalpool-audit.md`](scratch/decalpool-audit.md): Evaluates decal pool and particle effect interaction with phase-9 pipelines.
- [`frustum-culler-audit.md`](scratch/frustum-culler-audit.md): Audits `FrustumCuller` behavior and edge cases.
- [`ignore-generic-render-hoist-analysis.md`](scratch/ignore-generic-render-hoist-analysis.md): Assesses generic render-hoist patch behavior and implications.
- [`impostor-lru-followup-audit.md`](scratch/impostor-lru-followup-audit.md): Tracks impostor billboard LRU and cleanup behavior.
- [`lod-system-audit.md`](scratch/lod-system-audit.md): Audits Level-of-Detail logic and behavior.
- [`perp-actors-buildings-audit.md`](scratch/perp-actors-buildings-audit.md): Checks perpendicular actor/building constants usage and runtime impact.
- [`phase3-cull-lift-audit.md`](scratch/phase3-cull-lift-audit.md): Identifies cull-lift mismatches in phase 3 render paths.
- [`phase3-plus-latent-cull-audit.md`](scratch/phase3-plus-latent-cull-audit.md): Extends cull-lift verification across phase 3 adjacent systems.
- [`remaining-2d-gates-audit.md`](scratch/remaining-2d-gates-audit.md): Catalogs additional actor early-exit guards after first gate removals.
- [`voxelrender-refactor-opportunities.md`](scratch/voxelrender-refactor-opportunities.md): Finds duplicated render-path skeletons for refactor candidates.
- [`voxel-size-perf-analysis.md`](scratch/voxel-size-perf-analysis.md): Explains perf pressure from voxel density and batching behavior.
- [`voxel-quality-audit.md`](scratch/voxel-quality-audit.md): Audits mesh generation quality and identifies bottlenecks.
- [`voxel-depth-extrusion-spec.md`](scratch/voxel-depth-extrusion-spec.md): Proposes symmetric voxel extrusion to improve depth appearance.
- [`libs-sprite-to-voxel.md`](scratch/libs-sprite-to-voxel.md): Reviews sprite-to-voxel library landscape for current mesh generation path.
- [`instancing-broken-removal-verification.md`](scratch/instancing-broken-removal-verification.md): Verifies removal of the `InstancingBroken` branch and confirms submit flow behavior.
- [`invisible-voxel-actors-diagnosis.md`](scratch/invisible-voxel-actors-diagnosis.md): Diagnoses additional actor invisibility paths and confirms cull/render gate interactions.
- [`highest-leverage-fix-recommendation.md`](scratch/highest-leverage-fix-recommendation.md): Recommends a world-unload teardown-first approach for deferred destroy and lighting cleanup reliability.
- [`vanilla-2d-regression-audit.md`](scratch/vanilla-2d-regression-audit.md): Audits post-load behavior when all phase flags are off and identifies remaining 2D/3D cross-over leaks.

## Rendering, lighting, atmosphere, and post-processing
- [`day-night-audit.md`](scratch/day-night-audit.md): Audits sun/day-night lifecycle timing and initialization coupling.
- [`day-night-smooth-spec.md`](scratch/day-night-smooth-spec.md): Proposes smoother time-state transitions to reduce stepped lighting artifacts.
- [`fog-implementation-spec.md`](scratch/fog-implementation-spec.md): Defines a fog feature plan driven by saved settings without changing startup contracts.
- [`high-shadows-audit.md`](scratch/high-shadows-audit.md): Verifies current high-shadows behavior and URP-only side conditions.
- [`replace-sky-research.md`](scratch/replace-sky-research.md): (Also in buy-vs-build) details constraints in replacing sky system.
- [`rt-ptgi-dlss-spec.md`](scratch/rt-ptgi-dlss-spec.md): Lays out practical built-in+advanced-lighting strategy for PTGI/DLSS-like enhancements.
- [`stratum-pbr-pipeline-spec.md`](scratch/stratum-pbr-pipeline-spec.md): Defines texture-pack ingest and per-voxel material strategy.
- [`urp-migration-plan.md`](scratch/urp-migration-plan.md): Tracks migration risks and opportunities for URP transition.
- [`water-render-audit.md`](scratch/water-render-audit.md): Validates water pipeline and mesh/shader load costs.
- [`fog/sky/perf`](scratch/rt-ptgi-dlss-spec.md) **(cross-cutting)**: Linked via related post-processing and illumination paths above.

## Vegetation, world geometry, and world-space UI
- [`foliage-phase3-audit.md`](scratch/foliage-phase3-audit.md): Audits crossed-quad foliage material, shader resolution, and lifecycle risks.
- [`tile-height-caching-analysis.md`](scratch/tile-height-caching-analysis.md): Checks repeated tile-height query behavior for caching necessity.
- [`tilemap-to-sphere-audit.md`](scratch/tilemap-to-sphere-audit.md): Audits `TileMapToSphere` patching and world-mode gating.
- [`worldui-renderer-audit.md`](scratch/worldui-renderer-audit.md): Audits nameplate/health UI rig lifecycle and 3D placement behavior.
- [`ui-handlers-audit.md`](scratch/ui-handlers-audit.md): Verifies `WorldSphereTab` and phase UI handler behavior.
- [`journey-integration-trace.md`](scratch/journey-integration-trace.md): Maps how journey execution should be traced across tooling, manifests, and game runs.

## Animation, rigging, and entities
- [`anatomical-template-spec.md`](scratch/anatomical-template-spec.md): (Also in architecture) sets baseline rig template model for actors.
- [`phase6-rig-variety-spec.md`](scratch/phase6-rig-variety-spec.md): Expands rig taxonomy and registry expectations for richer entities.
- [`skeletal-rig-audit.md`](scratch/skeletal-rig-audit.md): Audits RigCache lifecycle and bone matrix upload behavior.
- [`skeletal-position-consistency.md`](scratch/skeletal-position-consistency.md): Confirms phase-6 rig path avoids cull-lift mismatch.
- [`vehicle-rigging-spec.md`](scratch/vehicle-rigging-spec.md): Explores vehicle entity rigging approach and integration points.

## Effects, particles, and post-effect systems
- [`effectpatches9-audit.md`](scratch/effectpatches9-audit.md): Audits `EffectPatches9` and effect lifecycle consistency.
- [`replace-decal-particle-research.md`](scratch/replace-decal-particle-research.md): (Also in buy-vs-build) compares decal/particle implementation strategies.
- [`phase9b-voxel-particles-spec.md`](scratch/phase9b-voxel-particles-spec.md): Specifies voxel-burst effect implementation candidates.
- [`luminance-depth-spec.md`](scratch/luminance-depth-spec.md): Describes luminance/depth spec expectations for atmosphere and voxel visual depth cues.
- [`legs-sprite-locator.md`](scratch/legs-sprite-locator.md): Reviews leg sprite locator logic and potential render-alignment improvements.
- [`L2-unity-test-framework-design.md`](scratch/L2-unity-test-framework-design.md): Proposes an adapter-based Unity-independent test architecture.
- [`mock-unity-layer-design.md`](scratch/mock-unity-layer-design.md): Designs Unity API mocking boundaries for deterministic unit/integration tests.

## Performance, profiling, and reliability
- [`batcher-overflow-audit.md`](scratch/batcher-overflow-audit.md): (Also in rendering) confirms no draw truncation from overflow behavior.
- [`concurrent-collections-research.md`](scratch/concurrent-collections-research.md): Identifies best candidate areas for concurrent data structure upgrades.
- [`constants-mutability-audit.md`](scratch/constants-mutability-audit.md): Verifies mutable/immutable constant usage and safety.
- [`perf-regression-harness-design.md`](scratch/perf-regression-harness-design.md): Defines reproducible perf harness and thresholding.
- [`perf-roadmap-2026-05-19.md`](scratch/perf-roadmap-2026-05-19.md): Consolidates performance recommendations from multi-agent review.
- [`procgen-cache-audit.md`](scratch/procgen-cache-audit.md): Audits cache capacity, eviction, and missing-asset fallbacks.
- [`memory-leak-audit.md`](scratch/memory-leak-audit.md): Catalogs leak risks across runtime systems.
- [`integration-risks-top5.md`](scratch/integration-risks-top5.md): Ranks highest-probability integration risks by blast radius.
- [`mono-drivers-audit.md`](scratch/mono-drivers-audit.md): Audits all active MonoBehaviours and resource cleanup expectations.
- [`memory-leak-audit.md`](scratch/memory-leak-audit.md): References world-unload cleanup paths in reliability risk context.

## Testing, automation, and orchestration
- [`autotest-harness-audit.md`](scratch/autotest-harness-audit.md): Documents current AutoTest coverage limits and state-reset risks.
- [`bdd-validation-frameworks.md`](scratch/bdd-validation-frameworks.md): Surveys BDD/testing frameworks best-fit for WSM3D.
- [`e2e-coverage-gaps.md`](scratch/e2e-coverage-gaps.md): Maps remaining E2E gaps against manifests and tool support.
- [`integration-test-proposals.md`](scratch/integration-test-proposals.md): Proposes additional integration scenarios and execution patterns.
- [`test-coverage-gaps.md`](scratch/test-coverage-gaps.md): Identifies hard gaps in unit coverage for newly landed features.
- [`test-coverage-gaps-holistic.md`](scratch/test-coverage-gaps-holistic.md): Expands on missing test classes and edge-case blind spots.
- [`visual-regression-harness-design.md`](scratch/visual-regression-harness-design.md): Converts screenshot previews into stable regression checks.
- [`test-orchestrator-design.md`](scratch/test-orchestrator-design.md): Designs a parallel test harness for multiple game instances.

## Infrastructure and dev tooling
- [`infra-investment-roadmap.md`](scratch/infra-investment-roadmap.md): Ranks top infrastructure investments for broad downstream impact.
- [`infra-tooling-gaps.md`](scratch/infra-tooling-gaps.md): Surfaces missing but high-value dev tooling gaps.
- [`containerized-test-design.md`](scratch/containerized-test-design.md): Proposes multi-container WorldBox+WSM3D parallel testing architecture.
- [`wsm3d-ps1-cli-audit.md`](scratch/wsm3d-ps1-cli-audit.md): Audits CLI coverage and command routing inconsistencies.
- [`game-bridge-rpc-design.md`](scratch/game-bridge-rpc-design.md): Chooses FastMCP localhost bridge architecture and RPC contract direction.
- [`mcp-server-audit.md`](scratch/mcp-server-audit.md): Audits MCP server operations, command exposure, and transport assumptions.
- [`nml-pin-bump-recommendation.md`](scratch/nml-pin-bump-recommendation.md): Summarizes NeoModLoader pinning and reference strategy guidance.
- [`external-gfx-sources-survey.md`](scratch/external-gfx-sources-survey.md): Documents candidate external graphics sources and integration context.
- [`external-rigging-and-depth-libs.md`](scratch/external-rigging-and-depth-libs.md): Surveys external libraries for rigging and depth inference.
- [`external-voxelization-libs.md`](scratch/external-voxelization-libs.md): Surveys voxelization libraries and compatibility constraints.
- [`headless-rendering-research.md`](scratch/headless-rendering-research.md): Investigates headless Unity/game runtime strategies.
- [`sandbox-wsl-feasibility.md`](scratch/sandbox-wsl-feasibility.md): Explores WSL/Sandbox viability for ephemeral test infrastructure.
- [`steamless-research.md`](scratch/steamless-research.md): Explores CI execution without interactive Steam dependencies.
- [`win-container-engines-comparison.md`](scratch/win-container-engines-comparison.md): Compares orchestrator engine options for Windows host CI.
- [`wsm3d-ps1-cli-audit.md`](scratch/wsm3d-ps1-cli-audit.md): (Also here) checks implemented vs wired CLI entrypoints.
- [`csproj-audit.md`](scratch/csproj-audit.md): Confirms project framework and compatibility constraints.

## Config, security, and persistence
- [`contributor-onboarding-gaps.md`](scratch/contributor-onboarding-gaps.md): (Also listed above) identifies long-term contribution/quality gates.
- [`default-flags-recommendation.md`](scratch/default-flags-recommendation.md): Recommends production-safe default phase and startup flag combinations.
- [`savedsettings-roundtrip-audit.md`](scratch/savedsettings-roundtrip-audit.md): Confirms JSON roundtrip behavior and default compatibility.
- [`save-roundtrip-compat-audit.md`](scratch/save-roundtrip-compat-audit.md): Confirms world save compatibility and settings-only persistence scope.
- [`mod-entry-audit.md`](scratch/mod-entry-audit.md): Audits mod entrypoint behavior, environment checks, and initialization state.
- [`core-startup-audit.md`](scratch/core-startup-audit.md): (Also in architecture) details startup-order tradeoffs.
- [`security-audit.md`](scratch/security-audit.md): Flags MCP/auth and local-surface risk areas.
- [`nml-compat-audit.md`](scratch/nml-compat-audit.md): Confirms NeoModLoader compatibility assumptions and co-install constraints.
- [`mono-drivers-audit.md`](scratch/mono-drivers-audit.md): Also documents lifecycle behavior for settings- and driver-driven systems.
- [`wsm3d-ps1-cli-audit.md`](scratch/wsm3d-ps1-cli-audit.md): Confirms CLI command set gaps and wiring issues around `journey capture`.

## Miscellaneous feature and domain notes
- [`dimension-converter-audit.md`](scratch/dimension-converter-audit.md): Audits map-dimension conversion boundaries and patch assumptions.
- [`game-qol-sources.md`](scratch/game-qol-sources.md): Compiles references for QoL improvements in input, audio, and UX.
- [`harmony-patch-inventory.md`](scratch/harmony-patch-inventory.md): Inventory of Harmony patches and build continuity status.
- [`headless-rendering-research.md`](scratch/headless-rendering-research.md): (Also in infra) evaluates non-interactive runtime possibilities.
- [`polyglot-architecture-survey.md`](scratch/polyglot-architecture-survey.md): Recommends polyglot strategy at process and tooling boundaries.
- [`quantumsprites-isworld3d-audit.md`](scratch/quantumsprites-isworld3d-audit.md): Audits always-on sprite patch behavior with 2D/3D branching.
- [`missing-adrs.md`](scratch/missing-adrs.md): Tracks required architecture decision records missing from current documentation.
- [`camera-resolution-audit.md`](scratch/camera-resolution-audit.md): Audits camera access points and null assumptions across the codebase.
- [`error-handling-audit.md`](scratch/error-handling-audit.md): Summarizes error/logging quality and silent-failure patterns.
- [`mc-texture-pack-importer-spec.md`](scratch/mc-texture-pack-importer-spec.md): Specifies importer workflow for texture-pack integration.
