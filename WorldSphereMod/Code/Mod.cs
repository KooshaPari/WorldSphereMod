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
        if (!SystemInfo.supportsInstancing)
        {
            throw new IncompatibleHardwareException();
        }
        if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsIndirectArgumentsBuffer)
        {
            Debug.LogWarning("[WSM3D] Compute/IndirectArgs not supported; impostor-only mode.");
            WorldSphereMod.LOD.LodSelector.ImpostorOnlyMode = true;
        }
    }
    public string GetUrl()
    {
        return "https://github.com/MelvinShwuaner?tab=repositories";
    }

    public void Init()
    {
        Core.Init();
        // Phase 1: per-frame flush driver for batched voxel mesh draw calls.
        // No-op when SavedSettings.VoxelEntities is false (Flush early-returns
        // until a material is resolved on first Submit).
        if (Object != null && Object.GetComponent<WorldSphereMod.Voxel.VoxelFrameDriver>() == null)
        {
            Object.AddComponent<WorldSphereMod.Voxel.VoxelFrameDriver>();
        }
        if (Object != null && Object.GetComponent<WorldSphereMod.Perf.ProfilerFrameDriver>() == null)
        {
            Object.AddComponent<WorldSphereMod.Perf.ProfilerFrameDriver>();
        }
        if (Object != null && Object.GetComponent<WorldSphereMod.Foliage.WindSwayDriver>() == null)
        {
            Object.AddComponent<WorldSphereMod.Foliage.WindSwayDriver>();
        }
        // Phase 7 Step 1: rig-tracker MonoBehaviour. EnsureCreated is idempotent
        // and gated on IsWorld3D && WorldspaceUI internally.
        WorldSphereMod.Worldspace.WorldUIRenderer.EnsureCreated();
        WorldSphereMod.Lighting.TimeOfDay.EnsureCreated();
        WorldSphereMod.Lighting.ProceduralSky.EnsureCreated();
    }

    public void PostInit()
    {
        Core.PostInit();
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
    static ModDeclare declare;
}
