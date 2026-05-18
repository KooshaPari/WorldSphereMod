using NeoModLoader.General;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using UnityEngine;
using WorldSphereMod.Effects;

namespace WorldSphereMod.API
{
    public static class WorldSphereModAPI
    {
        // Upstream v1 surface (preserved verbatim for compatibility) ----------

        public static bool IsWorld3D()
        {
            return Core.IsWorld3D;
        }
        public static void MakeActorPerp(string ID)
        {
            Constants.PerpActors.Add(ID, true);
        }
        public static void MakeBuildingPerp(string ID)
        {
            Constants.PerpBuildings.Add(ID, true);
        }
        public static void MakeProjectilePerp(string ID)
        {
            Constants.PerpProjectiles.Add(ID, true);
        }
        public static void EditEffect(string ID, bool isUpright, bool SeperateSprite, float ExtraHeight, bool OnGround)
        {
            Constants.EffectDatas.Add(ID, new EffectData(isUpright, SeperateSprite, ExtraHeight, OnGround));
        }
        public static object GetSetting(string Name, Type Type)
        {
            try
            {
                FieldInfo field = typeof(SavedSettings).GetField(Name);
                return field.GetValue(Core.savedSettings);
            }
            catch (Exception ex)
            {
                Debug.Log($"Setting of Name {Name} and Type {Type} Not Found! ({ex.Message})");
                return null;
            }
        }

        // v2 surface (WorldSphereMod3D fork additions) -------------------------
        //
        // Backwards-compatible: any mod that linked against v1 still works. New
        // entry points let downstream mods override the voxelization / procgen /
        // rigging defaults the fork ships with.

        /// <summary>True once the v2 mesh pipeline (voxel/procgen) is active.</summary>
        public static bool IsModel3D()
        {
            return Core.IsWorld3D
                && Core.savedSettings.VoxelEntities
                && SystemInfo.supportsInstancing;
        }

        internal static readonly ConcurrentDictionary<string, MeshOverride> MeshOverrides
            = new ConcurrentDictionary<string, MeshOverride>();

        /// <summary>
        /// Replace the auto-voxelized mesh for an asset with an author-supplied one.
        /// The Unity types arrive boxed as <see cref="object"/> to keep
        /// <c>WorldSphereAPI.dll</c> free of any Unity reference; we cast here.
        /// </summary>
        public static void RegisterCustomMesh(string assetId, object mesh, object albedo)
        {
            if (string.IsNullOrEmpty(assetId)) return;
            if (!(mesh is Mesh m)) return;
            MeshOverrides[assetId] = new MeshOverride { Mesh = m, Albedo = albedo as Texture };
        }

        /// <summary>Fired whenever the day/night driver advances. Argument: 0..1 (0=midnight, 0.5=noon).</summary>
        public static event Action<float>? OnTimeOfDayChanged;
        internal static void RaiseTimeOfDay(float t) => OnTimeOfDayChanged?.Invoke(t);

        internal struct MeshOverride
        {
            public Mesh Mesh;
            public Texture Albedo;
        }
    }
}
