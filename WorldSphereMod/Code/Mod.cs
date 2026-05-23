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
                InitProfiler.Measure("Core.Init", () => { try { Core.Init(); } catch (System.Exception ex) { Debug.LogError("[WSM3D] Core.Init FAILED: " + ex + System.Environment.NewLine + ex.StackTrace); } });
        bool profileMode = false;
        foreach (var arg in System.Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, "--profile-mode", System.StringComparison.OrdinalIgnoreCase))
            {
                profileMode = true;
                break;
            }
        }
        if (profileMode && !Core.savedSettings.ProfilerDump)
        {
            Core.savedSettings.ProfilerDump = true;
            Core.SaveSettings();
            Debug.Log("[WSM3D] --profile-mode detected; enabling ProfilerDump for overlay and frame profiler.");
        }
        // Phase 1: per-frame flush driver for batched voxel mesh draw calls.
        // No-op when SavedSettings.VoxelEntities is false (Flush early-returns
        // until a material is resolved on first Submit).
        if (Object != null && Object.GetComponent<WorldSphereMod.Voxel.VoxelFrameDriver>() == null)
        {
            InitProfiler.Measure("AddComponent: VoxelFrameDriver", () =>
            {
                try { Object.AddComponent<WorldSphereMod.Voxel.VoxelFrameDriver>(); } catch (System.Exception ex) { Debug.LogError("[WSM3D] VoxelFrameDriver FAILED: " + ex); }
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
        InitProfiler.Measure("EnsureCreated: WorldUIRenderer", () => { try { WorldSphereMod.Worldspace.WorldUIRenderer.EnsureCreated(); } catch (System.Exception ex) { Debug.LogError("[WSM3D] WorldUIRenderer FAILED: " + ex); } });
        InitProfiler.Measure("EnsureCreated: RuntimeStatsOverlay", () => { try { WorldSphereMod.Worldspace.RuntimeStatsOverlay.EnsureCreated(); } catch (System.Exception ex) { Debug.LogError("[WSM3D] RuntimeStatsOverlay FAILED: " + ex); } });
        InitProfiler.Measure("EnsureCreated: TimeOfDay", () => { try { WorldSphereMod.Lighting.TimeOfDay.EnsureCreated(); } catch (System.Exception ex) { Debug.LogError("[WSM3D] TimeOfDay FAILED: " + ex); } });
        InitProfiler.Measure("EnsureCreated: ProceduralSky", () => { try { WorldSphereMod.Lighting.ProceduralSky.EnsureCreated(); } catch (System.Exception ex) { Debug.LogError("[WSM3D] ProceduralSky FAILED: " + ex); } });
        InitProfiler.Measure("EnsureCreated: CubemapLighting", () => { try { WorldSphereMod.Lighting.CubemapLighting.EnsureCreated(); } catch (System.Exception ex) { Debug.LogError("[WSM3D] CubemapLighting FAILED: " + ex); } });
        InitProfiler.Measure("EnsureCreated: ColorGradingLUT", () => { try { WorldSphereMod.Lighting.ColorGradingLUT.EnsureCreated(); } catch (System.Exception ex) { Debug.LogError("[WSM3D] ColorGradingLUT FAILED: " + ex); } });
        InitProfiler.Measure("EnsureCreated: ScreenSpaceAO", () => { try { WorldSphereMod.PostFx.ScreenSpaceAO.ApplySetting(Core.savedSettings != null && Core.savedSettings.SSAOEnabled); } catch (System.Exception ex) { Debug.LogError("[WSM3D] ScreenSpaceAO FAILED: " + ex); } });
        InitProfiler.Measure("EnsureCreated: ScreenSpaceGI", () => { try { WorldSphereMod.PostFx.ScreenSpaceGI.ApplySetting(Core.savedSettings != null && Core.savedSettings.SSGIEnabled); } catch (System.Exception ex) { Debug.LogError("[WSM3D] ScreenSpaceGI FAILED: " + ex); } });
        InitProfiler.Measure("EnsureCreated: WeatherDriver", () => { try { WorldSphereMod.Weather.WeatherDriver.EnsureCreated(); } catch (System.Exception ex) { Debug.LogError("[WSM3D] WeatherDriver FAILED: " + ex); } });
    }

public void PostInit()
{
        // Re-create bridge if it died during scene transition (DontDestroyOnLoad
        // doesn't survive LoadSceneMode.Single). EnsureCreated is idempotent.
        WorldSphereMod.Bridge.BridgeServer.EnsureCreated();
        Core.PostInit();
        if (Object != null && Object.GetComponent<WorldSphereMod.AutoScreenshotDriver>() == null)
        {
            Object.AddComponent<WorldSphereMod.AutoScreenshotDriver>();
        }
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
 
