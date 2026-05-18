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
        if (!SystemInfo.supportsInstancing || !SystemInfo.supportsComputeShaders || !SystemInfo.supportsIndirectArgumentsBuffer)
        {
            throw new IncompatibleHardwareException();
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
