
using System;
using System.Collections.Concurrent;
using System.Reflection;
using UnityEngine;
using WorldSphereMod.Effects;
using WorldSphereMod.ProcGen;

namespace WorldSphereMod.API
{
    public static class WorldSphereModAPI
    {
        // Upstream v1 surface (preserved verbatim for compatibility) ----------

        /// <summary>True when the upstream 3D-camera + tile-height pipeline is active.</summary>
        public static bool IsWorld3D()
        {
            return Core.IsWorld3D;
        }
        /// <summary>Report the host API version for downstream capability checks.</summary>
        public static string GetVersion()
        {
            return "2.0";
        }
        /// <summary>Return the host capabilities supported by this build.</summary>
        public static string[] GetCapabilities()
        {
            return new[]
            {
                "IsWorld3D",
                "IsModel3D",
                "RegisterCustomMesh",
                "RegisterBuildingRules"
            };
        }
        /// <summary>Return true when the host advertises the named feature.</summary>
        public static bool HasFeature(string Name)
        {
            if (string.IsNullOrEmpty(Name))
            {
                return false;
            }

            switch (Name)
            {
                case "IsWorld3D":
                case "IsModel3D":
                case "RegisterCustomMesh":
                case "RegisterBuildingRules":
                    return true;
                default:
                    return false;
            }
        }
        /// <summary>Mark an actor asset as non-billboarded (faces ground, doesn't rotate to camera).</summary>
        /// <param name="ID">WorldBox actor asset id.</param>
        public static void MakeActorPerp(string ID)
        {
            Constants.PerpActors.Add(ID, true);
        }
        /// <summary>Mark a building asset as non-billboarded (faces ground, doesn't rotate to camera).</summary>
        /// <param name="ID">WorldBox building asset id.</param>
        public static void MakeBuildingPerp(string ID)
        {
            Constants.PerpBuildings.Add(ID, true);
        }
        /// <summary>Mark a projectile asset as non-billboarded (faces ground, doesn't rotate to camera).</summary>
        /// <param name="ID">WorldBox projectile asset id.</param>
        public static void MakeProjectilePerp(string ID)
        {
            Constants.PerpProjectiles.Add(ID, true);
        }
        /// <summary>Register a per-effect render override (orientation, separation, height, ground-snap).</summary>
        /// <param name="ID">WorldBox effect asset id.</param>
        /// <param name="isUpright">If true, the effect renders upright and may rotate to face the camera.</param>
        /// <param name="SeperateSprite">If true, the SpriteRenderer is fully separated so changes do not propagate back.</param>
        /// <param name="ExtraHeight">Additional vertical offset, in world units.</param>
        /// <param name="OnGround">If true, the base height is the tile's terrain height; otherwise 0.</param>
        public static void EditEffect(string ID, bool isUpright, bool SeperateSprite, float ExtraHeight, bool OnGround)
        {
            Constants.EffectDatas.Add(ID, new EffectData(isUpright, SeperateSprite, ExtraHeight, OnGround));
        }
        /// <summary>
        /// Reflectively read a public field on <see cref="SavedSettings"/> by name. Returns
        /// <c>null</c> on missing field or any read failure (logged at <c>Debug.Log</c> level).
        /// External caller-side typed coercion is the caller's responsibility; this method
        /// returns the boxed field value as <c>object</c>.
        /// </summary>
        /// <param name="Name">Field name on <see cref="SavedSettings"/>.</param>
        public static object GetSetting(string Name)
        {
            try
            {
                FieldInfo field = typeof(SavedSettings).GetField(Name, BindingFlags.Instance | BindingFlags.Public)!;
                return field.GetValue(Core.savedSettings)!;
            }
            catch (Exception ex)
            {
                Debug.Log($"[WSM3D] Setting of Name {Name} Not Found! ({ex.Message})");
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

        /// <summary>
        /// Fires once per <c>TimeOfDay</c> tick after the day/night driver advances.
        /// Subscribers receive a normalized phase: 0=midnight, 0.25=dawn, 0.5=noon,
        /// 0.75=dusk. No-op on v1 hosts (the event simply never fires). Subscribe
        /// in your mod's <c>PostInit</c>; unsubscribe in your unload sink.
        /// </summary>
        public static event Action<float>? OnTimeOfDayChanged;
        /// <summary>Internal hook called by <c>TimeOfDay</c> to broadcast the current phase.</summary>
        internal static void RaiseTimeOfDay(float t) => OnTimeOfDayChanged?.Invoke(t);

        /// <summary>
        /// Override the heuristic procgen rules for a building asset. The rules struct
        /// arrives boxed as <see cref="object"/> so <c>WorldSphereAPI.dll</c> stays free
        /// of any reference to this mod's types; we cast here. Callers from external
        /// assemblies can construct an equivalent <c>BuildingRules</c> via reflection
        /// or by referencing this assembly directly.
        /// </summary>
        public static void RegisterBuildingRules(string assetId, object rulesObj)
        {
            if (string.IsNullOrEmpty(assetId)) return;
            if (rulesObj is BuildingRules rules)
            {
                rules.AssetId = assetId;
                BuildingRulesRegistry.Register(assetId, rules);
            }
        }

        internal struct MeshOverride
        {
            public Mesh Mesh;
            public Texture Albedo;
        }
    }
}
