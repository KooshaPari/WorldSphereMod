using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Effects;
using WorldSphereMod.Rig;

namespace WorldSphereMod
{
    public static class Constants
    {
        public const int ZDisplacement = 100;


        //square root of 1/2
        public const float HalfRoot = 0.70710678118f;
        //idk
        public const float TileHeightDiffSpeed = 4f;

        public static readonly Quaternion ConstRot = Quaternion.Euler(0, 90, 180);
        public static readonly Quaternion ToUpright = Quaternion.Euler(90, 0, 0);
        public static readonly Quaternion FromUpright = Quaternion.Euler(-90, 0, 0);
        public static readonly ConcurrentDictionary<string, EffectData> EffectDatas = new ConcurrentDictionary<string, EffectData>()
        {
            {"fx_meteorite", new EffectData(false) },
            {"fx_fire_smoke", new EffectData(false) },
            {"fx_antimatter_effect", new EffectData(false) },
            {"fx_napalm_flash", new EffectData(false) },
            {"fx_boulder", new EffectData(true) },
            {"fx_explosion_wave", new EffectData(false) },
            {"fx_tile_effect", new EffectData(false) },
            {"fx_cloud", new EffectData(false, true, 21, false) }
        };
        public static readonly ConcurrentDictionary<string, bool> PerpActors = new ConcurrentDictionary<string, bool>();
        public static readonly ConcurrentDictionary<string, bool> PerpBuildings = new ConcurrentDictionary<string, bool>();
        public static readonly ConcurrentDictionary<string, bool> PerpProjectiles = new ConcurrentDictionary<string, bool>();
        public static readonly Dictionary<string, RigType> ActorRigTypes = new Dictionary<string, RigType>
        {
            ["human"] = RigType.Humanoid,
            ["villager"] = RigType.Humanoid,
            ["swordsman"] = RigType.Humanoid,
            ["archer"] = RigType.Humanoid,
            ["mage"] = RigType.Humanoid,
            ["orc"] = RigType.Humanoid,
            ["elf"] = RigType.Humanoid,
            ["wolf"] = RigType.Quadruped,
            ["bear"] = RigType.Quadruped,
            ["eagle"] = RigType.Bird,
            ["snake"] = RigType.Snake,
            ["spider"] = RigType.Insect,
            ["sand_spider"] = RigType.Insect,
            ["dragon"] = RigType.None,
            ["crabzilla"] = RigType.None,
        };

        public static void RegisterActorRig(string assetId, RigType rig)
        {
            if (string.IsNullOrEmpty(assetId))
            {
                return;
            }

            ActorRigTypes[assetId] = rig;
        }

        public static RigType ResolveActorRig(string assetId)
        {
            if (!string.IsNullOrEmpty(assetId) && ActorRigTypes.TryGetValue(assetId, out RigType rig))
            {
                return rig;
            }

            return RigType.Humanoid;
        }
        public const int SpecialHeight = 4;
        public static float YConst => 1f / (81 / (Core.Sphere.HeightMult));
        public static Vector3 HighlightedZoneSize => new Vector3(1, 1 + (10 * YConst), 1);
        public static Vector3 Zero = Vector3.zero;
    }
}
