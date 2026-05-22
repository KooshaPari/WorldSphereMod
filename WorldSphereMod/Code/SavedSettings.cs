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
    public float AutoScreenshotIntervalSeconds = 30f;
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
        // to the upstream camera-billboard sprite path. Defaults OFF during
        // alpha — flip via the in-game settings tab once a build of yours is
        // verified to render voxel meshes correctly. The wiring is in place but
        // the supplied placeholder material is unlit; Phase 5 ships the real
        // lit + shadow-casting shader.
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
        // Sprite voxel extrusion depth: 1 keeps the old slab, 3 is the cheapest
        // setting that reads as actual 3D depth at a glance.
        public int VoxelSpriteDepth = 3;
        public bool VoxelColorTonemap = true;
        // Voxel volume style for sprite inflation. Recognized values:
        // "pertexel" (full slab), "balloon" (distance-based profile),
        // "lathe" (revolved 360° extrusion; depth is forced to sprite width),
        // "extruded" (alias for pertexel for backward compatibility).
        public string VoxelInflationStyle = "pertexel";
        public float VoxelScaleMultiplier = 3.0f;
        public bool DebugVoxelOutline = false;
        public bool DebugSanityCube = false;
        public bool DebugSpawnBuildings = false;
        // AutoTest set false now that Phase 1+6+10 confirmed via opus —
        // AutoTest's flag-flip cycle leaves the game in non-default state on
        // exit + spams 13×3s timing buckets per launch. Switch to manual
        // re-enable when running phase-budget regression tests.
        public bool AutoTest = false;
        // Phase 2: Procedural building meshes (vs. billboarded building sprites).
        // Default ON now that Phase 2 is code-complete (per README) — buildings
        // render as proc-meshes instead of 2D sprites.
        public bool ProceduralBuildings = true;
        // Optional Phase 2 style override: keep the old stylized procgen architecture
        // path instead of voxelizing building sprites directly.
        public bool BuildingStyleProcgen = false;
        // Phase 3: Crossed-quad foliage (vs. billboarded sprite top tiles).
        public bool CrossedQuadFoliage = true;
        // Terrain polish: blend biome colors across tile boundaries.
        public bool BiomeBlending = true;
        // Phase 4: Mesh water surface (vs. flat tile color).
        public bool MeshWater = false;
        // Worldspace health bar style: true => 3D mesh bars, false => legacy billboard quads.
        public bool WorldspaceHealth3D = true;
        // Mountain slope smoothing: smooth overlay mesh that blends the upstream
        // blocky terrain around cliff and ridge transitions.
        public bool MountainSlopeSmoothing = true;
        // Phase 5: Directional sun + cascaded shadow maps.
        public bool HighShadows = true;
        // Phase 5b: optional HDR skybox reflections and 256x16 LUT color grading.
        public bool HdrSkybox = true;
        public bool ColorGradingLut = true;
        // Phase 6: Skeletal animation driver for voxel actors.
        // ON by default: without it the per-texel voxel path renders as a
        // sparse tri-dot mesh (sub-pixel fragments instead of proper limbed
        // humanoid silhouettes). HumanoidRig + SegmentVoxels path gives real
        // body shape with head/torso/arms/legs bones.
        public bool SkeletalAnimation = true;
        // Phase 7: Worldspace UI (nameplates, health bars, selection rings).
        public bool WorldspaceUI = true;
        // Phase 7: optional worldspace 3D text labels instead of upstream NameplateText.
        public bool WorldspaceLabel3D = true;
        // Phase 8: Day/night cycle + procedural sky + fog.
        public bool DayNightCycle = true;
        public float FogDensity = 0.05f;
        // Phase 9: URP post-processing (bloom, color grading, vignette).
        public bool PostFX = true;
        // Phase 9: Built-in pipeline screen-space ambient occlusion (SSAO) pass.
        public bool SSAOEnabled = true;
        public SsaoQuality SSAOQuality = SsaoQuality.Medium;
        // Phase 9: Built-in pipeline screen-space global illumination (SSGI) pass.
        public bool SSGIEnabled = false;
        public bool ParticleEffects = true;
        public bool WeatherRain = true;
        public bool WeatherSnow = false;
        public bool WeatherLightning = false;

        // Phase 10: LOD ladder + impostor fallback.
        public float LODScale = 0.5f;
        public float WaterDetail = 1.0f;
        public float FoliageDensity = 1.0f;
        // Runtime diagnostics overlay (FPS + draw calls + phase-level telemetry).
        // Keep enabled by default for phase 7 runtime visibility in-game.
        public bool ProfilerDump = true;
    }
}

