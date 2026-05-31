using System;
namespace WorldSphereMod
{
    public enum SsaoQuality
    {
        Low,
        Medium,
        High
    }

    [Serializable]
public class SavedSettings
{
    public bool AutoScreenshotEnabled = true;
    public float AutoScreenshotIntervalSeconds = 60f;
    public string AutoScreenshotPath = @"C:\Users\koosh\Dev\WorldSphereMod\docs\journeys\scratch\";

        public string Version = "2.3";
        public bool Is3D = true;
        public bool InvertedCameraMovement = false;
        public bool PerlinNoise = true;
        public bool UpsideDownMovement = true;
        public bool RotateStuffToCamera = true;
        public bool CameraRotatesWithWorld = true;
        public bool FirstPerson = true;
        public float RowRange = 2;
        public float RenderRange
        {
            get => RowRange;
            set => RowRange = value;
        }
        public float TileHeight = 1;
        public float BuildingSize = 0.5f;
        public float CameraDefaultStrategyZoomHeight = 10f;
        public float CameraMinSurfaceDistance = 1f;
        public float CameraMaxSurfaceDistance = 60f;
        public float CameraNearClipPlane = 0.5f;
        public float CameraFarClipRadiusMultiplier = 4f;
        public float CameraFarClipPadding = 500f;
        public float CameraTouchDragThreshold = 0.1f;
        public float CameraTouchZoomThreshold = 20f;
        // Default to flat (0) — sphere/cylindrical (1) causes GPU hangs on
        // large maps until sphere-mode performance is resolved.
        public int CurrentShape = 0;

