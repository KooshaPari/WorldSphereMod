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

        public string Version = "2.2";
        public bool Is3D = true;
        public bool InvertedCameraMovement = false;
        public bool PerlinNoise = true;
        public bool UpsideDownMovement = true;
        public bool RotateStuffToCamera = true;
        public bool CameraRotatesWithWorld = true;
        public bool FirstPerson = true;
        public float RenderRange = 2;
        public float TileHeight = 1;
        public float BuildingSize = 0.5f;
        public int CurrentShape = 1;

        // --- v2 (WorldSphereMod3D fork) additions ---
        // Phase 1: Voxel actor/item/projectile rendering. When false, falls back
        // to the upstream camera-billboard sprite path. Default ON per ADR-0018
        // beta cascade (README / HANDOFF / PRD); smoke-test with false for the
        // regression gate, then verify true in-game.
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
        public float VoxelScaleMultiplier = 4.0f;
        public bool DebugVoxelOutline = false;
        public bool DebugSanityCube = false;
        public bool DebugSpawnBuildings = false;
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
        public bool CrossedQuadFoliage = false;
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

        // Phase 10: LOD ladder + impostor fallback.
        public float LODScale = 0.5f;
        public float WaterDetail = 1.0f;
        public float FoliageDensity = 1.0f;
        public bool ProfilerDump = false;

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
