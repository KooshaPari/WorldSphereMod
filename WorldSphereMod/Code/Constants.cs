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
            {"fx_cloud", new EffectData(false, true, 21, false, emitCrossedQuad: true) }
        };
        public static readonly ConcurrentDictionary<string, bool> PerpActors = new ConcurrentDictionary<string, bool>();
        public static readonly ConcurrentDictionary<string, bool> PerpBuildings = new ConcurrentDictionary<string, bool>();
        public static readonly ConcurrentDictionary<string, bool> PerpProjectiles = new ConcurrentDictionary<string, bool>();
        public static readonly Dictionary<string, RigType> ActorRigTypes = new Dictionary<string, RigType>
        {
            // Humanoid races
            ["human"] = RigType.Humanoid,
            ["villager"] = RigType.Humanoid,
            ["swordsman"] = RigType.Humanoid,
            ["archer"] = RigType.Humanoid,
            ["mage"] = RigType.Humanoid,
            ["orc"] = RigType.Humanoid,
            ["elf"] = RigType.Humanoid,
            ["dwarf"] = RigType.Humanoid,
            ["goblin"] = RigType.Humanoid,
            ["skeleton"] = RigType.Humanoid,
            ["zombie"] = RigType.Humanoid,
            ["bandit"] = RigType.Humanoid,
            ["necromancer"] = RigType.Humanoid,
            ["druid"] = RigType.Humanoid,
            ["king"] = RigType.Humanoid,
            ["warrior"] = RigType.Humanoid,
            ["plague_doctor"] = RigType.Humanoid,
            ["demon"] = RigType.Humanoid,
            ["angel"] = RigType.Humanoid,
            ["whiteMage"] = RigType.Humanoid,
            ["evilMage"] = RigType.Humanoid,
            // Quadruped animals
            ["wolf"] = RigType.Quadruped,
            ["bear"] = RigType.Quadruped,
            ["horse"] = RigType.Quadruped,
            ["cow"] = RigType.Quadruped,
            ["sheep"] = RigType.Quadruped,
            ["pig"] = RigType.Quadruped,
            ["dog"] = RigType.Quadruped,
            ["cat"] = RigType.Quadruped,
            ["fox"] = RigType.Quadruped,
            ["deer"] = RigType.Quadruped,
            ["rabbit"] = RigType.Quadruped,
            ["chicken"] = RigType.Quadruped,
            ["turtle"] = RigType.Quadruped,
            ["rhino"] = RigType.Quadruped,
            ["mammoth"] = RigType.Quadruped,
            ["frog"] = RigType.Quadruped,
            ["rat"] = RigType.Quadruped,
            // Bird / flying
            ["bird"] = RigType.Bird,
            ["eagle"] = RigType.Bird,
            ["seagull"] = RigType.Bird,
            ["pigeon"] = RigType.Bird,
            ["bat"] = RigType.Bird,
            // Insect / flying invertebrates
            ["butterfly"] = RigType.Insect,
            ["bee"] = RigType.Insect,
            ["firefly"] = RigType.Insect,
            // Snake
            ["snake"] = RigType.Snake,
            // Special / rigless
            ["sand_spider"] = RigType.None,
            ["dragon"] = RigType.None,
            ["crabzilla"] = RigType.None,
            ["tumor"] = RigType.None,
            ["ufo"] = RigType.None,
        };

        public static void RegisterActorRig(string assetId, RigType rig)
        {
            if (string.IsNullOrEmpty(assetId))
            {
                return;
            }

            ActorRigTypes[assetId] = rig;
        }

        static readonly string[] _humanoidPrefixes = { "human", "elf", "orc", "dwarf", "goblin", "skeleton", "zombie", "bandit", "mage", "warrior", "king", "demon", "angel", "druid", "necromancer" };
        static readonly string[] _quadrupedPrefixes = { "wolf", "bear", "horse", "cow", "sheep", "pig", "dog", "cat", "fox", "deer", "rabbit", "chicken", "turtle", "rhino", "mammoth", "frog", "rat", "fire_elemental_horse" };
        static readonly string[] _birdPrefixes = { "bird", "eagle", "seagull", "pigeon", "bat" };
        static readonly string[] _insectPrefixes = { "butterfly", "bee", "firefly" };
        static readonly string[] _snakePrefixes = { "snake", "worm" };

        public static RigType ResolveActorRig(string assetId)
        {
            if (string.IsNullOrEmpty(assetId))
            {
                return RigType.Humanoid;
            }

            if (ActorRigTypes.TryGetValue(assetId, out RigType rig))
            {
                return rig;
            }

            if (Rig.VehicleShapeHints.IsVehicleAssetId(assetId))
            {
                return RigType.None;
            }

            string lower = assetId.ToLowerInvariant();
            if (MatchesAnyPrefix(lower, _birdPrefixes)) return RigType.Bird;
            if (MatchesAnyPrefix(lower, _insectPrefixes)) return RigType.Insect;
            if (MatchesAnyPrefix(lower, _snakePrefixes)) return RigType.Snake;
            if (MatchesAnyPrefix(lower, _quadrupedPrefixes)) return RigType.Quadruped;
            if (MatchesAnyPrefix(lower, _humanoidPrefixes)) return RigType.Humanoid;

            return RigType.Humanoid;
        }

        static bool MatchesAnyPrefix(string lower, string[] prefixes)
        {
            foreach (string prefix in prefixes)
            {
                if (lower.StartsWith(prefix) || lower.Contains("_" + prefix) || lower.Contains(prefix + "_"))
                {
                    return true;
                }
            }
            return false;
        }
        public const int SpecialHeight = 4;
        public static float YConst => 1f / (81 / (Core.Sphere.HeightMult));
        public static Vector3 HighlightedZoneSize => new Vector3(1, 1 + (10 * YConst), 1);
        public static Vector3 Zero = Vector3.zero;
        public static readonly Quaternion Right = Quaternion.Euler(0, 90, 0);
    }
}
