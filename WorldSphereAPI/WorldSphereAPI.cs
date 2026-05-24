using System;
using System.Reflection;
delegate bool IsWorld3D();
delegate string GetVersion();
delegate string[] GetCapabilities();
delegate bool HasFeature(string Name);
delegate void MakePerp(string ID);
delegate object GetSetting(string Name);
delegate void EditEffect(string ID, bool IsUpright, bool SeperateSprite, float ExtraHeight, bool OnGround);
delegate bool IsModel3D();
// Unity types are passed as object so this assembly stays free of any
// UnityEngine reference — the WorldSphereMod3D host casts internally.
delegate void RegisterCustomMesh(string assetId, object mesh, object albedo);
delegate void RegisterBuildingRules(string assetId, object rules);
/// <summary>
/// WorldSphereMod API Calller. Compatible with both upstream WorldSphereMod (v1)
/// and the WorldSphereMod3D fork (v2). v2-only members fall back to safe defaults
/// when connected to a v1 host.
/// </summary>
public class WorldSphereAPI
{
    IsWorld3D? is3D;
    GetVersion? getVersion;
    GetCapabilities? getCapabilities;
    HasFeature? hasFeature;
    MakePerp? actor;
    MakePerp? building;
    MakePerp? proj;
    EditEffect? editEffect;
    GetSetting? getSetting;
    IsModel3D? isModel3D;
    RegisterCustomMesh? registerCustomMesh;
    RegisterBuildingRules? registerBuildingRules;
    internal WorldSphereAPI() { }
    /// <summary>
    /// returns true if the world Is 3D
    /// </summary>
    public bool IsWorld3D { get { return is3D!(); } }
    /// <summary>
    /// returns true if the v2 mesh pipeline is active (voxel/procgen meshes,
    /// not just camera-billboarded sprites). False on v1 hosts.
    /// </summary>
    public bool IsModel3D { get { return isModel3D != null && isModel3D(); } }
    /// <summary>
    /// Returns the host API version string when available. Falls back to
    /// <c>unknown</c> on older hosts that do not expose discovery methods.
    /// </summary>
    public string GetVersion() { return getVersion != null ? getVersion() : "unknown"; }
    /// <summary>
    /// Returns the set of host capabilities exposed through the discovery surface.
    /// Older hosts return an empty set.
    /// </summary>
    public string[] GetCapabilities() { return getCapabilities != null ? getCapabilities() : Array.Empty<string>(); }
    /// <summary>
    /// Returns true when the connected host advertises <paramref name="Name"/>.
    /// Older hosts safely return false.
    /// </summary>
    public bool HasFeature(string Name) { return hasFeature != null && hasFeature(Name); }
    /// <summary>
    /// Override the auto-voxelized mesh for a given asset id. No-op on v1 hosts.
    /// </summary>
    /// <param name="assetId">WorldBox asset id (e.g. "human" or "house_human")</param>
    /// <param name="mesh">A <c>UnityEngine.Mesh</c> instance (typed as object to keep this assembly Unity-free)</param>
    /// <param name="albedo">A <c>UnityEngine.Texture</c> instance, or <c>null</c></param>
    public void RegisterCustomMesh(string assetId, object mesh, object albedo)
    {
        registerCustomMesh?.Invoke(assetId, mesh, albedo);
    }
    /// <summary>
    /// Override the procgen heuristic rules for a building asset (Phase 2). Pass a
    /// <c>BuildingRules</c> instance typed as <see cref="object"/> — external callers
    /// can either copy the BuildingRules struct definition locally or use reflection
    /// against the host assembly. No-op on v1 hosts.
    /// </summary>
    /// <param name="assetId">WorldBox building asset id (e.g. "house_human")</param>
    /// <param name="rules">A WorldSphereMod.ProcGen.BuildingRules instance (boxed as object)</param>
    public void RegisterBuildingRules(string assetId, object rules)
    {
        registerBuildingRules?.Invoke(assetId, rules);
    }
    internal WorldSphereAPI(Type WorldSpherePort)
    {
        is3D = (IsWorld3D)Delegate.CreateDelegate(typeof(IsWorld3D), WorldSpherePort.GetMethod("IsWorld3D", BindingFlags.Static | BindingFlags.Public));
        actor = (MakePerp)Delegate.CreateDelegate(typeof(MakePerp), WorldSpherePort.GetMethod("MakeActorPerp", BindingFlags.Static | BindingFlags.Public));
        building = (MakePerp)Delegate.CreateDelegate(typeof(MakePerp), WorldSpherePort.GetMethod("MakeBuildingPerp", BindingFlags.Static | BindingFlags.Public));
        proj = (MakePerp)Delegate.CreateDelegate(typeof(MakePerp), WorldSpherePort.GetMethod("MakeProjectilePerp", BindingFlags.Static | BindingFlags.Public));
        editEffect = (EditEffect)Delegate.CreateDelegate(typeof(EditEffect), WorldSpherePort.GetMethod("EditEffect", BindingFlags.Static | BindingFlags.Public));
        getSetting = (GetSetting)Delegate.CreateDelegate(typeof(GetSetting), WorldSpherePort.GetMethod("GetSetting", BindingFlags.Static | BindingFlags.Public));

        // v2 surface — present only when connected to WorldSphereMod3D.
        TryBindOptional(WorldSpherePort, "GetVersion", out getVersion);
        TryBindOptional(WorldSpherePort, "GetCapabilities", out getCapabilities);
        TryBindOptional(WorldSpherePort, "HasFeature", out hasFeature);
        TryBindOptional(WorldSpherePort, "IsModel3D", out isModel3D);
        TryBindOptional(WorldSpherePort, "RegisterCustomMesh", out registerCustomMesh);
        TryBindOptional(WorldSpherePort, "RegisterBuildingRules", out registerBuildingRules);
    }
    private static void TryBindOptional<T>(Type hostType, string methodName, out T? binding) where T : class
    {
        binding = null;
        MethodInfo? method = hostType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
        if (method == null)
        {
            return;
        }
        try
        {
            binding = Delegate.CreateDelegate(typeof(T), method) as T;
        }
        catch (ArgumentException)
        {
            binding = null;
        }
    }
    /// <summary>
    /// gets a setting
    /// </summary>
    /// <remarks>refer to SavedSettings.cs in the Mod Code to view all the settings</remarks>
    /// <typeparam name="T">the type of the setting, this can be a boolean, float or an integer</typeparam>
    /// <param name="Name">the Name of the setting</param>
    /// <returns></returns>
    public T GetSetting<T>(string Name)
    {
        return (T)getSetting!(Name);
    }
    /// <summary>
    /// Makes a actor with asset <paramref name="ID"/> non upright, it will face towards the ground and not rotate to the camera
    /// </summary>
    /// <param name="ID"></param>
    public void MakeActorNonUpright(string ID)
    {
        actor!(ID);
    }
    /// <summary>
    /// Makes a building with asset <paramref name="ID"/> non upright, it will face towards the ground and not rotate to the camera
    /// </summary>
    /// <param name="ID"></param>
    public void MakeBuildingNonUpright(string ID)
    {
        building!(ID);
    }
    /// <summary>
    /// Makes a projectile with asset <paramref name="ID"/> non upright, it will face towards the ground and not rotate to the camera
    /// </summary>
    /// <param name="ID"></param>
    public void MakeProjectileNonUpright(string ID)
    {
        proj!(ID);
    }
    /// <summary>
    /// edits the data of an effect that worldspheremod uses
    /// </summary>
    /// <param name="ID">the asset ID of the effect</param>
    /// <param name="isUpright">if upright, the effect will be rotated upright and can rotate towards the camera, like nukes for example</param>
    /// <param name="SeperateSprite">if true, worldspheremod will make the spriterenderer of your effect completly seperate, any changes to your effect wll translate over to the spriterenderer, and the spriterenderer will be in 3D space, and have rotations</param>
    /// <param name="ExtraHeight">how high off the ground the effect will be</param>
    /// <param name="OnGround">if true, the base height of the effect is the height of the tile it is on, otherwise the base height is 0</param>
    public void EditEffect(string ID, bool isUpright, bool SeperateSprite = false, float ExtraHeight = 0, bool OnGround = true)
    {
        editEffect!(ID, isUpright, SeperateSprite, ExtraHeight, OnGround);
    }
    /// <summary>
    /// Connects to WorldSphereMod, if it is in the Game
    /// </summary>
    /// <remarks>only use this in post init</remarks>
    /// <returns>true if worldspheremod is detected</returns>
    public static bool Connect(out WorldSphereAPI? API)
    {
        API = null;
        // Try the WorldSphereMod3D fork first, fall back to upstream WorldSphereMod.
        Type WorldSpherePort = Type.GetType("WorldSphereMod.API.WorldSphereModAPI, WorldSphereMod3D, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null")
            ?? Type.GetType("WorldSphereMod.API.WorldSphereModAPI, THE_3D_WORLDBOX_MOD, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
        if (WorldSpherePort != null)
        {
            API = new WorldSphereAPI(WorldSpherePort);
            return true;
        }
        return false;
    }
}