        // --- v2 (WorldSphereMod3D fork) additions ---
        // Phase 1: Voxel actor/item/projectile rendering. When false, falls back
        // to the upstream camera-billboard sprite path.
        public bool VoxelEntities = true;
        // Optional Unity 2022.3 BatchRendererGroup path for batching.
        public bool UseBRG = false;
        // ADR-0008: optional post-mesh Laplacian smoothing for voxel output.
        public bool VoxelMeshSmoothing = false;
        public bool EnableMcPackTextures = false;
        // Set false to use the DrawMeshInstanced fast-path by default.
        // Flip true only when a diagnostic needs guaranteed per-instance rendering.
        public bool ForceFallbackDrawPath = false;
        public int SmoothingIterations = 0;
        // Symmetric Z extrusion depth for greedy Build() and styles that call
        // SpriteVoxelizer.ResolveDepth (pertexel/greedy/extruded, balloon,
        // organicblob, legacy-pertexel, rig BuildPerTexel). Default 8 per
        // docs/journeys/scratch/voxel-depth-extrusion-spec.md. Ignored by lathe
        // (sprite width) and by AssetShapeRegistry "auto" routing.
        public int VoxelSpriteDepth = 8;
        // Luminance depth complement (docs/journeys/scratch/luminance-depth-spec.md).
        // Phase 1 global knobs only; per-sprite tuning deferred. Hybrid DT+luminance
        // in SpriteVoxelizer (BuildBalloon / BuildPerTexel) not wired yet — off until then.
        public bool VoxelLuminanceDepth = false;
        public float VoxelNeutralLuminance = 0.5f;
        public float VoxelShadowRecession = 1.0f;
        public bool VoxelColorTonemap = true;
        // Voxel volume style. "pertexel"/"greedy"/"extruded" use symmetric
        // greedy Build() and honor VoxelSpriteDepth. "balloon"/"organicblob"
        // vary shape but still use ResolveDepth. "lathe" ignores depth setting.
        // "auto" defers to AssetShapeRegistry per sprite name. See spec Known gaps.
        public string VoxelInflationStyle = "pertexel";
        public float VoxelScaleMultiplier = 8.0f;
        // WHY: actor/drop voxel meshes are already sprite-sized in world units; the full 8x
        // terrain VoxelScaleMultiplier made actors gigantic (clipping the camera at max zoom).
        // Effective actor render scale = VoxelScaleMultiplier * ActorVoxelScaleFactor. The old
        // 0.25 gave net 2x (8 * 0.25) — a humanoid was still ~2 terrain tiles tall and ungraspable
        // even at max zoom (user-reported). Dropped to 0.10 → net 0.8x (8 * 0.10), so a humanoid
        // reads at slightly under one tile; the voxel mesh extends above the tile so net 1.0x still
        // looked oversized, hence the small undershoot. Terrain (raw VoxelScaleMultiplier) and
        // buildings/projectiles (raw 8x, do NOT use this factor) are unaffected.
        public float ActorVoxelScaleFactor = 0.10f;
        public bool DebugVoxelOutline = false;
        public bool DebugSanityCube = false;
        public bool DebugSpawnBuildings = false;
        // RENDER-ERROR DIAGNOSTICS: when true, render failures draw a typed in-world ERROR
        // prop (GMod-style) at the failing object + record telemetry. When false, failures
        // are INVISIBLE (voxel-or-invisible policy) but STILL recorded in RenderErrorRegistry
        // / /diag/errors — telemetry is always on; only the visual marker is gated.
        // Default TRUE while we fix rendering; flip false for clean play.
        public bool RenderErrorProps = true;
        // PER-OBJECT DIAGNOSTIC OVERLAY: tags objects with compact render-state so the
        // failure DISTRIBUTION is visible at a glance. Opt-in; off by default.
        public bool RenderDiagOverlay = false;
        // AutoTest set false now that Phase 1+6+10 confirmed via opus —
        // AutoTest's flag-flip cycle leaves the game in non-default state on
        // exit + spams 13×3s timing buckets per launch. Switch to manual
        // re-enable when running phase-budget regression tests.
        public bool AutoTest = false;
        // Phase 2: Procedural building meshes (vs. billboarded building sprites).
        public bool ProceduralBuildings = false;
        // Optional Phase 2 style override: keep the old stylized procgen architecture
        // path instead of voxelizing building sprites directly.
        public bool BuildingStyleProcgen = false;
        // Phase 3: Crossed-quad foliage (vs. billboarded sprite top tiles).
        // WHY default ON: the [Phase] gate skips the FoliageTileRender Harmony patch
        // entirely when this is false, so trees fall through to vanilla 2D. Mirrors
        // VoxelEntities=true so foliage renders 3D out-of-the-box like actors.
        public bool CrossedQuadFoliage = true;
        // ADR-0017 M0: continuous height-field mesh terrain (replaces per-tile quads).
        // Default ON (#201): the corner-averaged + analytic-normal + Perlin-displaced
        // mesh is the smooth-terrain path; OFF made land fall back to blocky per-tile
        // quads (the cube-step regression). The mesh build is shape-agnostic (operates
        // on Rows×Cols + injected shape-aware projectPosition), so it is enabled for all
        // shapes via the gate in Core.ConfigureHeightField.
        public bool UseHeightFieldTerrain = true;
        // Terrain polish: blend biome colors across tile boundaries.
        public bool BiomeBlending = true;
        // Phase 4: Mesh water surface (vs. flat tile color).
        public bool MeshWater = false;
        // Worldspace health bar style: true => 3D mesh bars, false => legacy billboard quads.
        public bool WorldspaceHealth3D = false;
        // Mountain slope smoothing: smooth overlay mesh that blends the upstream
        // blocky terrain around cliff and ridge transitions.
        public bool MountainSlopeSmoothing = false;
        // Phase 5: Directional sun + cascaded shadow maps.
        public bool HighShadows = false;
        // Phase 5b: optional HDR skybox reflections and 256x16 LUT color grading.
        public bool HdrSkybox = false;
        public bool ColorGradingLut = false;
        // Phase 6: Skeletal animation driver for voxel actors.
        public bool SkeletalAnimation = false;
        // ADR-0006: DrawProceduralIndirect GPU skinning (opt-in; scaffold only until
        // VoxelSkinned.shader + per-rig StructuredBuffer path ships). Requires SkeletalAnimation.
        public bool GpuProceduralSkinning = false;
        // Phase 7: Worldspace UI (nameplates, health bars, selection rings).
        public bool WorldspaceUI = false;
        // Phase 7: optional worldspace 3D text labels instead of upstream NameplateText.
        public bool WorldspaceLabel3D = false;
        public float NameplateFadeNear = 10f;
        public float NameplateFadeFar = 30f;
        public float NameplateReferenceDistance = 10f;
        public float NameplateMinScale = 0.25f;
        public float NameplateMaxScale = 4f;
        public float NameplateBaseScale = 0.15f;
        // Phase 8: Day/night cycle + procedural sky + fog.
        public bool DayNightCycle = false;
        public float FogDensity = 0.05f;
        // Tier 5: Forward+ CommandBuffer renderer (docs/specs/forward-plus-renderer-spec.md).
        // Opt-in last-resort path; defaults OFF until depth/color passes ship.
        public bool ForwardPlusRenderer = false;
        // Phase 9: URP post-processing (bloom, color grading, vignette).
        public bool PostFX = false;
        // Phase 9: Built-in pipeline screen-space ambient occlusion (SSAO) pass.
        public bool SSAOEnabled = false;
        public SsaoQuality SSAOQuality = SsaoQuality.Medium;
        // Phase 9: Built-in pipeline screen-space global illumination (SSGI) pass.
        public bool SSGIEnabled = false;
        public bool BloomEnabled = false;
        public bool ACESTonemapping = true;
        public bool ParticleEffects = false;
        public bool WeatherRain = false;
        public bool WeatherSnow = false;
        public bool WeatherLightning = false;

        // First-run experience: set true after the welcome dialog has been shown once.
        public bool HasSeenWelcome = false;

        // Safety: maximum tile count (width*height) for 3D mode. Maps larger
        // than this skip Become3D to prevent GPU hangs. ~316x316 = 100K default.
        public int MaxTilesFor3D = 100000;

