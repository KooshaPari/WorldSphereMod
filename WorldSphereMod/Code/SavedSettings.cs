using System;
namespace WorldSphereMod
{
    [Serializable]
    public class SavedSettings
    {
        public string Version = "2.0";
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
        public bool VoxelMeshSmoothing = true;
        public int SmoothingIterations = 2;
        // Sprite voxel extrusion depth: 1 keeps the old slab, 3 is the cheapest
        // setting that reads as actual 3D depth at a glance.
        public int VoxelSpriteDepth = 3;
        public float VoxelScaleMultiplier = 6.0f;
        public bool DebugVoxelOutline = false;
        public bool DebugSanityCube = false;
        public bool DebugSpawnBuildings = false;
        public bool AutoTest = false;
        // Phase 2: Procedural building meshes (vs. billboarded building sprites).
        public bool ProceduralBuildings = false;
        // Optional Phase 2 style override: keep the old stylized procgen architecture
        // path instead of voxelizing building sprites directly.
        public bool BuildingStyleProcgen = false;
        // Phase 3: Crossed-quad foliage (vs. billboarded sprite top tiles).
        public bool CrossedQuadFoliage = true;
        // Terrain polish: blend biome colors across tile boundaries.
        public bool BiomeBlending = false;
        // Phase 4: Mesh water surface (vs. flat tile color).
        public bool MeshWater = false;
        // Phase 5: Directional sun + cascaded shadow maps.
        public bool HighShadows = false;
        // Phase 6: Skeletal animation driver for voxel actors.
        public bool SkeletalAnimation = false;
        // Phase 7: Worldspace UI (nameplates, health bars, selection rings).
        public bool WorldspaceUI = true;
        // Phase 8: Day/night cycle + procedural sky + fog.
        public bool DayNightCycle = false;
        // Keep a visible starter density so Phase 8 fog is actually apparent on
        // a fresh reset without requiring a separate hidden slider.
        public float FogDensity = 0.0125f;
        // Phase 9: URP post-processing (bloom, color grading, vignette).
        public bool PostFX = false;
        public bool ParticleEffects = true;
        public bool WeatherRain = false;
        public bool WeatherSnow = false;
        public bool WeatherLightning = false;

        // Phase 10: LOD ladder + impostor fallback.
        public float LODScale = 1.0f;
        public float WaterDetail = 1.0f;
        public float FoliageDensity = 1.0f;
        // Diagnostic: dump per-system frame budget to console once per second.
        public bool ProfilerDump = false;
    }
}

