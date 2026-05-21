using CompoundSpheres;
using NeoModLoader.api;
using NeoModLoader.constants;
using System.IO;
using UnityEngine;
using WorldSphereMod;
    public class Mod : MonoBehaviour, IMod, IStagedLoad, ILocalizable
{
    public ModDeclare GetDeclaration()
    {
        return declare;
    }
    public GameObject GetGameObject()
    {
        return Object;
    }
    public void OnLoad(ModDeclare pModDecl, GameObject pGameObject)
    {
        declare = pModDecl;
        Object = pGameObject;
        WorldSphereMod.Rig.RigDriver.Clear();
        if (!SystemInfo.supportsInstancing)
        {
            throw new IncompatibleHardwareException();
        }
        if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsIndirectArgumentsBuffer)
        {
            Debug.LogWarning("[WSM3D] Compute/IndirectArgs not supported; impostor-only mode.");
            WorldSphereMod.LOD.LodSelector.ImpostorOnlyMode = true;
        }
        WorldSphereMod.Bridge.BridgeServer.EnsureCreated();
        IsAutoTest = System.Environment.GetEnvironmentVariable("WSM3D_AUTOTEST") == "1"
                    || (Core.savedSettings != null && Core.savedSettings.AutoTest);
    }
    public string GetUrl()
    {
        return "https://github.com/MelvinShwuaner?tab=repositories";
    }

    public void Init()
    {
        InitProfiler.Measure("Core.Init", () => Core.Init());
        // Phase 1: per-frame flush driver for batched voxel mesh draw calls.
        // No-op when SavedSettings.VoxelEntities is false (Flush early-returns
        // until a material is resolved on first Submit).
        if (Object != null && Object.GetComponent<WorldSphereMod.Voxel.VoxelFrameDriver>() == null)
        {
            InitProfiler.Measure("AddComponent: VoxelFrameDriver", () =>
            {
                Object.AddComponent<WorldSphereMod.Voxel.VoxelFrameDriver>();
            });
        }
        if (Object != null && Object.GetComponent<WorldSphereMod.Perf.ProfilerFrameDriver>() == null)
        {
            InitProfiler.Measure("AddComponent: ProfilerFrameDriver", () =>
            {
                Object.AddComponent<WorldSphereMod.Perf.ProfilerFrameDriver>();
            });
        }
        if (Object != null && Object.GetComponent<WorldSphereMod.Foliage.WindSwayDriver>() == null)
        {
            InitProfiler.Measure("AddComponent: WindSwayDriver", () =>
            {
                Object.AddComponent<WorldSphereMod.Foliage.WindSwayDriver>();
            });
        }
        // Phase 7 Step 1: rig-tracker MonoBehaviour. EnsureCreated is idempotent
        // and gated on IsWorld3D && WorldspaceUI internally.
        InitProfiler.Measure("EnsureCreated: WorldUIRenderer", () =>
        {
            WorldSphereMod.Worldspace.WorldUIRenderer.EnsureCreated();
        });
        InitProfiler.Measure("EnsureCreated: RuntimeStatsOverlay", () =>
        {
            WorldSphereMod.Worldspace.RuntimeStatsOverlay.EnsureCreated();
        });
        InitProfiler.Measure("EnsureCreated: TimeOfDay", () =>
        {
            WorldSphereMod.Lighting.TimeOfDay.EnsureCreated();
        });
        InitProfiler.Measure("EnsureCreated: ProceduralSky", () =>
        {
            WorldSphereMod.Lighting.ProceduralSky.EnsureCreated();
        });
        InitProfiler.Measure("EnsureCreated: CubemapLighting", () =>
        {
            WorldSphereMod.Lighting.CubemapLighting.EnsureCreated();
        });
        InitProfiler.Measure("EnsureCreated: ColorGradingLUT", () =>
        {
            WorldSphereMod.Lighting.ColorGradingLUT.EnsureCreated();
        });
        InitProfiler.Measure("EnsureCreated: ScreenSpaceAO", () =>
        {
            WorldSphereMod.PostFx.ScreenSpaceAO.ApplySetting(Core.savedSettings != null && Core.savedSettings.SSAOEnabled);
        });
        InitProfiler.Measure("EnsureCreated: ScreenSpaceGI", () =>
        {
            WorldSphereMod.PostFx.ScreenSpaceGI.ApplySetting(Core.savedSettings != null && Core.savedSettings.SSGIEnabled);
        });
        InitProfiler.Measure("EnsureCreated: WeatherDriver", () =>
        {
            WorldSphereMod.Weather.WeatherDriver.EnsureCreated();
        });
    }

public void PostInit()
{
    // AutoScreenshotDriver removed (was untracked + used ScreenCapture not in stripped UnityEngine.dll). Re-add later via Tools/wsm3d-capture/ Rust path.
        Core.PostInit();
        if (!IsAutoTest && Core.savedSettings != null && Core.savedSettings.AutoTest)
        {
            IsAutoTest = true;
        }
        if (Core.savedSettings.DebugSpawnBuildings && Object != null && Object.GetComponent<WorldSphereMod.ProcGen.DebugSpawnBuildingsDriver>() == null) Object.AddComponent<WorldSphereMod.ProcGen.DebugSpawnBuildingsDriver>();
        if (IsAutoTest && Object != null && Object.GetComponent<AutoTestDriver>() == null) Object.AddComponent<AutoTestDriver>();
    }

    public string GetLocaleFilesDirectory(ModDeclare pModDeclare)
    {
        return Path.Combine(pModDeclare.FolderPath, "Locales");
    }
    public static string ModDirectory
    {
        get
        {
            return declare.FolderPath;
        }
    }

    public static GameObject Object;
    public static bool IsAutoTest;
    static ModDeclare declare;
}