        // Phase 10: LOD ladder + impostor fallback.
        public float LODScale = 0.5f;
        public float WaterDetail = 1.0f;
        public float FoliageDensity = 1.0f;
        public bool ProfilerDump = false;

        // In-game IMGUI HUD (top-left) showing FPS, frame ms, draw calls,
        // instances, visible units, voxel cache size, shape, and isWorld3D.
        // Toggle in-game with F8. Default off; opt-in for debugging only.
        public bool DebugHUDVisible = false;
        public bool DebugSpriteRendererSuppression = false;
        public float ImpostorEmissionMultiplier = 1.5f;

        // Per-frame building render budget: process at most this many buildings
        // per frame, cycling through the full visible set across frames.
        // 0 = unlimited (process all). Reduces per-frame cost from O(visible)
        // to O(budget) at the expense of spreading updates over multiple frames.
        public int BuildingRenderBudget = 200;

        // Voxel disk cache: persist voxelized meshes to SQLite so subsequent
        // launches skip the async voxelization queue entirely.
        public bool VoxelDiskCache = true;
        public int VoxelDiskCacheMaxSizeMB = 50;

        // Input-capture substrate: passively record the user's in-game action stream
        // (clicks->tool+tile, camera moves, world create/load, speed changes) to an
        // append-only JSONL session log so flows can be replayed headlessly via the bridge.
        // Default on for now (see docs/adr/ADR-input-capture-substrate.md).
        public bool InputCaptureEnabled = true;

        public static void ApplyLightweightPreset(SavedSettings s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));

            s.VoxelEntities = true;
            s.ProceduralBuildings = false;
            s.CrossedQuadFoliage = false;
            s.BiomeBlending = false;
            s.MeshWater = false;
            s.WorldspaceHealth3D = false;
            s.MountainSlopeSmoothing = false;
            s.HighShadows = false;
            s.HdrSkybox = false;
            s.ColorGradingLut = false;
            s.SkeletalAnimation = false;
            s.GpuProceduralSkinning = false;
            s.WorldspaceUI = false;
            s.WorldspaceLabel3D = false;
            s.DayNightCycle = false;
            s.PostFX = false;
            s.SSAOEnabled = false;
            s.SSGIEnabled = false;
            s.BloomEnabled = false;
            s.ACESTonemapping = false;
            s.ParticleEffects = false;
            s.WeatherRain = false;
            s.DebugSanityCube = false;
            s.ProfilerDump = false;
        }

        public static void ApplyPhaseDefaults(SavedSettings s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));

            s.VoxelEntities = true;
            s.ProceduralBuildings = false;
            s.CrossedQuadFoliage = true; // WHY: gates the foliage patch; off = trees stay vanilla 2D
            s.UseHeightFieldTerrain = true; // #201: smooth corner-averaged terrain mesh (off = cube-step regression)
            // #206: re-apply the actor voxel scale so persisted JSON (which shadows the field
            // default) re-migrates to the smaller net 0.8x actor size. Requires SettingsVersion
            // bump (Core.cs 2.4 -> 2.5) so loadedData.Version mismatch triggers this migration.
            s.ActorVoxelScaleFactor = 0.10f;
            s.MeshWater = false;
            s.WorldspaceHealth3D = false;
            s.MountainSlopeSmoothing = false;
            s.HighShadows = false;
            s.HdrSkybox = false;
            s.ColorGradingLut = false;
            s.SkeletalAnimation = false;
            s.GpuProceduralSkinning = false;
            s.WorldspaceUI = false;
            s.WorldspaceLabel3D = false;
            s.DayNightCycle = false;
            s.ForwardPlusRenderer = false;
            s.PostFX = false;
            s.SSAOEnabled = false;
            s.SSGIEnabled = false;
            s.BloomEnabled = false;
            s.ParticleEffects = false;
            s.WeatherRain = false;
            s.WeatherSnow = false;
            s.WeatherLightning = false;
            s.ProfilerDump = false;
        }

        public static void ApplyFullPreset(SavedSettings s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));

            s.VoxelEntities = true;
            s.ProceduralBuildings = true;
            s.CrossedQuadFoliage = true;
            s.BiomeBlending = true;
            s.MeshWater = true;
            s.WorldspaceHealth3D = true;
            s.MountainSlopeSmoothing = true;
            s.HighShadows = true;
            s.HdrSkybox = true;
            s.ColorGradingLut = true;
            s.SkeletalAnimation = true;
            s.GpuProceduralSkinning = true;
            s.WorldspaceUI = true;
            s.WorldspaceLabel3D = true;
            s.DayNightCycle = true;
            s.PostFX = true;
            s.SSAOEnabled = true;
            s.SSGIEnabled = true;
            s.BloomEnabled = true;
            s.ACESTonemapping = true;
            s.ParticleEffects = true;
            s.WeatherRain = true;
            s.DebugSanityCube = true;
            s.ProfilerDump = true;
        }
    }
}
